(function() {
    // === Cross-platform C# bridge ===
    // Uses the standard chrome.webview.postMessage API (supported by WebView2,
    // Avalonia WebView Browser backend via av-webview.mjs polyfill, and
    // Android native WebView via Avalonia's built-in JavaScriptInterface).
    var origLog = console.log, origWarn = console.warn, origError = console.error;

    // JSON.stringify throws on circular objects and BigInt. A console wrapper
    // must never be able to break the caller, so every argument is stringified
    // defensively.
    function argText(a) {
        if (typeof a === 'string') return a;
        try { return JSON.stringify(a); } catch (e) { return String(a); }
    }
    function joinArgs(args) {
        var out = [];
        for (var i = 0; i < args.length; i++) out.push(argText(args[i]));
        return out.join(' ');
    }

    var post = function(level, msg) {
        var data = '[' + level + '] ' + msg;
        // Truncate DIAGNOSTICS (a runaway stack trace) but never an export
        // payload: truncating base64/XML would corrupt the file. The export path
        // has its own, platform-aware size limits.
        var limit = (level === 'EXPORT') ? 32 * 1024 * 1024 : 64 * 1024;
        if (data.length > limit) data = data.slice(0, limit) + '…[truncated]';

        var attempted = false;
        // Try every known Avalonia JS→C# bridge in order. Only the first that
        // succeeds is used; guards keep a throwing bridge from breaking the rest.
        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
            attempted = true;
            try { window.chrome.webview.postMessage(data); return; } catch (e) { /* next */ }
        }
        if (window.AvaloniaWebViewBridge && window.AvaloniaWebViewBridge.postMessage) {
            attempted = true;
            try { window.AvaloniaWebViewBridge.postMessage(data); return; } catch (e) { /* next */ }
        }
        if (typeof window.invokeCSharpAction === 'function') {
            attempted = true;
            try { window.invokeCSharpAction(data); return; } catch (e) { /* next */ }
        }
        if (window.external && typeof window.external.notify === 'function') {
            attempted = true;
            try { window.external.notify(data); return; } catch (e) { /* next */ }
        }
        if (window.webkit && window.webkit.messageHandlers &&
            window.webkit.messageHandlers.postAvWebViewMessage) {
            attempted = true;
            try { window.webkit.messageHandlers.postAvWebViewMessage.postMessage(data); return; } catch (e) { /* next */ }
        }
        // Browser/WASM iframe without any bridge shim → relay to the host page.
        // Guard with window.parent !== window so a top-level page (Android WebView)
        // never self-posts and swallows the message.
        if (window.parent !== window) {
            attempted = true;
            try { window.parent.postMessage(data, '*'); return; } catch (e2) { /* last resort */ }
        }
        // A bridge existed but every call failed. Surface that once per message
        // on the ORIGINAL console (not the wrapped one) so it cannot recurse.
        if (attempted) {
            try { origError('[Bridge] could not deliver a ' + data.length + '-byte message'); } catch (e3) { /* ignore */ }
        }
    };

    // True when any Avalonia JS→C# bridge is reachable, so the page should route
    // navigation through C# instead of falling back to window.open (which on Android
    // WebView navigates the current page and would overwrite the preview).
    function hasBridge() {
        return !!(window.chrome && window.chrome.webview && window.chrome.webview.postMessage) ||
            !!(window.AvaloniaWebViewBridge && window.AvaloniaWebViewBridge.postMessage) ||
            typeof window.invokeCSharpAction === 'function' ||
            !!(window.external && typeof window.external.notify === 'function') ||
            !!(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.postAvWebViewMessage);
    }
    console.log = function() {
        var m = joinArgs(arguments);
        try { origLog.apply(console, arguments); } catch (e) { /* never throw from logging */ }
        post('LOG', m);
    };
    console.warn = function() {
        var m = joinArgs(arguments);
        try { origWarn.apply(console, arguments); } catch (e) { /* never throw from logging */ }
        post('WARN', m);
    };
    console.error = function() {
        var m = joinArgs(arguments);
        try { origError.apply(console, arguments); } catch (e) { /* never throw from logging */ }
        post('ERR', m);
    };

    // === Global error capture — shown within the control instead of silent swallow ===
    // Browser extensions / ad & translate scripts inject code that throws errors
    // unrelated to the preview (e.g. Google Translate's www.google.com/js/th/...).
    // Filter those so the error stream only surfaces errors from the preview itself.
    var EXTERNAL_NOISE = /google\.com|googleapis\.com|gstatic\.com|vimeocdn\.com|player\.vimeo\.com|doubleclick\.net|googletagmanager\.com|google-analytics\.com|translate\.|chrome-extension:\/\/|moz-extension:\/\/|ms-browser-extension:\/\/|safari-extension:\/\//i;
    function isExternalNoise(text) {
        return EXTERNAL_NOISE.test(String(text || ''));
    }

    window.onerror = function(msg, source, line, col, error) {
        // Silently log all errors, no UI overlay.
        // Subscribe to MarkdownView.ErrorOccurred for custom error handling.
        var detail = msg;
        if (source) detail += '\n   at ' + source + ':' + line + ':' + col;
        if (error && error.stack) detail += '\n' + error.stack;
        if (isExternalNoise(detail)) return true; // ignore injected/extension errors

        if (error && !(error instanceof Error)) {
            post('WARN', '[ResourceLoadError] ' + msg);
        } else {
            post('ERR', detail);
        }
        return true;
    };
    window.addEventListener('unhandledrejection', function(e) {
        var detail = e.reason ? (e.reason.message || e.reason.toString()) : 'Unhandled Promise rejection';
        if (isExternalNoise(detail)) return; // ignore injected/extension rejections
        post('ERR', '[UnhandledRejection] ' + detail);
    });

    // Exposed for C# side to invoke (window.showPreviewError(detail)), not auto-triggered by default
    window.showPreviewError = function(detail) {
        var overlay = document.getElementById('error-overlay');
        var body = document.getElementById('error-body');
        if (overlay && body) {
            body.textContent = detail;
            overlay.style.display = 'block';
        }
    };
    window.dismissErrorOverlay = function() {
        var overlay = document.getElementById('error-overlay');
        if (overlay) overlay.style.display = 'none';
    };

    function describeError(e) {
        if (e == null) return 'unknown error';
        if (typeof e === 'string') return e;
        return e.message ? String(e.message) : String(e);
    }

    // ── Disaster recovery: markdown-it itself is missing ────────────────────
    // It is the one critical dependency. If the CDN is blocked, the old code
    // threw while constructing md, which aborted this whole script: no
    // renderMarkdown, no READY, and a 15-second timeout in C#. Install a plain-
    // text renderer instead and signal readiness immediately.
    function installFallbackMode(reason) {
        window.renderMarkdown = function(text) {
            var pv = document.getElementById('preview');
            if (!pv) return;
            text = (text == null) ? '' : String(text);
            pv.innerHTML = '';
            if (!text) { pv.removeAttribute('data-source'); return; }
            pv.setAttribute('data-source', text);

            var note = document.createElement('div');
            note.className = 'render-unavailable';
            note.textContent = '⚠ ' + reason + ' Showing the Markdown source as plain text.';
            var pre = document.createElement('pre');
            pre.className = 'render-fallback';
            pre.textContent = text;
            pv.appendChild(note);
            pv.appendChild(pre);
            console.log('renderMarkdown called, length=' + text.length);
        };
        window.onMarkdownUpdate = window.renderMarkdown;
        window.setPreviewConfig = function(config) {
            if (!config || typeof config !== 'object') return;
            if (typeof config.fontSize === 'number' && isFinite(config.fontSize)) {
                var size = Math.max(1, Math.min(96, config.fontSize));
                document.body.style.fontSize = size + 'px';
            }
            if (typeof config.lineHeight === 'number' && isFinite(config.lineHeight)) {
                document.body.style.lineHeight = Math.max(0.8, Math.min(4, config.lineHeight));
            }
        };
        window.setTheme = function(theme) {
            document.documentElement.className =
                (theme === 'light') ? 'theme-light' : 'theme-dark';
        };
        window.setCustomCss = function() {};
        window.setScrollProgress = function() {};
        window.getScrollProgress = function() { return 0; };
        window.navigateToHeading = function() { return 0; };
        window.getHeadings = function() { return []; };
        window.exportAvailable = function() { return false; };
        window.exportMemberAsync = function(container, kind, format, cb) {
            cb({ ok: false, error: reason + ' Export is unavailable.' });
        };
        window.md = null;
        window.escapeHtml = escapeHtml;

        post('ERR', '[Init] ' + reason);
        post('READY', '');
        console.log('✅ Markdown Preview ready (plain-text fallback)');
    }

    console.log('Markdown Preview initializing');
    console.log('UA: ' + navigator.userAgent);

    if (typeof window.markdownit !== 'function') {
        installFallbackMode('markdown-it failed to load from the CDN.');
        return;
    }

    // ===== Preview configuration =====
    var previewConfig = {
        fontSize: 14,
        lineHeight: 1.6,
        showCodeLanguage: true,
        showCopyButton: true,
        maxCodeBlockHeight: 480,
        // autoScrollOnUpdate: when content changes, follow the newest content to the
        //   bottom (agent-friendly). Disabled while the user is operating the scrollbar.
        // autoScrollCooldown: ms of scrollbar inactivity before auto-scroll resumes.
        autoScrollOnUpdate: true,
        autoScrollCooldown: 2000,
        // placeholder: text shown when the preview has no content; empty = blank.
        placeholder: "",
        // contextMenuEnabled: right-clicking a mermaid / plot / PlantUML member
        // offers "Save as SVG / PNG". Only those three are intercepted; the
        // platform's own menu keeps working everywhere else.
        contextMenuEnabled: true
    };

    function clampNumber(value, min, max, fallback) {
        var n = Number(value);
        if (!isFinite(n)) return fallback;
        return Math.max(min, Math.min(max, n));
    }

    window.setPreviewConfig = function(config) {
        if (!config || typeof config !== 'object') return;

        // C# normally sends well-formed values, but InvokeScript is also public
        // API: a bad config must not paint an invalid CSS value or throw.
        if (config.fontSize !== undefined) {
            previewConfig.fontSize = clampNumber(config.fontSize, 1, 96, previewConfig.fontSize);
            document.body.style.fontSize = previewConfig.fontSize + 'px';
        }
        if (config.lineHeight !== undefined) {
            previewConfig.lineHeight = clampNumber(config.lineHeight, 0.8, 4, previewConfig.lineHeight);
            document.body.style.lineHeight = previewConfig.lineHeight;
        }
        if (config.showCodeLanguage !== undefined) previewConfig.showCodeLanguage = !!config.showCodeLanguage;
        if (config.showCopyButton !== undefined) previewConfig.showCopyButton = !!config.showCopyButton;
        if (config.maxCodeBlockHeight !== undefined) {
            previewConfig.maxCodeBlockHeight =
                Math.round(clampNumber(config.maxCodeBlockHeight, 0, 5000, previewConfig.maxCodeBlockHeight));
        }
        if (config.autoScrollOnUpdate !== undefined) previewConfig.autoScrollOnUpdate = !!config.autoScrollOnUpdate;
        if (config.autoScrollCooldown !== undefined) {
            previewConfig.autoScrollCooldown =
                Math.round(clampNumber(config.autoScrollCooldown, 0, 60000, previewConfig.autoScrollCooldown));
        }
        if (config.placeholder !== undefined) {
            previewConfig.placeholder = String(config.placeholder == null ? '' : config.placeholder)
                .slice(0, 2000);
        }
        if (config.contextMenuEnabled !== undefined) {
            previewConfig.contextMenuEnabled = !!config.contextMenuEnabled;
            if (previewConfig.contextMenuEnabled === false) ctxHide();
        }

        applyCodeBlockMaxHeight();

        // Re-render to apply language label / copy button visibility.
        // Pass followToBottom=false: a config change is not a content update, so it
        // must not yank the user's scroll position to the bottom.
        try {
            var preview = document.getElementById('preview');
            var text = preview ? (preview.getAttribute('data-source') || '') : '';
            if (text) {
                window.renderMarkdown(text, false);
            } else if (config.placeholder !== undefined) {
                // Re-render the empty state so a changed placeholder takes effect.
                window.renderMarkdown('', false);
            }
        } catch (e) {
            console.error('[Config] re-render failed: ' + describeError(e));
        }
        console.log('[Config] updated', JSON.stringify(previewConfig));
    };

    function applyCodeBlockMaxHeight() {
        var style = document.getElementById('cb-max-height-style');
        if (!style) {
            style = document.createElement('style');
            style.id = 'cb-max-height-style';
            document.head.appendChild(style);
        }
        // Store current height in preview config for resize function
        if (previewConfig.maxCodeBlockHeight > 0) {
            style.textContent = '.code-block-wrapper pre { max-height: ' + previewConfig.maxCodeBlockHeight + 'px; overflow-y: auto; }';
        } else {
            style.textContent = '.code-block-wrapper pre { max-height: none; overflow-y: visible; }';
        }
    }

    // Apply once on init
    applyCodeBlockMaxHeight();

    // ===== Per-code-block height adjustment =====
    function increaseCodeHeight(btn) {
        var pre = btn.closest('.code-block-wrapper').querySelector('pre');
        if (!pre) return;
        var cur = parseInt(pre.getAttribute('data-height')) || previewConfig.maxCodeBlockHeight || 480;
        var nxt = cur + 80;
        pre.style.maxHeight = nxt + 'px';
        pre.setAttribute('data-height', nxt);
    }
    function decreaseCodeHeight(btn) {
        var pre = btn.closest('.code-block-wrapper').querySelector('pre');
        if (!pre) return;
        var cur = parseInt(pre.getAttribute('data-height')) || previewConfig.maxCodeBlockHeight || 480;
        var nxt = Math.max(80, cur - 80);
        pre.style.maxHeight = nxt + 'px';
        pre.setAttribute('data-height', nxt);
    }
    window.increaseCodeHeight = increaseCodeHeight;
    window.decreaseCodeHeight = decreaseCodeHeight;

    // ===== Code copy =====
    window.copyCode = function(btn) {
        var wrapper = btn.closest('.code-block-wrapper');
        var code = wrapper ? wrapper.querySelector('code') : null;
        var text = code ? code.textContent : '';
        if (!text) return;
        // Prefer Clipboard API
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function() {
                btn.textContent = '✓ Copied';
                btn.classList.add('copied');
                setTimeout(function() { btn.textContent = 'Copy'; btn.classList.remove('copied'); }, 2000);
            }).catch(function() { fallbackCopy(text, btn); });
        } else {
            fallbackCopy(text, btn);
        }
    };
    function fallbackCopy(text, btn) {
        var ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.left = '-9999px';
        document.body.appendChild(ta); ta.select();
        try {
            document.execCommand('copy');
            btn.textContent = '✓ Copied';
            btn.classList.add('copied');
            setTimeout(function() { btn.textContent = 'Copy'; btn.classList.remove('copied'); }, 2000);
        } catch(e) {}
        document.body.removeChild(ta);
    }

    // ===== Theme management =====
    // Read current theme from the html tag's class rather than hard-coding
    var currentTheme = document.documentElement.className.indexOf('theme-light') >= 0 ? 'light' : 'dark';
    var mermaidInitialized = false;

    /**
     * Run mermaid on all .mermaid elements in the given container.
     * Intentionally no timeout — letting mermaid queue and process freely.
     * On Browser (WASM), mermaid shares the main thread with Avalonia's UI loop,
     * Desktop WebView2 runs in a separate process and is unaffected.
     */
    var mermaidGeneration = 0;
    var mermaidTimer = null;

    /**
     * Run mermaid on all .mermaid elements in the given container.
     * Deferred to the next animation frame so that:
     *  - a back-to-back re-render (Android's safety retry renders the same text twice)
     *    cancels the pending run and only the latest DOM is processed, avoiding mermaid
     *    accessing nodes that a re-render removed (getBoundingClientRect null);
     *  - the container is laid out before mermaid measures it (a zero-size container
     *    at WASM startup can fail dagre layout).
     * Errors from a run superseded by a newer render are stale-node artifacts and are
     * suppressed; real errors still surface as a warning.
     */
    // mermaid.run() takes its whole node list in one go, and the layout work is
    // synchronous: 16 diagrams measured 396ms inside a single callback. Feed it one
    // node at a time and yield a frame between nodes, so no single block grows with
    // the number of diagrams on the page. Per-node runs also isolate failures — a
    // batch rejects as a whole, so one bad diagram used to suppress the rest.
    function runMermaidChunked(nodes, gen, index) {
        var started = nowMs();

        function warn(e) {
            if (gen === mermaidGeneration) console.warn('[Mermaid] Render error: ' + describeError(e));
        }


        function pump() {
            if (gen !== mermaidGeneration || index >= nodes.length) return;
            if (nowMs() - started >= PLOT_FRAME_BUDGET_MS) {
                mermaidTimer = requestAnimationFrame(function () {
                    mermaidTimer = null;
                    started = nowMs();
                    pump();
                });
                return;
            }
            var node = nodes[index++];
            if (!node.isConnected) { pump(); return; }

            var result;
            try {
                result = mermaid.run({ nodes: [node] });
            } catch (e) {
                warn(e);
                pump();
                return;
            }
            if (result && typeof result.then === 'function') {
                result.then(pump, function (e) { warn(e); pump(); });
            } else {
                pump();
            }
        }

        pump();
    }

    // A blocked mermaid CDN used to throw a ReferenceError inside requestAnimationFrame
    // for every diagram. Show one visible, local explanation per diagram and keep
    // the fence source readable instead.
    function markMermaidUnavailable(pre) {
        if (!pre || pre.getAttribute('data-avalon-missing')) return;
        pre.setAttribute('data-avalon-missing', '1');
        var note = document.createElement('div');
        note.className = 'render-unavailable';
        note.textContent = '⚠ Mermaid is unavailable (the library failed to load). ' +
            'The diagram source is shown below.';
        if (pre.parentNode) pre.parentNode.insertBefore(note, pre);
    }

    function runMermaidAll(container) {
        var els = container.querySelectorAll('.mermaid');
        if (els.length === 0) return;

        if (typeof window.mermaid === 'undefined' || typeof window.mermaid.run !== 'function') {
            for (var m = 0; m < els.length; m++) markMermaidUnavailable(els[m]);
            return;
        }

        var gen = ++mermaidGeneration;
        if (mermaidTimer) cancelAnimationFrame(mermaidTimer);
        mermaidTimer = requestAnimationFrame(function() {
            mermaidTimer = null;
            var live = [];
            var nodes = container.querySelectorAll('.mermaid');
            for (var i = 0; i < nodes.length; i++) {
                if (nodes[i].isConnected) live.push(nodes[i]);
            }
            if (live.length === 0) return;
            runMermaidChunked(live, gen, 0);
        });
    }

    function getMermaidThemeVars(theme) {
        if (theme === 'light') {
            return {
                theme: 'base',
                themeVariables: {
                    background: '#ffffff',
                    primaryColor: '#d4e6f9',
                    primaryBorderColor: '#005fb8',
                    primaryTextColor: '#333333',
                    secondaryColor: '#e8f5f0',
                    secondaryBorderColor: '#2b91af',
                    secondaryTextColor: '#333333',
                    tertiaryColor: '#f5f5f5',
                    tertiaryBorderColor: '#d4d4d4',
                    lineColor: '#666666',
                    arrowheadColor: '#005fb8',
                    textColor: '#333333',
                    mainBkg: '#ffffff',
                    nodeBorder: '#005fb8',
                    clusterBkg: '#f5f5f5',
                    clusterBorder: '#d4d4d4',
                    titleColor: '#800000',
                    edgeLabelBackground: '#ffffff',
                    nodeTextColor: '#333333',
                    pieTitleTextColor: '#333333',
                    pieStrokeWidth: '1px',
                    pieStrokeColor: '#d4d4d4',
                    pieSectionTextColor: '#333333',
                    pieLegendTextColor: '#666666',
                    pieOpacity: '0.85',
                    git0: '#e8e8e8',
                    git1: '#d4e6f9',
                    git2: '#e8f5f0',
                    git3: '#fdf6d9',
                    git4: '#f5e8e8',
                    git5: '#e8e8f5',
                    git6: '#f0e8d4',
                    git7: '#ffffff',
                    gitBranchLabelColor: '#333333',
                    gitBranchLabelBg: '#ffffff',
                    commitLabelColor: '#333333',
                    commitLabelBg: '#f5f5f5',
                    tagLabelColor: '#ffffff',
                    tagLabelBg: '#005fb8',
                    tagLabelBorder: '#005fb8',
                    // sequence diagram
                    actorBorder: '#005fb8',
                    actorBkg: '#e8f0fe',
                    actorTextColor: '#333333',
                    actorLineColor: '#666666',
                    signalColor: '#005fb8',
                    signalTextColor: '#000000',
                    labelBoxBkgColor: '#f0f0f0',
                    labelBoxBorderColor: '#d4d4d4',
                    labelTextColor: '#333333',
                    loopTextColor: '#666666',
                    activationBorderColor: '#005fb8',
                    activationBkgColor: '#e8f0fe',
                }
            };
        }
        // dark (default)
        return {
            theme: 'base',
            themeVariables: {
                background: '#1e1e1e',
                primaryColor: '#1e3a5f',
                primaryBorderColor: '#3794ff',
                primaryTextColor: '#d4d4d4',
                secondaryColor: '#1a3a32',
                secondaryBorderColor: '#4ec9b0',
                secondaryTextColor: '#d4d4d4',
                tertiaryColor: '#252526',
                tertiaryBorderColor: '#3c3c3c',
                lineColor: '#888888',
                arrowheadColor: '#3794ff',
                textColor: '#d4d4d4',
                mainBkg: '#1e1e1e',
                nodeBorder: '#569cd6',
                clusterBkg: '#252526',
                clusterBorder: '#3c3c3c',
                titleColor: '#569cd6',
                edgeLabelBackground: '#2d2d2d',
                nodeTextColor: '#d4d4d4',
                pieTitleTextColor: '#d4d4d4',
                pieStrokeWidth: '1px',
                pieStrokeColor: '#3c3c3c',
                pieSectionTextColor: '#d4d4d4',
                pieLegendTextColor: '#888888',
                pieOpacity: '0.85',
                git0: '#2d2d2d',
                git1: '#1e3a5f',
                git2: '#1a3a32',
                git3: '#3a3a1a',
                git4: '#3a1a1a',
                git5: '#1a1a3a',
                git6: '#3a2a1a',
                git7: '#1e1e1e',
                gitBranchLabelColor: '#d4d4d4',
                gitBranchLabelBg: '#252526',
                commitLabelColor: '#d4d4d4',
                commitLabelBg: '#2d2d2d',
                tagLabelColor: '#ffffff',
                tagLabelBg: '#3794ff',
                tagLabelBorder: '#3794ff',
                // sequence diagram
                actorBorder: '#569cd6',
                actorBkg: '#1e3a5f',
                actorTextColor: '#d4d4d4',
                actorLineColor: '#888888',
                signalColor: '#569cd6',
                signalTextColor: '#ffffff',
                labelBoxBkgColor: '#2d2d2d',
                labelBoxBorderColor: '#3c3c3c',
                labelTextColor: '#d4d4d4',
                loopTextColor: '#888888',
                activationBorderColor: '#569cd6',
                activationBkgColor: '#1e3a5f',
            }
        };
    }

    // ===== Function plots (function-plot) =====
    // A ```plot fence carries a raw function-plot options object as JSON; hydrateOne()
    // turns it into an SVG once the element is in the document. Structurally modelled on
    // runMermaidAll — generation counter, rAF deferral, liveness re-query — because the
    // same two hazards apply: Android's safety retry renders the same text twice, and a
    // pass superseded by a newer render must not touch nodes that render removed.
    var plotGeneration = 0;
    var plotTimer = null;

    // Curve colours, drawn from the renderer.css hljs token palette so plots read as part
    // of the same design system. These live in JS rather than GenerateCss because the
    // library has no top-level `color` option (colour is per-datum only), and because a
    // CSS variable with no var() consumer would be reported as a dead knob by the theme
    // audit gate.
    var PLOT_PALETTE_DARK = ['#569cd6', '#4ec9b0', '#d69d85', '#b5cea8',
                             '#dcdcaa', '#c586c0', '#9cdcfe', '#ce9178'];
    var PLOT_PALETTE_LIGHT = ['#005fb8', '#2b91af', '#b35c00', '#4b7a2b',
                              '#8a6d00', '#8b2f8b', '#0d6ea8', '#a04a1f'];

    // The library compiles expressions to JavaScript source and runs them through
    // new Function, so a fence body is an execution vector inside the preview. This
    // excludes every character that could break out of an expression — quotes,
    // backticks, semicolons, braces, brackets, backslash, $, # — while keeping the
    // full expression grammar: comparison and ternary operators are what make
    // piecewise definitions possible (`x > 0 ? x : -x`), and `%` is modulo.
    var PLOT_FN_WHITELIST = /^[0-9A-Za-z_+\-*/^().,\s%?:<>=!]*$/;

    function shallowCopy(o) {
        var out = {};
        for (var k in o) {
            if (Object.prototype.hasOwnProperty.call(o, k)) out[k] = o[k];
        }
        return out;
    }

    // Pick the palette by the brightness of whatever is actually behind the graph, not by
    // the app theme: GenerateCss emits one theme-agnostic container background for both
    // themes, and the Plot* sliders can move it anywhere.
    function plotPaletteFor(el, theme) {
        try {
            var bg = window.getComputedStyle(el).backgroundColor || '';
            var m = bg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
            if (m) {
                var lum = (0.2126 * parseInt(m[1], 10) +
                           0.7152 * parseInt(m[2], 10) +
                           0.0722 * parseInt(m[3], 10)) / 255;
                return lum > 0.5 ? PLOT_PALETTE_LIGHT : PLOT_PALETTE_DARK;
            }
        } catch (e) { /* fall through to the app theme */ }
        return (theme || currentTheme) === 'light' ? PLOT_PALETTE_LIGHT : PLOT_PALETTE_DARK;
    }

    function getFunctionPlotTheme(theme, el) {
        return { palette: plotPaletteFor(el, theme) };
    }

    // Reject anything the whitelist does not cover, before it reaches the evaluator.
    function plotExpressionError(data) {
        if (Object.prototype.toString.call(data) !== '[object Array]') return null;
        var keys = ['fn', 'x', 'y', 'r'];
        for (var i = 0; i < data.length; i++) {
            var d = data[i];
            if (!d || typeof d !== 'object') continue;
            for (var j = 0; j < keys.length; j++) {
                var v = d[keys[j]];
                if (typeof v === 'string' && !PLOT_FN_WHITELIST.test(v)) {
                    return 'rejected expression in data[' + i + '].' + keys[j] + ': ' + v +
                        ' (only numbers, operators and named functions are allowed)';
                }
            }
        }
        return null;
    }

    // The width a plot should draw at when the author gave none. Shared by hydration
    // and by the reflow pass so both always agree.
    //
    // The floor matters: a chart drawn narrower than the pane is fine, one drawn
    // wider is clipped or scrolled. 240 keeps a 320px-wide phone pane (minus
    // padding) inside the viewport while still leaving the axis text legible.
    function plotWidthFor(el) {
        var pad = 0;
        try {
            var cs = window.getComputedStyle(el);
            pad = (parseFloat(cs.paddingLeft) || 0) + (parseFloat(cs.paddingRight) || 0);
        } catch (e) { /* fall back to the default below */ }
        var avail = el.clientWidth ? el.clientWidth - pad : 550;
        // Rounded to whole pixels: that, rather than a dead zone, is what keeps
        // sub-pixel noise from causing redraw churn.
        return Math.round(Math.max(240, Math.min(760, avail > 0 ? avail : 550)));
    }

    // The library scales x and y independently. When both domains are given, the
    // grid cells are only square if the domain spans match the plot area's aspect
    // ratio. Expand whichever axis needs it (keeping the requested range) so the
    // chart fills the area with square cells.
    function squarePlotDomains(opts) {
        // Only grid plots need the square-cell treatment; without a grid the
        // author's requested domains should be shown exactly as written.
        if (!opts.grid) return;
        var xAxis = opts.xAxis ? shallowCopy(opts.xAxis) : {};
        var yAxis = opts.yAxis ? shallowCopy(opts.yAxis) : {};
        var typeX = xAxis.type || 'linear';
        var typeY = yAxis.type || 'linear';
        if (typeX !== 'linear' || typeY !== 'linear') {
            opts.xAxis = xAxis;
            opts.yAxis = yAxis;
            return;
        }

        // These are the library's own margins (internalVars): left 40, right 20,
        // bottom 20, top 20 — or 40 when a title is present.
        var width = Number(opts.width) || 550;
        var height = Number(opts.height) || 350;
        var top = opts.title ? 40 : 20;
        var plotW = width - 40 - 20;
        var plotH = height - top - 20;
        if (!(plotW > 0 && plotH > 0)) {
            opts.xAxis = xAxis;
            opts.yAxis = yAxis;
            return;
        }

        var xDom = Object.prototype.toString.call(xAxis.domain) === '[object Array]' &&
            xAxis.domain.length >= 2 ? xAxis.domain : null;
        var yDom = Object.prototype.toString.call(yAxis.domain) === '[object Array]' &&
            yAxis.domain.length >= 2 ? yAxis.domain : null;
        var xSpan = xDom ? Number(xDom[1]) - Number(xDom[0]) : 0;
        var ySpan = yDom ? Number(yDom[1]) - Number(yDom[0]) : 0;

        if (xDom && yDom && xSpan > 0 && ySpan > 0) {
            var wantX = ySpan * plotW / plotH;
            if (xSpan < wantX) {
                var midX = (Number(xDom[0]) + Number(xDom[1])) / 2;
                xAxis.domain = [midX - wantX / 2, midX + wantX / 2];
            } else {
                var wantY = xSpan * plotH / plotW;
                if (ySpan < wantY) {
                    var midY = (Number(yDom[0]) + Number(yDom[1])) / 2;
                    yAxis.domain = [midY - wantY / 2, midY + wantY / 2];
                }
            }
        } else if (yDom && !xDom && ySpan > 0) {
            // When y is explicit and x is not, the library's x default would not
            // preserve the pixel-per-unit. Derive a centred x range instead.
            var derivedX = ySpan * plotW / plotH;
            xAxis.domain = [-derivedX / 2, derivedX / 2];
        }
        // x explicit, y absent: the library already derives y from xSpan and the
        // plot area, which is the square-cell case.
        opts.xAxis = xAxis;
        opts.yAxis = yAxis;
    }

    function niceStep(raw) {
        if (!(raw > 0) || !isFinite(raw)) return 0;
        var p = Math.pow(10, Math.floor(Math.log(raw) / Math.LN10));
        var m = raw / p;
        return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p;
    }

    function ticksWithin(dom, step) {
        var out = [];
        var start = Math.ceil(dom[0] / step - 1e-9) * step;
        for (var v = start; v <= dom[1] + step * 1e-9; v += step) {
            out.push(Math.abs(v) < step * 1e-9 ? 0 : Number(v.toPrecision(12)));
        }
        return out;
    }

    function replaceAxisTicks(axis, values, pixelFor, horizontal, formatter) {
        var ticks = [];
        for (var i = 0; i < axis.children.length; i++) {
            var child = axis.children[i];
            if (child.classList && child.classList.contains('tick')) ticks.push(child);
        }
        if (!ticks.length) return;
        var template = ticks[0].cloneNode(true);
        for (var j = 0; j < ticks.length; j++) axis.removeChild(ticks[j]);
        for (var k = 0; k < values.length; k++) {
            var g = template.cloneNode(true);
            g.__data__ = values[k];   // keep the d3 binding honest for probes
            var p = pixelFor(values[k]);
            g.setAttribute('transform', horizontal ?
                'translate(' + p + ',0)' : 'translate(0,' + p + ')');
            var t = g.querySelector('text');
            if (t) {
                var label;
                try {
                    label = formatter ? formatter(values[k]) : String(values[k]);
                } catch (e) {
                    label = String(values[k]);
                }
                t.textContent = label;
            }
            axis.appendChild(g);
        }
    }

    // Equal x/y scales still leave rectangular GRID CELLS when d3 picks different
    // tick intervals on the two axes (e.g. every 1 on x and every 0.5 on y). Give
    // both axes the same nice data step so adjacent grid lines are the same pixel
    // distance apart. The chart's own selections are stale after drawing, so the
    // tick groups are replaced directly in the SVG; the line/text template keeps
    // the library's grid styling and label alignment.
    function applySquareGridTicks(chart, opts, el) {
        if (!chart || !chart.meta || !chart.meta.xScale || !chart.meta.yScale) return;
        if (!opts.grid) return;
        var typeX = (opts.xAxis && opts.xAxis.type) || 'linear';
        var typeY = (opts.yAxis && opts.yAxis.type) || 'linear';
        if (typeX !== 'linear' || typeY !== 'linear') return;
        var xScale = chart.meta.xScale, yScale = chart.meta.yScale;
        var xDom = xScale.domain(), yDom = yScale.domain();
        var xSpan = xDom[1] - xDom[0], ySpan = yDom[1] - yDom[0];
        if (!(xSpan > 0 && ySpan > 0)) return;
        var pxX = Math.abs(xScale(1) - xScale(0));
        var pxY = Math.abs(yScale(1) - yScale(0));
        if (!(pxX > 0 && pxY > 0)) return;
        // squarePlotDomains() should already have made these equal; if the
        // library did something else, leave its own ticks alone.
        if (Math.abs(pxX / pxY - 1) > 0.02) return;

        var step = niceStep(Math.max(xSpan, ySpan) / 10);
        if (!(step > 0)) return;
        var xTicks = ticksWithin(xDom, step);
        var yTicks = ticksWithin(yDom, step);
        if (xTicks.length < 2 || yTicks.length < 2) return;

        var svg = el && el.querySelector ? el.querySelector('svg') : null;
        if (!svg) return;
        var xAxis = svg.querySelector('.x.axis');
        var yAxis = svg.querySelector('.y.axis');
        if (!xAxis || !yAxis) return;
        var fmtX = null, fmtY = null;
        try { fmtX = chart.meta.xAxis.tickFormat(); } catch (e) { /* fall back */ }
        try { fmtY = chart.meta.yAxis.tickFormat(); } catch (e) { /* fall back */ }
        replaceAxisTicks(xAxis, xTicks, function(v) { return xScale(v); }, true, fmtX);
        replaceAxisTicks(yAxis, yTicks, function(v) { return yScale(v); }, false, fmtY);

        // Zoom re-renders the library's own axes; reapply after the frame settles
        // so the grid stays square while the user zooms/pans.
        if (opts.disableZoom === false && chart.on && !chart.__squareGridZoomHooked) {
            chart.__squareGridZoomHooked = true;
            chart.on('zoom', function() {
                requestAnimationFrame(function() {
                    applySquareGridTicks(chart, opts, el);
                });
            });
        }
    }

    // Merge order: the user's JSON always wins. Only two things are ever defaulted —
    // a missing width, and a missing disableZoom.
    function buildPlotOptions(parsed, el) {
        var opts = shallowCopy(parsed);

        // A user-supplied id would key the library's static Chart.cache, so two blocks
        // sharing one id would collide and the second would reuse the first's chart.
        delete opts.id;

        // The library's 550 default yields ~9.8px axis text, unreadable next to 14px body
        // text, so fit the container when the user gave no explicit width. Note width and
        // height are the whole SVG including margins (l 40 / r 20 / t 20 or 40 / b 20).
        if (opts.width === undefined || opts.width === null) {
            opts.width = plotWidthFor(el);
        }

        // Keep grid cells square for linear axes, whatever the pane aspect is.
        squarePlotDomains(opts);

        // Apply the theme palette by datum index, unless the user set an explicit colour.
        var src = parsed.data;
        if (Object.prototype.toString.call(src) === '[object Array]') {
            var palette = getFunctionPlotTheme(currentTheme, el).palette;
            opts.data = [];
            for (var i = 0; i < src.length; i++) {
                var d = src[i];
                // Text annotations have no stroke to colour.
                if (d && typeof d === 'object' && !d.color && d.graphType !== 'text') {
                    d = shallowCopy(d);
                    d.color = palette[i % palette.length];
                }
                opts.data.push(d);
            }
        }

        // Default to a static graph; see installPlotWheelGate for why the wheel has to be
        // intercepted separately anyway.
        if (!Object.prototype.hasOwnProperty.call(parsed, 'disableZoom')) {
            opts.disableZoom = true;
        }

        // target as an element, not a selector: drawGraphWrapper() runs
        // select(target).selectAll("svg"), and d3's string overload with no match yields
        // an unusable [[null]] group.
        opts.target = el;
        return opts;
    }

    // function-plot's d3-zoom binding preventDefaults and stopImmediatePropagation's every
    // wheel event over the graph, before its own disableZoom guard runs — so the page
    // cannot scroll while the cursor is over a plot, and the page-level wheel listener
    // never fires either. A capture-phase listener stops the event before it reaches the
    // .zoom-and-drag rect, letting the browser's default (page scroll) proceed. Installed
    // unless the JSON explicitly opts into zoom.
    function installPlotWheelGate(el, parsed) {
        if (parsed.disableZoom === false) return;
        el.addEventListener('wheel', function (e) {
            // The window-level wheel listener that suspends auto-scroll is bubble-phase,
            // so stopping propagation here skips it — register the activity here instead
            // of regressing that behaviour. passive:true only forbids preventDefault; we
            // want the browser's native scroll, so stopPropagation is the whole job.
            markUserScroll();
            e.stopPropagation();
        }, { capture: true, passive: true });
    }

    // Chart.cache is a static map the library never evicts, and each entry pins a whole
    // SVG subtree plus scale/zoom state. renderMarkdown rebuilds #preview.innerHTML on
    // every content update, so without this every render leaks one Chart per plot.
    // functionPlot.Chart is not exported (webpack libraryExport:'default'), so reach the
    // class through the returned instance.
    function prunePlotCache(chart) {
        try {
            var C = chart && chart.constructor;
            var cache = C && C.cache;
            if (!cache) return;
            for (var k in cache) {
                if (!Object.prototype.hasOwnProperty.call(cache, k)) continue;
                var c = cache[k];
                var node = c && c.root && c.root.node ? c.root.node() : null;
                if (!node || !node.isConnected) delete cache[k];
            }
        } catch (e) { /* pruning is best-effort */ }
    }

    // Built with DOM APIs rather than string concatenation, so the raw fence body can
    // never be interpreted as markup.
    function showPlotError(el, message, raw) {
        var pre = document.createElement('pre');
        pre.style.cssText = 'color:#f14c4c;border:1px solid #f14c4c;padding:8px;' +
            'white-space:pre-wrap;text-align:left;';
        pre.textContent = '⚠ Plot render error: ' + message + '\n\n' +
            (raw == null ? '' : String(raw));
        el.textContent = '';
        el.removeAttribute('data-plot');
        el.appendChild(pre);
    }

    // ── Frame-budgeted work ─────────────────────────────────────────────────
    // Hydrating and re-laying-out a page of plots are both synchronous jobs. A page
    // of 27 charts took ~594ms in a single requestAnimationFrame callback, which is a
    // frozen UI — and on the Browser/WASM head the JS thread *is* the Avalonia UI
    // thread, so the whole app stalls, not just the preview. Splitting the work
    // across frames caps any single block; progress stays visible and the page stays
    // interactive.
    var PLOT_FRAME_BUDGET_MS = 40;
    var plotHydrateSlot = { timer: null };
    var plotReflowSlot = { timer: null };

    function nowMs() {
        return (window.performance && window.performance.now)
            ? window.performance.now() : Date.now();
    }

    /**
     * Run worker(item) over the items, stopping each frame once the budget is spent.
     * Completes at least one item per frame, so it always makes progress.
     *
     * getItems is called inside the first frame rather than up front: a back-to-back
     * re-render may have replaced the DOM in between. isStale() lets a superseded pass
     * abandon the rest. onDone receives the accumulated *busy* time, which — unlike
     * wall time — is what the adaptive reflow throttle needs.
     */
    function runInFrames(slot, getItems, worker, isStale, onDone) {
        if (slot.timer) cancelAnimationFrame(slot.timer);
        var items = null, index = 0, busy = 0, finished = false;

        function finish() {
            if (finished) return;
            finished = true;
            if (!onDone) return;
            try { onDone(busy); }
            catch (e) { console.warn('[Frames] completion handler failed: ' + describeError(e)); }
        }

        function step() {
            slot.timer = null;
            try {
                if (isStale && isStale()) return;
                if (!items) {
                    items = getItems() || [];
                    if (!items.length) { finish(); return; }
                }
                var started = nowMs();
                while (index < items.length) {
                    if (isStale && isStale()) return;
                    try {
                        worker(items[index]);
                    } catch (e) {
                        // One bad item must not strand the rest of the pass (and,
                        // for the reflow slot, must not leave its busy flag set).
                        console.warn('[Frames] worker failed: ' + describeError(e));
                    }
                    index++;
                    if (nowMs() - started >= PLOT_FRAME_BUDGET_MS) break;
                }
                busy += nowMs() - started;
                if (index < items.length) {
                    slot.timer = requestAnimationFrame(step);
                    return;
                }
                finish();
            } catch (e) {
                console.warn('[Frames] pass aborted: ' + describeError(e));
                finish();
            }
        }

        slot.timer = requestAnimationFrame(step);
    }

    function hydrateOne(el, gen) {
        var raw = el.getAttribute('data-plot');
        el.removeAttribute('data-plot');   // read exactly once

        var parsed = null;
        try {
            parsed = JSON.parse(raw);
        } catch (e) {
            showPlotError(el, 'invalid JSON — ' + e.message, raw);
            return;
        }
        if (!parsed || typeof parsed !== 'object' ||
            Object.prototype.toString.call(parsed) === '[object Array]') {
            showPlotError(el,
                'the plot body must be a single JSON object, e.g. { "data": [{ "fn": "x^2" }] }',
                raw);
            return;
        }
        if (typeof window.functionPlot !== 'function') {
            showPlotError(el, 'function-plot is not loaded (CDN blocked?)', raw);
            return;
        }
        var exprError = plotExpressionError(parsed.data);
        if (exprError) {
            console.warn('[Plot] ' + exprError);
            showPlotError(el, exprError, raw);
            return;
        }

        var opts, chart;
        try {
            opts = buildPlotOptions(parsed, el);
            chart = window.functionPlot(opts);
        } catch (e2) {
            if (gen !== plotGeneration) return;   // stale-node artefact — stay quiet
            console.warn('[Plot] Render error: ' + describeError(e2));
            showPlotError(el, describeError(e2), raw);
            return;
        }

        // Post-draw decoration is best-effort. A library quirk in the grid/zoom
        // pass must not turn a chart that already drew into an error block.
        try { applySquareGridTicks(chart, opts, el); }
        catch (e3) { console.warn('[Plot] grid adjustment failed: ' + describeError(e3)); }
        try { prunePlotCache(chart); }
        catch (e4) { console.warn('[Plot] cache prune failed: ' + describeError(e4)); }
        try { installPlotWheelGate(el, parsed); }
        catch (e5) { console.warn('[Plot] wheel gate failed: ' + describeError(e5)); }

        // Remembered so the reflow pass can redraw at a new width. The chart is
        // laid out once, from whatever the container measured at this moment —
        // which is not necessarily its final width.
        el._plotOpts = parsed;
        el._plotWidth = plotWidthFor(el);
        try { ensurePlotResizeObserver(); }
        catch (e6) { console.warn('[Plot] resize observer failed: ' + describeError(e6)); }
    }

    // ===== Hydration, budgeted across frames =====
    // Deferred to an animation frame so a back-to-back re-render cancels the pending
    // pass and only the latest DOM is hydrated, and so the container is laid out
    // before clientWidth is read.
    function runFunctionPlotAll(container, followToBottom) {
        if (container.querySelectorAll('.plot-container[data-plot]').length === 0) return;
        var gen = ++plotGeneration;
        if (plotTimer) cancelAnimationFrame(plotTimer);

        runInFrames(plotHydrateSlot,
            function () {
                var out = [];
                var nodes = container.querySelectorAll('.plot-container[data-plot]');
                for (var i = 0; i < nodes.length; i++) {
                    if (nodes[i].isConnected) out.push(nodes[i]);
                }
                return out;
            },
            function (el) { hydrateOne(el, gen); },
            function () { return gen !== plotGeneration; },
            function () {
                if (followToBottom !== false) maybeAutoScroll();
            });
    }

    // ===== Responsive reflow =====
    // function-plot lays a chart out once, from the width it is handed, and the
    // result is a fixed-size <svg>. Nothing re-runs when the pane changes size, so a
    // plot drawn while the pane was still settling (or at its old width) stays wrong:
    // too wide and it is clipped or scrolled, too narrow and it leaves a gap.
    var plotResizeObserver = null;

    // Throttle, not debounce: a debounce only fires once the drag STOPS, which reads
    // as "the chart doesn't follow the pane". Fire immediately on the first change,
    // then keep up at a bounded rate while the pane keeps moving, with a trailing
    // call so the final width is always the one drawn.
    //
    // The floor adapts to how expensive the last pass actually was. A full re-layout
    // measured 347-580ms for 27 charts, which is many times the 60ms base interval —
    // queueing passes at the base rate would saturate the thread. On a page with one
    // or two charts a pass costs ~20ms and the floor never leaves 60ms.
    var PLOT_REFLOW_MIN_MS = 60;
    var plotReflowLast = 0;
    var plotReflowLastBusyMs = 0;
    var plotReflowTimer = null;
    var plotReflowBusy = false;
    var plotReflowQueued = false;

    function reflowPlots() {
        var preview = document.getElementById('preview');
        if (!preview || typeof window.functionPlot !== 'function') return;
        plotReflowBusy = true;

        runInFrames(plotReflowSlot,
            function () {
                var out = [];
                var nodes = preview.querySelectorAll('.plot-container');
                for (var i = 0; i < nodes.length; i++) {
                    var el = nodes[i];
                    if (!el._plotOpts || !el.isConnected) continue;
                    if (plotWidthFor(el) === el._plotWidth) continue;
                    out.push(el);
                }
                return out;
            },
            function (el) {
                // Width is sampled per item, not up front: while the pass runs the
                // pane may still be moving, and each chart should land on the width
                // the pane has when that chart is drawn.
                var w = plotWidthFor(el);
                if (w === el._plotWidth) return;
                try {
                    var opts = buildPlotOptions(el._plotOpts, el);
                    var chart = window.functionPlot(opts);
                    try { applySquareGridTicks(chart, opts, el); }
                    catch (e2) { console.warn('[Plot] reflow grid adjustment failed: ' + describeError(e2)); }
                    try { prunePlotCache(chart); }
                    catch (e3) { console.warn('[Plot] reflow cache prune failed: ' + describeError(e3)); }
                    el._plotWidth = w;
                } catch (e) {
                    console.warn('[Plot] reflow error: ' + describeError(e));
                }
            },
            null,
            function (busyMs) {
                plotReflowBusy = false;
                plotReflowLastBusyMs = busyMs;
                if (plotReflowQueued) {
                    // A change arrived mid-pass. Run once more rather than cancelling:
                    // a restart would redo the charts already drawn and never reach the
                    // ones at the end.
                    plotReflowQueued = false;
                    plotReflowLast = nowMs();
                    reflowPlots();
                }
            });
    }

    function schedulePlotReflow() {
        if (plotReflowBusy) { plotReflowQueued = true; return; }
        var min = Math.max(PLOT_REFLOW_MIN_MS, plotReflowLastBusyMs);
        var since = nowMs() - plotReflowLast;
        if (since >= min) {
            plotReflowLast = nowMs();
            reflowPlots();
            return;
        }
        if (plotReflowTimer) return;   // a trailing call is already queued
        plotReflowTimer = setTimeout(function () {
            plotReflowTimer = null;
            plotReflowLast = nowMs();
            reflowPlots();
        }, min - since);
    }

    function ensurePlotResizeObserver() {
        if (plotResizeObserver || typeof window.ResizeObserver !== 'function') return;
        // Observing #preview rather than each container: the containers are recreated
        // by every render, so per-container observers would need constant re-wiring.
        var preview = document.getElementById('preview');
        if (!preview) return;
        try {
            plotResizeObserver = new window.ResizeObserver(schedulePlotReflow);
            plotResizeObserver.observe(preview);
        } catch (e) {
            // A constructor that exists but refuses (privacy mode / restricted
            // WebView) is not fatal: plots simply stop following pane resizes.
            console.warn('[Plot] ResizeObserver unavailable: ' + describeError(e));
            plotResizeObserver = null;
        }
    }

    // ===== Export a diagram / plot / PlantUML image =====
    // Right-clicking one of the three SVG-ish members offers "Save as SVG / PNG".
    // Math is deliberately absent: KaTeX emits positioned HTML spans, not SVG, so
    // there is no vector artefact to hand out.
    //
    // Everything here is ES5, like the rest of this file.
    var EXPORT_SCALE = 2;

    // An export is the one path where the user is actively waiting. A network
    // fetch or an image decode that never settles would leave the menu disabled
    // forever, so every asynchronous step is bounded.
    var EXPORT_TIMEOUT_MS = 15000;

    // Wrap a callback so it can only fire once. Rasterise/retry paths have both a
    // timeout and an event callback; without this the loser would report again.
    function once(fn) {
        var called = false;
        return function() {
            if (called) return;
            called = true;
            fn.apply(null, arguments);
        };
    }

    // iOS/WebKit refuse canvases above roughly 16M pixels, and the failure mode is
    // a SILENTLY BLANK image -- no exception, a perfectly valid PNG data URL.
    var EXPORT_MAX_PX = 16 * 1024 * 1024;

    // Android delivers JS->C# through addJavascriptInterface, which crosses Binder
    // with a ~1MB per-transaction ceiling. Staying under it means a big PNG is
    // reported as too large instead of vanishing into a TransactionTooLargeException.
    var EXPORT_MAX_B64 = 700 * 1024;

    // Only the properties the plot stylesheet actually moves. Each read forces a
    // style resolution, so the list stays tight.
    var EXPORT_INLINE_PROPS = ['fill', 'fill-opacity', 'stroke', 'stroke-opacity',
                               'stroke-width', 'opacity'];

    var EXPORT_SVG_NS = 'http://www.w3.org/2000/svg';
    var EXPORT_XLINK_NS = 'http://www.w3.org/1999/xlink';

    function exportKindOf(el) {
        if (!el || el.nodeType !== 1 || !el.classList) return null;
        if (el.classList.contains('mermaid-container')) return 'mermaid';
        if (el.classList.contains('plot-container')) return 'plot';
        if (el.classList.contains('puml-container')) return 'puml';
        return null;
    }

    // Walk up from the event target to the member container it belongs to.
    function findExportContainer(node) {
        var el = node;
        while (el && el.nodeType === 1 && el !== document.body) {
            var kind = exportKindOf(el);
            if (kind) return { el: el, kind: kind };
            el = el.parentNode;
        }
        return null;
    }

    // A member that failed to render has no artefact worth exporting -- the plot
    // replaces its SVG with a <pre>, mermaid injects an error diagram, and a
    // PlantUML image that never loaded is a blank box.
    function exportSvgOf(container, kind) {
        if (kind === 'plot') return container.querySelector('svg.function-plot');
        if (kind === 'mermaid') return container.querySelector('svg');
        return null;
    }

    function exportPumlImg(container) {
        var img = container.querySelector('img');
        return (img && img.complete && img.naturalWidth > 0) ? img : null;
    }

    function exportIsAvailable(container, kind) {
        if (kind === 'puml') return !!exportPumlImg(container);
        return !!exportSvgOf(container, kind);
    }

    // {type}-{ordinal}[-{heading slug}], so a Downloads folder full of these is
    // still navigable.
    function exportName(container, kind, ext) {
        var sel = kind === 'mermaid' ? '.mermaid-container'
                : kind === 'plot' ? '.plot-container' : '.puml-container';
        var all = document.querySelectorAll('#preview ' + sel);
        var idx = 1;
        for (var i = 0; i < all.length; i++) {
            if (all[i] === container) { idx = i + 1; break; }
        }
        var slug = '';
        var hs = document.querySelectorAll('#preview h1,#preview h2,#preview h3,' +
            '#preview h4,#preview h5,#preview h6');
        for (var j = 0; j < hs.length; j++) {
            // DOCUMENT_POSITION_PRECEDING: the heading comes before the container.
            if (container.compareDocumentPosition(hs[j]) & 2) {
                slug = hs[j].getAttribute('id') || '';
            }
        }
        return kind + '-' + idx + (slug ? '-' + slug : '') + '.' + ext;
    }

    // The first ancestor with an opaque background. .mermaid-container and
    // .plot-container have none of their own -- they inherit the page background --
    // so a dark-theme export would otherwise be dark ink on transparency, which
    // disappears the moment it lands on a white page.
    function exportBackdrop(el) {
        var node = el;
        while (node && node.nodeType === 1) {
            var bg = '';
            try { bg = window.getComputedStyle(node).backgroundColor || ''; } catch (e) { bg = ''; }
            if (bg && bg !== 'transparent' && !/^rgba\(0,\s*0,\s*0,\s*0\)$/.test(bg)) return bg;
            node = node.parentNode;
        }
        return '#ffffff';
    }

    // Copy the computed presentation values onto the clone, in parallel with the
    // original.
    //
    // Two rules that are easy to get wrong:
    //  * read from the ORIGINAL -- getComputedStyle on a detached node returns
    //    empty strings, so a clone cannot style itself;
    //  * the two trees must stay the same shape, so this runs BEFORE anything is
    //    deleted from the clone.
    //
    // Needed because the plot's colours come from author CSS in renderer.css
    // (stroke: currentColor and friends), which exists precisely to override
    // function-plot's hard-coded black. Serialise without inlining and the axis
    // reverts to black-on-transparent: invisible on the dark theme.
    function exportInlineStyles(origRoot, cloneRoot) {
        var o = [origRoot], c = [cloneRoot], i;
        var oa = origRoot.querySelectorAll('*'), ca = cloneRoot.querySelectorAll('*');
        for (i = 0; i < oa.length; i++) o.push(oa[i]);
        for (i = 0; i < ca.length; i++) c.push(ca[i]);
        if (o.length !== c.length) return;   // shapes diverged; do not guess

        for (i = 0; i < o.length; i++) {
            var cs;
            try { cs = window.getComputedStyle(o[i]); } catch (e) { continue; }
            var color = null;
            for (var p = 0; p < EXPORT_INLINE_PROPS.length; p++) {
                var prop = EXPORT_INLINE_PROPS[p];
                var v = cs.getPropertyValue(prop);
                if (!v) continue;
                if (v === 'currentColor') {
                    if (color === null) color = cs.getPropertyValue('color') || '';
                    v = color;
                }
                if (!v || v === 'inherit') continue;
                c[i].setAttribute(prop, v);
            }
        }
    }

    // Give the root an explicit size and a viewBox.
    // Both are required: width/height give the <img> an intrinsic size (without
    // one it draws at the 300x150 CSS default), and a viewBox makes the vector
    // RE-RENDER at the target resolution instead of being upscaled.
    function exportNormaliseSize(clone) {
        var w = 0, h = 0;
        var vb = clone.getAttribute('viewBox');
        if (vb) {
            var p = vb.replace(/,/g, ' ').split(/\s+/);
            w = parseFloat(p[2]);
            h = parseFloat(p[3]);
        }
        if (!(w > 0 && h > 0)) {
            w = parseFloat(clone.getAttribute('width'));
            h = parseFloat(clone.getAttribute('height'));
            if (!(w > 0 && h > 0)) return null;
            clone.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
        }
        if (!(w > 0 && h > 0)) return null;
        // Drops mermaid's max-width:100%, PlantUML's baked background, and any
        // other root-level sizing hack; both are re-stated explicitly below.
        clone.removeAttribute('style');
        return { w: w, h: h };
    }

    function exportApplyScale(clone, size, scale) {
        var s = scale;
        if (size.w * size.h * s * s > EXPORT_MAX_PX) {
            s = Math.max(1, Math.sqrt(EXPORT_MAX_PX / (size.w * size.h)));
        }
        clone.setAttribute('width', Math.round(size.w * s));
        clone.setAttribute('height', Math.round(size.h * s));
        return { w: size.w, h: size.h, scale: s };
    }

    // Strip everything that belongs to the live page rather than to the picture.
    function exportCleanClone(clone) {
        var junk = clone.querySelectorAll('.tip-x-line,.tip-y-line,.zoom-and-drag,.inner-tip');
        for (var i = junk.length - 1; i >= 0; i--) {
            junk[i].parentNode.removeChild(junk[i]);
        }
        var els = clone.querySelectorAll('*');
        var list = [clone];
        for (var j = 0; j < els.length; j++) list.push(els[j]);
        for (var k = 0; k < list.length; k++) {
            var el = list[k];
            for (var a = el.attributes.length - 1; a >= 0; a--) {
                var name = el.attributes[a].name;
                // A standalone .svg is a live document -- never ship handlers out of
                // a preview, and never ship the renderer's own bookkeeping.
                if (name.indexOf('on') === 0 || name.indexOf('data-') === 0) {
                    el.removeAttribute(name);
                }
            }
        }
    }

    function exportSetTitle(clone, title) {
        var first = clone.firstElementChild;
        if (first && first.tagName && first.tagName.toLowerCase() === 'title') return; // already has one
        var t = document.createElementNS(EXPORT_SVG_NS, 'title');
        t.textContent = title;
        clone.insertBefore(t, clone.firstChild);
    }

    function exportSerialize(clone, title) {
        if (!clone.getAttribute('xmlns')) clone.setAttribute('xmlns', EXPORT_SVG_NS);
        exportSetTitle(clone, title);
        var xml = new XMLSerializer().serializeToString(clone);
        // xlink: must be declared if used, or the whole file fails to parse -- and a
        // failed parse is a blank PNG, not an error. Checked after serialising
        // because the attribute name needs escaping in a selector.
        if (xml.indexOf('xlink:') >= 0 && !clone.getAttribute('xmlns:xlink')) {
            clone.setAttribute('xmlns:xlink', EXPORT_XLINK_NS);
            xml = new XMLSerializer().serializeToString(clone);
        }
        // Labels are routinely CJK; the prolog makes the encoding unambiguous for
        // older consumers. C# writes this out as BOM-less UTF-8, which agrees.
        return '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n' + xml;
    }

    function exportRasterise(svgText, pxW, pxH, bg, cb) {
        var done = once(cb);

        if (!(pxW > 0 && pxH > 0)) { done('the image has no usable size'); return; }
        if (pxW * pxH > EXPORT_MAX_PX) {
            done('the image is too large to rasterise — try SVG instead');
            return;
        }

        // A data: URL does NOT taint the canvas; a blob: URL has engine-specific
        // behaviour and an http(s): URL always taints. Stay on data:.
        // btoa throws on any code point above 0xFF, so CJK labels must be UTF-8
        // encoded first -- mermaid diagrams with Chinese labels are the norm here.
        var url;
        try {
            url = 'data:image/svg+xml;base64,' +
                window.btoa(unescape(encodeURIComponent(svgText)));
        } catch (e0) {
            done('the SVG could not be encoded (' + describeError(e0) + ')');
            return;
        }

        var timer = setTimeout(function () {
            done('the image could not be rasterised (timed out)');
        }, EXPORT_TIMEOUT_MS);

        var img = new Image();
        img.onload = function () {
            clearTimeout(timer);
            try {
                var canvas = document.createElement('canvas');
                canvas.width = pxW;
                canvas.height = pxH;
                var ctx = canvas.getContext('2d');
                if (!ctx) { done('the canvas 2D context is unavailable'); return; }
                ctx.fillStyle = bg;
                ctx.fillRect(0, 0, pxW, pxH);
                ctx.drawImage(img, 0, 0, pxW, pxH);
                var out;
                try {
                    out = canvas.toDataURL('image/png');
                } catch (e) {
                    done('the image could not be rasterised (' + (e && e.name) + ')');
                    return;
                }
                // A zero-sized canvas returns 'data:,' WITHOUT throwing, so the
                // exception handler alone is not a sufficient guard.
                if (!out || out.indexOf('data:image/png') !== 0) {
                    done('the raster came back empty (canvas too large?)');
                    return;
                }
                // Opt-in only: this reads back every pixel, which the render-check
                // probe needs and a normal export does not. It is the only way to
                // catch a silently blank canvas -- toDataURL throws for neither an
                // over-area canvas nor an SVG that drew nothing.
                var ink = -1;
                if (window.__exportInkProbe) {
                    try {
                        var px = ctx.getImageData(0, 0, pxW, pxH).data;
                        var bgR = px[0], bgG = px[1], bgB = px[2];
                        var marked = 0;
                        for (var k = 0; k < px.length; k += 4) {
                            if (Math.abs(px[k] - bgR) > 10 || Math.abs(px[k + 1] - bgG) > 10 ||
                                Math.abs(px[k + 2] - bgB) > 10) marked++;
                        }
                        ink = marked / (px.length / 4);
                    } catch (e3) { ink = -2; }
                }
                done(null, out.substring(out.indexOf(',') + 1), ink);
            } catch (e2) {
                done(describeError(e2));
            }
        };
        img.onerror = function () {
            clearTimeout(timer);
            done('the serialised SVG could not be parsed');
        };
        img.src = url;
    }

    // Build a standalone SVG string for an inline <svg> member.
    function exportInlineSvg(sourceSvg, kind, title) {
        var clone = sourceSvg.cloneNode(true);
        if (kind === 'plot') exportInlineStyles(sourceSvg, clone);
        exportCleanClone(clone);
        var size = exportNormaliseSize(clone);
        if (!size) return null;
        var scaled = exportApplyScale(clone, size, EXPORT_SCALE);
        return { xml: exportSerialize(clone, title), size: scaled };
    }

    // PlantUML is the odd one out twice over.
    //  * Its artefact is a remote <img>, so the SVG is fetched rather than
    //    serialised locally -- plantuml.com sends Access-Control-Allow-Origin: *,
    //    so this works from the preview's origin.
    //  * It is black-on-white ARTWORK. The .puml-dark invert filter is a display
    //    hack, so the export is deliberately the unfiltered original -- and it must
    //    keep its own white background. Applying the container's dark backdrop
    //    here would produce black ink on near-black: an invisible picture.
    function exportPuml(img, cb) {
        var done = once(cb);
        var src = img.getAttribute('src') || '';
        if (!src) { done('the image has no source'); return; }

        // plantuml.com is a third party and does drop requests. Retrying once turns
        // most transient failures into a working export -- and the export is the one
        // path where the user is actively waiting for a result, so a silent failure
        // is worse here than anywhere else.
        var attempt = 0;
        function go() {
            attempt++;
            var controller = (typeof AbortController === 'function') ? new AbortController() : null;
            var timedOut = false;
            var timer = setTimeout(function () {
                timedOut = true;
                if (controller) { try { controller.abort(); } catch (e2) { /* ignore */ } }
                // No AbortController (or a fetch that ignores the signal) must not
                // leave the export pending forever.
                done('could not fetch the PlantUML SVG: timed out');
            }, EXPORT_TIMEOUT_MS);

            var options = controller ? { signal: controller.signal } : undefined;
            fetch(src, options).then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text();
            }).then(function (text) {
                if (text.indexOf('<svg') < 0) throw new Error('the server did not return SVG');
                // A processing instruction some strict SVG consumers reject.
                text = text.replace(/<\?plantuml[^>]*\?>/g, '');
                var host = document.createElement('div');
                host.innerHTML = text;   // detached: nothing here is executed
                var svg = host.querySelector('svg');
                if (!svg) throw new Error('the server response had no <svg>');
                exportCleanClone(svg);
                var size = exportNormaliseSize(svg);
                if (!size) throw new Error('could not determine the image size');
                var scaled = exportApplyScale(svg, size, EXPORT_SCALE);
                clearTimeout(timer);
                done(null, { xml: exportSerialize(svg, 'PlantUML diagram'), size: scaled });
            }).catch(function (e) {
                clearTimeout(timer);
                if (attempt < 2 && !timedOut) {
                    setTimeout(go, 400);
                    return;
                }
                done('could not fetch the PlantUML SVG: ' + describeError(e));
            });
        }
        go();
    }

    /**
     * Produce the bytes for one member.
     * cb(err, { name, source, encoding, data, pxW, pxH })  data is text or base64.
     */
    function exportMember(container, kind, format, cb) {
        var done = once(cb);
        var title = kind + ' diagram';
        try {
            if (kind === 'puml') {
                var img = exportPumlImg(container);
                if (!img) { done('the PlantUML image never loaded'); return; }
                exportPuml(img, function (err, built) {
                    if (err) { done(err); return; }
                    finishExport(container, kind, format, built, title, '#ffffff', done);
                });
                return;
            }

            var svg = exportSvgOf(container, kind);
            if (!svg) { done('this ' + kind + ' did not render'); return; }
            var built = exportInlineSvg(svg, kind, title);
            if (!built) { done('could not determine the image size'); return; }
            finishExport(container, kind, format, built, title, exportBackdrop(container), done);
        } catch (e) {
            done('the export could not be built: ' + describeError(e));
        }
    }

    function finishExport(container, kind, format, built, title, bg, cb) {
        try {
            if (format === 'svg') {
                // Android crosses Binder with a ~1 MB per-transaction ceiling and
                // reports the failure by throwing inside the bridge (which post()
                // cannot recover from). Fail loudly here instead of losing the file.
                if (typeof window.invokeCSharpAction === 'function' &&
                    built.xml.length > EXPORT_MAX_B64) {
                    cb('this image is too large to save as SVG on this platform — ' +
                        'try PNG, or export it on desktop');
                    return;
                }
                cb(null, {
                    name: exportName(container, kind, 'svg'),
                    source: kind,
                    encoding: 'utf8',
                    data: built.xml
                });
                return;
            }

            var pxW = Math.round(built.size.w * built.size.scale);
            var pxH = Math.round(built.size.h * built.size.scale);
            exportRasterise(built.xml, pxW, pxH, bg, function (err, b64, ink) {
                if (err) { cb(err); return; }
                if (b64.length > EXPORT_MAX_B64 && built.size.scale > 1) {
                    // Retry at 1x rather than failing: halving each dimension quarters
                    // the bytes, which clears the Binder ceiling in almost every case.
                    var w2 = Math.round(built.size.w);
                    var h2 = Math.round(built.size.h);
                    exportRasterise(built.xml, w2, h2, bg, function (err2, b64b, ink2) {
                        if (err2) { cb(err2); return; }
                        if (b64b.length > EXPORT_MAX_B64) { cb(TOO_LARGE); return; }
                        cb(null, { name: exportName(container, kind, 'png'),
                            source: kind, encoding: 'base64', data: b64b, ink: ink2 });
                    });
                    return;
                }
                if (b64.length > EXPORT_MAX_B64) { cb(TOO_LARGE); return; }
                cb(null, { name: exportName(container, kind, 'png'),
                    source: kind, encoding: 'base64', data: b64, ink: ink });
            });
        } catch (e) {
            cb('the export could not be finished: ' + describeError(e));
        }
    }

    var TOO_LARGE = 'this image is too large to save as PNG — try SVG instead';

    /** Whether this member produced an artefact worth exporting at all. */
    window.exportAvailable = function (container, kind) {
        return exportIsAvailable(container, kind);
    };

    /** Dismiss the menu; used by the render-check probe after its synthetic event. */
    window.hideExportMenu = ctxHide;

    /**
     * Callback API used by the render-check probe. Reports a summary rather than
     * the payload, so a multi-hundred-KB PNG never enters the report.
     */
    window.exportMemberAsync = function (container, kind, format, cb) {
        exportMember(container, kind, format, function (err, res) {
            if (err) { cb({ ok: false, error: String(err) }); return; }
            var out = {
                ok: true,
                name: res.name,
                encoding: res.encoding,
                bytes: res.encoding === 'base64'
                    ? Math.floor(res.data.length * 0.75)
                    : res.data.length
            };
            // Cheap shape assertions, so the checker does not have to ship the
            // bytes back to C# to prove they are a real image.
            if (res.encoding === 'base64') {
                out.pngMagic = res.data.indexOf('iVBORw0KGgo') === 0;
                if (typeof res.ink === 'number') out.ink = Math.round(res.ink * 10000) / 10000;
            } else {
                out.svgDoc = res.data.indexOf('<svg') >= 0;
                out.svgProlog = res.data.indexOf('<?xml') === 0;
                // Ink coverage cannot tell a correctly-coloured axis from a black
                // one. Sample the stroke values that actually landed in the file:
                // the plot's colours come from author CSS, and if the computed-style
                // pass did not run they revert to function-plot's hard-coded black.
                var seen = {}, samples = [];
                var re = /stroke="([^"]+)"/g, m;
                while ((m = re.exec(res.data)) && samples.length < 6) {
                    if (!seen[m[1]]) { seen[m[1]] = 1; samples.push(m[1]); }
                }
                out.strokeSamples = samples;
            }
            cb(out);
        });
    };

    // ===== Export context menu =====
    var ctxMenu = null;
    var ctxContainer = null;
    var ctxKind = null;
    var ctxBusy = false;
    // Opening the menu is part of a gesture that also generates other events -- on
    // Android a long press fires the touch timer AND a synthetic mousedown/touch
    // sequence whose target is the diagram, not the menu. Without this guard the
    // dismissal handlers treat that as "clicked outside" and the menu flashes open
    // and vanishes.
    var ctxOpenedAt = 0;
    var ctxTouchTimer = null;
    var ctxTouchX = 0, ctxTouchY = 0;

    function ctxHide() {
        if (ctxMenu && ctxMenu.parentNode) ctxMenu.parentNode.removeChild(ctxMenu);
        ctxMenu = null;
        ctxContainer = null;
        ctxKind = null;
    }

    function ctxItem(label, format) {
        var b = document.createElement('button');
        b.className = 'md-ctx-item';
        b.type = 'button';
        b.textContent = label;
        b.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            ctxRun(format);
        });
        return b;
    }

    function ctxBuild() {
        var menu = document.createElement('div');
        menu.className = 'md-ctx-menu';
        menu.setAttribute('role', 'menu');
        menu.appendChild(ctxItem('Save as SVG', 'svg'));
        menu.appendChild(ctxItem('Save as PNG', 'png'));
        return menu;
    }

    function ctxRun(format) {
        if (!ctxContainer || !ctxKind || ctxBusy) return;
        var container = ctxContainer, kind = ctxKind;
        // The preview is rebuilt on every render; the container we captured may
        // already be detached.
        if (!container.isConnected) { ctxHide(); return; }
        ctxBusy = true;
        var menu = ctxMenu;
        if (menu) {
            var items = menu.querySelectorAll('.md-ctx-item');
            for (var i = 0; i < items.length; i++) items[i].disabled = true;
        }
        try {
            exportMember(container, kind, format, function (err, res) {
                ctxBusy = false;
                if (err) {
                    console.warn('[Export] ' + kind + '/' + format + ': ' + err);
                    // The user asked for something that did not work; say so in the
                    // preview rather than only in the console.
                    showExportToast(err);
                    ctxHide();
                    return;
                }
                post('EXPORT', JSON.stringify({
                    v: 1,
                    source: res.source,
                    kind: format,
                    encoding: res.encoding,
                    name: res.name,
                    data: res.data
                }));
                ctxHide();
            });
        } catch (e) {
            // A synchronous throw must not leave ctxBusy latched, or the menu
            // would stay disabled for the rest of the session.
            ctxBusy = false;
            console.warn('[Export] threw: ' + describeError(e));
            showExportToast('the export failed: ' + describeError(e));
            ctxHide();
        }
    }

    // A failed export has nowhere else to show: the console channel is not visible
    // to the user, and this is not a document defect worth a red block.
    function showExportToast(message) {
        ctxHide();
        var t = document.createElement('div');
        t.className = 'md-export-toast';
        t.textContent = message;
        document.body.appendChild(t);
        setTimeout(function () {
            if (t.parentNode) t.parentNode.removeChild(t);
        }, 4000);
    }

    // Keeps the menu inside the viewport.
    function ctxPosition(x, y) {
        var w = ctxMenu.offsetWidth, h = ctxMenu.offsetHeight;
        var vw = window.innerWidth, vh = window.innerHeight;
        if (x + w > vw - 4) x = Math.max(4, vw - w - 4);
        if (y + h > vh - 4) y = Math.max(4, vh - h - 4);
        ctxMenu.style.left = x + 'px';
        ctxMenu.style.top = y + 'px';
    }

    function ctxShow(x, y, container, kind) {
        if (ctxMenu && ctxContainer === container) {
            // Already up for this member. Android reaches here twice for one gesture
            // (long-press timer and synthetic contextmenu); repositioning instead of
            // rebuilding keeps that from flickering.
            ctxPosition(x, y);
            return;
        }
        ctxHide();
        ctxContainer = container;
        ctxKind = kind;
        ctxMenu = ctxBuild();
        document.body.appendChild(ctxMenu);
        ctxPosition(x, y);
        ctxOpenedAt = Date.now();
    }

    function ctxEnabled() {
        return previewConfig.contextMenuEnabled !== false;
    }

    function ctxCancelTouch() {
        if (ctxTouchTimer) { clearTimeout(ctxTouchTimer); ctxTouchTimer = null; }
    }

    // Only intercept over the three exportable members; everywhere else the
    // platform's own menu keeps working.
    function ctxHandleContextMenu(e) {
        // Chromium fires contextmenu on long press, which is the same gesture the
        // touch timer below is waiting for. Whichever arrives first wins; the other
        // must stand down or the menu is opened twice per press.
        ctxCancelTouch();
        if (!ctxEnabled()) return;
        var hit = findExportContainer(e.target);
        if (!hit || !exportIsAvailable(hit.el, hit.kind)) { ctxHide(); return; }
        e.preventDefault();
        ctxShow(e.clientX, e.clientY, hit.el, hit.kind);
    }

    // iOS WKWebView does not synthesise a contextmenu event on long press, so touch
    // needs its own timer.
    function ctxWireTouch() {
        document.addEventListener('touchstart', function (e) {
            if (!ctxEnabled() || e.touches.length !== 1) { ctxCancelTouch(); return; }
            var t = e.touches[0];
            ctxTouchX = t.clientX;
            ctxTouchY = t.clientY;
            ctxCancelTouch();
            ctxTouchTimer = setTimeout(function () {
                ctxTouchTimer = null;
                var hit = findExportContainer(e.target);
                if (!hit || !exportIsAvailable(hit.el, hit.kind)) return;
                ctxShow(ctxTouchX, ctxTouchY, hit.el, hit.kind);
            }, 500);
        }, { passive: true });   // never preventDefault here: it would kill scrolling

        document.addEventListener('touchmove', function (e) {
            if (!ctxTouchTimer) return;
            var t = e.touches[0];
            if (!t) { ctxCancelTouch(); return; }
            if (Math.abs(t.clientX - ctxTouchX) > 10 ||
                Math.abs(t.clientY - ctxTouchY) > 10) ctxCancelTouch();
        }, { passive: true });
        document.addEventListener('touchend', ctxCancelTouch, { passive: true });
        document.addEventListener('touchcancel', ctxCancelTouch, { passive: true });
    }

    // True while the gesture that opened the menu is still producing events.
    function ctxJustOpened() {
        return Date.now() - ctxOpenedAt < 600;
    }

    function ctxWireOnce() {
        if (window._ctxWired) return;
        window._ctxWired = true;

        document.addEventListener('contextmenu', ctxHandleContextMenu);
        ctxWireTouch();

        // Dismissal. The wheel listener MUST be capture-phase on window: over a
        // plot, installPlotWheelGate() already registers a capture-phase
        // stopPropagation on the container, so a bubble-phase listener here would
        // never fire and the menu would sit open while the page scrolled.
        window.addEventListener('wheel', function () { ctxHide(); },
            { capture: true, passive: true });
        document.addEventListener('mousedown', function (e) {
            if (!ctxMenu) return;
            if (ctxJustOpened()) return;   // still the gesture that opened it
            if (!ctxMenu.contains(e.target)) ctxHide();
        }, true);
        // Touch dismissal, because the synthesised mouse events are unreliable on
        // mobile: starting any new touch outside the menu closes it.
        document.addEventListener('touchstart', function (e) {
            if (!ctxMenu || ctxJustOpened()) return;
            if (!ctxMenu.contains(e.target)) ctxHide();
        }, true);
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && ctxMenu) ctxHide();
        });
        window.addEventListener('blur', function () {
            if (ctxJustOpened()) return;
            ctxHide();
        });
        window.addEventListener('resize', ctxHide);
    }

    function setTheme(theme) {
        theme = (theme === 'light') ? 'light' : 'dark';
        currentTheme = theme;
        document.documentElement.className = 'theme-' + theme;
        console.log('[Theme] Switched to: ' + theme);

        if (typeof mermaid !== 'undefined' && typeof mermaid.initialize === 'function') {
            try {
                mermaid.initialize(getMermaidThemeVars(theme));
                mermaidInitialized = true;
                // Mermaid replaces <pre class="mermaid"> with SVG after rendering,
                // so calling mermaid.run on already-rendered nodes has no effect.
                // Here we re-render the entire Markdown
                // to rebuild .mermaid elements with the new theme
                reRenderMarkdown();
                console.log('[Theme] Mermaid theme updated');
            } catch (e) {
                console.warn('[Theme] Mermaid theme update failed: ' + describeError(e));
            }
        }
    }

    function reRenderMarkdown() {
        var preview = document.getElementById('preview');
        if (!preview) return;
        var source = preview.getAttribute('data-source');
        if (!source) return;
        // Go through the full render path: replacing innerHTML inline used to
        // leave links and video facades unwired after a theme change.
        // followToBottom=false: a theme/config re-render is not a content update,
        // so it must not yank the user's scroll position to the bottom.
        window.renderMarkdown(source, false);
    }

    /**
     * Replace or create a <style id="custom-theme-css"> in the document head.
     * The cssText must use the exact same selector naming as the built-in
     * renderer.css so that all CSS variables and highlight.js rules are
     * correctly overridden.
     */
    function setCustomCss(cssText) {
        cssText = (cssText == null) ? '' : String(cssText);
        try {
            var style = document.getElementById('custom-theme-css');
            if (!style) {
                style = document.createElement('style');
                style.id = 'custom-theme-css';
                document.head.appendChild(style);
            }
            style.textContent = cssText;
            console.log('[Theme] Custom CSS applied (' + cssText.length + ' chars)');
        } catch (e) {
            console.error('[Theme] Custom CSS failed: ' + describeError(e));
        }
    }

    // C# call entry
    window.setTheme = setTheme;
    window.setCustomCss = setCustomCss;

    // ===== Initialize Mermaid (follows current theme) =====
    if (typeof mermaid !== 'undefined' && typeof mermaid.initialize === 'function') {
        try {
            mermaid.initialize(getMermaidThemeVars(currentTheme));
            mermaidInitialized = true;
            console.log('[Mermaid] Theme initialized');
        } catch (e) {
            console.warn('[Mermaid] Initialization failed: ' + describeError(e));
        }
    }
    else if (typeof mermaid === 'undefined') {
        console.warn('[Mermaid] Library unavailable; diagrams will show their source');
    }

    // === Markdown-it configuration (VS Code style) ===
    // Authors spell the C# info string in several ways. highlight.js knows
    // `csharp`, `cs` and `c#`, but not `dotnet` / `c-sharp`; map the common
    // spellings so a C# fence is highlighted however it was written. The
    // original label is still what the code header displays.
    function normalizeCodeLanguage(lang) {
        if (!lang) return '';
        var l = String(lang).toLowerCase().trim();
        if (l === 'cs' || l === 'c#' || l === 'csharp' || l === 'c-sharp' || l === 'dotnet') {
            return 'csharp';
        }
        return l;
    }

    var md = window.markdownit({
        html: true,
        linkify: true,
        typographer: true,
        highlight: function(str, lang) {
            var codeHtml;
            var highlightLang = normalizeCodeLanguage(lang);
            if (highlightLang && typeof hljs !== 'undefined' && hljs.getLanguage(highlightLang)) {
                try {
                    codeHtml = hljs.highlight(str, {
                        language: highlightLang,
                        ignoreIllegals: true
                    }).value;
                } catch(e) {}
            }
            if (!codeHtml) codeHtml = md.utils.escapeHtml(str);
            // Build header: language label + copy button
            var parts = [];
            if (previewConfig.showCodeLanguage && lang) {
                parts.push('<span class="code-lang">' + escapeHtml(lang) + '</span>');
            }
            var actions = [];
            if (previewConfig.showCopyButton) {
                actions.push('<button class="copy-btn" onclick="copyCode(this)">Copy</button>');
            }
            // Height +/- buttons (per-code-block independent control)
            actions.push('<button class="resize-btn" onclick="decreaseCodeHeight(this)" title="Shrink">−</button>');
            actions.push('<button class="resize-btn" onclick="increaseCodeHeight(this)" title="Enlarge">+</button>');
            parts.push('<span class="code-actions">' + actions.join('') + '</span>');
            var header = parts.length ? '<div class="code-header">' + parts.join('') + '</div>' : '';
            return '<div class="code-block-wrapper" data-code-lang="' +
                escapeHtml(highlightLang) + '">' + header +
                '<pre class="hljs"><code>' + codeHtml + '</code></pre></div>';
        }
    });
    // Both plugins are optional CDN loads. Passing undefined to md.use throws,
    // which used to abort the whole script and leave the preview blank.
    if (typeof window.markdownitFootnote === 'function') {
        md.use(window.markdownitFootnote);
    } else {
        console.warn('[Init] markdown-it-footnote unavailable; footnotes will render as text');
    }
    if (typeof window.markdownitTaskLists === 'function') {
        md.use(window.markdownitTaskLists);
    } else {
        console.warn('[Init] markdown-it-task-lists unavailable; task lists will render as text');
    }
    try { md.enable(['strikethrough']); } catch(e) {}

    // === Heading anchors (GitHub / VS Code style) ===
    // Give every rendered heading an auto-generated id so in-document anchor links
    // — [TOC](#heading-slug) and friends — can jump to it natively. Duplicate
    // headings keep the first slug and get -1, -2, … appended (same as GitHub).
    var headingUsedIds = {};

    // Wrap md.render so the per-document id-uniqueness state resets on every render
    // (the DOM is fully replaced each time, so stale suffixes must not leak across).
    var baseRender = md.render;
    var renderDepth = 0;
    md.render = function(src, env) {
        // Only the outermost render resets slug state. Chat fences render their
        // body with md.render() while the outer document is still being built;
        // resetting there would let the inner headings steal/duplicate the outer
        // document's ids.
        if (renderDepth === 0) headingUsedIds = {};
        renderDepth++;
        try {
            return baseRender.call(md, src, env);
        } finally {
            renderDepth--;
        }
    };

    // Lowercase, drop punctuation, spaces/underscores become '-' (keeps CJK and
    // other unicode letters so Chinese headings are linkable too).
    function headingSlugify(text) {
        return String(text).toLowerCase().trim()
            .replace(/[^\p{L}\p{N}_\s-]/gu, '')
            .replace(/[\s_]+/g, '-')
            .replace(/-+/g, '-')
            .replace(/^-|-$/g, '');
    }

    // Plain text of a heading body (markup stripped, entities decoded) by letting
    // markdown-it render the inline token then reading the DOM textContent.
    function headingPlainText(inlineToken) {
        var html = md.renderInline(inlineToken ? inlineToken.content : '');
        var tmp = document.createElement('div');
        tmp.innerHTML = html;
        return (tmp.textContent || '').trim();
    }

    md.renderer.rules.heading_open = function(tokens, idx, options, env, self) {
        try {
            var token = tokens[idx];
            var inline = tokens[idx + 1];
            var text = headingPlainText(inline && inline.type === 'inline' ? inline : null);
            if (text) {
                var slug = headingSlugify(text) || 'section';
                var n = headingUsedIds[slug] | 0;
                var id = n > 0 ? slug + '-' + n : slug;
                headingUsedIds[slug] = n + 1;
                token.attrSet('id', id);
            }
        } catch (e) {
            // An anchor is a convenience; never let it abort the document.
            console.warn('[Heading] id generation failed: ' + describeError(e));
        }
        return self.renderToken(tokens, idx, options);
    };

    console.log('markdown-it ✅');

    // === Math: VS Code style $...$ / ... ===
    // Adapted from @vscode/markdown-it-katex parsing logic
    function isWS(c) { return /^\s$/u.test(c); }
    function isWord(c) { return /^[\w\d]$/u.test(c); }

    function isValidInlineDelim(state, pos) {
        var prev = state.src[pos - 1], cur = state.src[pos], next = state.src[pos + 1];
        if (cur !== '$') return { can_open: false, can_close: false };
        return {
            can_open: prev !== '$' && prev !== '\\' && (prev === undefined || isWS(prev) || !isWord(prev)),
            can_close: next !== '$' && (next === undefined || isWS(next) || !isWord(next))
        };
    }

    md.inline.ruler.after('escape', 'math_inline', function(state, silent) {
        if (state.src[state.pos] !== '$') return false;
        var res = isValidInlineDelim(state, state.pos);
        if (!res.can_open) {
            if (!silent) state.pending += '$';
            state.pos += 1;
            return true;
        }
        var start = state.pos + 1, match = start, pos;
        while ((match = state.src.indexOf('$', match)) !== -1) {
            pos = match - 1;
            while (state.src[pos] === '\\') pos -= 1;
            if (((match - pos) % 2) == 1) break;
            match += 1;
        }
        if (match === -1) {
            if (!silent) state.pending += '$';
            state.pos = start; return true;
        }
        if (match - start === 0) {
            if (!silent) state.pending += '';
            state.pos = start + 1; return true;
        }
        res = isValidInlineDelim(state, match);
        if (!res.can_close) {
            if (!silent) state.pending += '$';
            state.pos = start; return true;
        }
        if (!silent) {
            var t = state.push('math_inline', 'math', 0);
            t.markup = '$'; t.content = state.src.slice(start, match);
        }
        state.pos = match + 1;
        return true;
    });

    md.block.ruler.after('blockquote', 'math_block', function(state, start, end, silent) {
        var pos = state.bMarks[start] + state.tShift[start], max = state.eMarks[start];
        if (pos + 2 > max || state.src.slice(pos, pos + 2) !== '$$') return false;
        if (silent) return true;
        pos += 2;
        var firstLine = state.src.slice(pos, max), found = false, lastLine, next, lastPos;
        for (next = start; !found;) {
            next++;
            if (next >= end) break;
            pos = state.bMarks[next] + state.tShift[next];
            max = state.eMarks[next];
            if (pos < max && state.tShift[next] < state.blkIndent) break;
            var line = state.src.slice(pos, max).trim();
            if (line.slice(-2) === '$$') {
                lastPos = state.src.slice(0, max).lastIndexOf('$$');
                lastLine = state.src.slice(pos, lastPos);
                found = true;
            }
        }
        state.line = next + 1;
        var t = state.push('math_block', 'math', 0);
        t.block = true; t.markup = '';
        t.content = (firstLine && firstLine.trim() ? firstLine + '\n' : '') +
            state.getLines(start + 1, next, state.tShift[start], true) +
            (lastLine && lastLine.trim() ? lastLine : '');
        t.map = [start, state.line];
        return true;
    }, { alt: ['paragraph', 'reference', 'blockquote', 'list'] });

    // === Renderer ===
    function katexUnavailable() {
        return typeof katex === 'undefined' || typeof katex.renderToString !== 'function';
    }
    function renderKatexInline(latex) {
        if (katexUnavailable()) {
            // Same class as the renderer's own catch path, so the existing
            // DOM audit classifies this as an environment failure, not a typo.
            return '<span class="katex-error" title="KaTeX unavailable">' +
                escapeHtml('KaTeX is unavailable (the library failed to load)') + '</span>';
        }
        try {
            // Detect display math from \begin env
            var display = /\\begin\{(align|equation|gather|cd|alignat)\}/i.test(latex);
            return katex.renderToString(latex, { displayMode: display, throwOnError: false });
        } catch(e) {
            return '<span class="katex-error" title="' + escapeHtml(latex) + '">' +
                escapeHtml(describeError(e)) + '</span>';
        }
    }
    function renderKatexBlock(latex) {
        if (katexUnavailable()) {
            return '<p class="katex-block katex-error" title="KaTeX unavailable">' +
                escapeHtml('KaTeX is unavailable (the library failed to load)') + '</p>';
        }
        try {
            return '<p class="katex-block">' + katex.renderToString(latex, { displayMode: true, throwOnError: false }) + '</p>';
        } catch(e) {
            return '<p class="katex-block katex-error" title="' + escapeHtml(latex) + '">' +
                escapeHtml(describeError(e)) + '</p>';
        }
    }
    function escapeHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    md.renderer.rules.math_inline = function(tokens, idx) {
        return renderKatexInline(tokens[idx].content);
    };
    md.renderer.rules.math_block = function(tokens, idx) {
        return renderKatexBlock(tokens[idx].content) + '\n';
    };

    // === Chat / collapsible status blocks ===
    // ```chat[label][option,...]``` becomes a collapsed <details> panel. The label
    // is free-form; the badge icon follows the step state — lightbulb for thinking,
    // wrench for tool calls, check/x for done/error, chat bubble otherwise.
    var CHAT_STATUS_ICONS = {
        thinking: 'M9 18h6M10 22h4M12 2a7 7 0 0 0-4 12.7V17h8v-2.3A7 7 0 0 0 12 2Z',
        tool: 'M14.7 6.3a4 4 0 0 0-5.2 5.2L3 18l3 3 6.5-6.5a4 4 0 0 0 5.2-5.2l-2.1 2.1-2.8-.7-.7-2.8 2.6-2.6Z',
        done: 'M20 6 9 17l-5-5',
        error: 'M18 6 6 18M6 6l12 12',
        chat: 'M21 15a4 4 0 0 1-4 4H8l-5 3V7a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4v8Z'
    };

    function chatStatusIcon(state, customSvg) {
        if (customSvg) {
            if (/^\s*<svg[\s>]/i.test(customSvg)) return customSvg;
            return '<svg viewBox="0 0 24 24" focusable="false"><path d="' +
                escapeHtml(customSvg) + '"/></svg>';
        }
        var d = CHAT_STATUS_ICONS[state] || CHAT_STATUS_ICONS.chat;
        return '<svg viewBox="0 0 24 24" focusable="false"><path d="' + d + '"/></svg>';
    }

    // Split an option group on commas, but keep commas inside "..." / '...'.
    // The quotes are preserved so a value can be unwrapped exactly once.
    function splitChatOptions(group) {
        var out = [], cur = '', quote = null;
        for (var i = 0; i < group.length; i++) {
            var ch = group[i];
            if (quote) {
                cur += ch;
                if (ch === quote) quote = null;
            } else if (ch === '"' || ch === "'") {
                quote = ch;
                cur += ch;
            } else if (ch === ',') {
                out.push(cur);
                cur = '';
            } else {
                cur += ch;
            }
        }
        out.push(cur);
        return out;
    }

    function stripOuterQuotes(value) {
        if (value.length >= 2 &&
            (value[0] === '"' || value[0] === "'") &&
            value[value.length - 1] === value[0]) {
            return value.slice(1, -1);
        }
        return value;
    }

    function inferChatState(label) {
        var l = String(label || '').toLowerCase();
        if (/错误|失败|出错|error|fail/.test(l)) return 'error';
        if (/完成|成功|done|complete|success/.test(l)) return 'done';
        if (/思考|推理|think|reason/.test(l)) return 'thinking';
        if (/工具|调用|tool|call/.test(l)) return 'tool';
        return 'chat';
    }

    function parseChatInfo(rawInfo) {
        var rest = rawInfo.slice(4);          // after "chat", case preserved
        var groups = [];
        var consumed = 0;
        var bracket = /\[([^\]]*)\]/g;
        var match;
        while ((match = bracket.exec(rest)) !== null) {
            if (match.index !== consumed) {
                return { error: 'expected chat[label][options] with no text between groups; got ' + rawInfo };
            }
            groups.push(match[1]);
            consumed = match.index + match[0].length;
        }
        if (consumed !== rest.length) {
            return { error: 'malformed info string "' + rawInfo + '"; expected chat or chat[label]...' };
        }
        var label = groups.length ? groups[0].trim() : '';
        var open = false;
        var spin = true;      // the status halo spins unless explicitly frozen
        var spinExplicit = false;
        var state = null;
        var customSvg = null;
        for (var i = 1; i < groups.length; i++) {
            var options = splitChatOptions(groups[i]);
            for (var j = 0; j < options.length; j++) {
                var rawOption = options[j].trim();
                if (rawOption === '') continue;
                var eq = rawOption.indexOf('=');
                if (eq > 0) {
                    var key = rawOption.slice(0, eq).trim().toLowerCase();
                    if (key === 'svg') {
                        customSvg = stripOuterQuotes(rawOption.slice(eq + 1).trim());
                        if (!customSvg) return { error: 'svg= needs a path data or SVG string' };
                        continue;
                    }
                    return {
                        error: 'unknown chat option "' + key +
                            '"; expected open, collapsed, spin, static, thinking, tool, ' +
                            'done, error, chat or svg=...'
                    };
                }
                var option = rawOption.toLowerCase();
                if (option === 'open' || option === 'expanded') open = true;
                else if (option === 'collapsed') open = false;
                else if (option === 'spin' || option === 'live' || option === 'active') {
                    spin = true; spinExplicit = true;
                }
                else if (option === 'static' || option === 'idle') {
                    spin = false; spinExplicit = true;
                }
                else if (option === 'thinking' || option === 'think') state = 'thinking';
                else if (option === 'tool' || option === 'tools' ||
                         option === 'tool-call' || option === 'toolcall') state = 'tool';
                else if (option === 'done' || option === 'complete' || option === 'success') state = 'done';
                else if (option === 'error' || option === 'fail' || option === 'failed') state = 'error';
                else if (option === 'chat' || option === 'message') state = 'chat';
                else return {
                    error: 'unknown chat option "' + rawOption +
                        '"; expected open, collapsed, spin, static, thinking, tool, ' +
                        'done, error, chat or svg=...'
                };
            }
        }
        var resolvedLabel = label || '详情';
        if (!state) state = inferChatState(resolvedLabel);
        if (!spinExplicit && (state === 'done' || state === 'error')) spin = false;
        return { label: resolvedLabel, open: open, spin: spin, state: state, svg: customSvg };
    }

    function renderChatFence(content, rawInfo) {
        var parsed = parseChatInfo(rawInfo);
        if (parsed.error) {
            return '<div class="chat-error">⚠ chat fence error: ' + escapeHtml(parsed.error) +
                (content.trim() ? '<pre>' + escapeHtml(content) + '</pre>' : '') +
                '</div>\n';
        }
        var body = '';
        if (content.trim()) {
            try {
                // A nested render: the body is Markdown, not escaped text. markdown-it
                // keeps no global parse state, so re-entering the parser is safe.
                body = md.render(content);
            } catch (e) {
                return '<div class="chat-error">⚠ chat fence error: the body failed to render: ' +
                    escapeHtml(describeError(e)) + '<pre>' + escapeHtml(content) + '</pre></div>\n';
            }
        }
        return '<div class="chat-block-wrap">' +
            '<details class="chat-block"' + (parsed.open ? ' open' : '') + '>' +
            '<summary class="chat-summary">' +
            '<span class="chat-chevron" aria-hidden="true">' +
            '<svg viewBox="0 0 16 16"><path d="M3 1 13 8 3 15 Z"/></svg>' +
            '</span>' +
            '<span class="chat-label">' + escapeHtml(parsed.label) + '</span>' +
            '</summary>' +
            '<div class="chat-body">' + body + '</div>' +
            '</details>' +
            '<span class="chat-status chat-status-' + parsed.state +
                (parsed.spin ? ' chat-status-live' : '') +
                '" aria-hidden="true">' +
            '<span class="chat-status-ring"></span>' +
            '<span class="chat-status-logo">' + chatStatusIcon(parsed.state, parsed.svg) + '</span>' +
            '</span>' +
            '</div>\n';
    }

    // === Mermaid / PlantUML / plot / video / chat code blocks ===
    function fenceError(label, message, source) {
        return '<pre style="color:#f14c4c;border:1px solid #f14c4c;padding:8px;white-space:pre-wrap;">' +
            '⚠ ' + label + ': ' + escapeHtml(message) + '\n\n' +
            escapeHtml(source == null ? '' : source) + '</pre>\n';
    }

    // One bad fence must not abort the whole document. The original rule is
    // wrapped so a throw in any special branch degrades to a visible error block
    // and the rest of the Markdown still renders.
    var origFence = md.renderer.rules.fence;
    md.renderer.rules.fence = function(tokens, idx, options, env, self) {
        var token = tokens[idx];
        try {
            return renderSpecialFence.call(this, token, tokens, idx, options, env, self);
        } catch (e) {
            console.error('[Fence] render failed: ' + describeError(e));
            return fenceError('Fence render error', describeError(e), token && token.content);
        }
    };

    function renderSpecialFence(token, tokens, idx, options, env, self) {
        var rawInfo = token.info ? token.info.trim() : '';
        var info = rawInfo.toLowerCase();
        if (info === 'chat' || info.indexOf('chat[') === 0) {
            return renderChatFence(token.content, rawInfo);
        }
        if (info === 'mermaid') {
            return '<div class="mermaid-container">' +
                '<pre class="mermaid" style="text-align:center;">' +
                escapeHtml(token.content) +
                '</pre></div>\n';
        }
        if (info === 'plantuml' || info === 'puml') {
            if (!window.plantumlEncoder || typeof window.plantumlEncoder.encode !== 'function') {
                // Keep the "PlantUML render error:" prefix: RenderCheck.js keys on
                // it to classify this as an environment problem, not a document bug.
                return fenceError('PlantUML render error',
                    'plantuml-encoder is unavailable (failed to load from the CDN)',
                    token.content);
            }
            try {
                var encoded = window.plantumlEncoder.encode(token.content);
                if (!encoded || typeof encoded !== 'string') throw new Error('the encoder returned no data');
                var themeClass = currentTheme === 'light' ? 'puml-light' : 'puml-dark';
                return '<div class="puml-container ' + themeClass + '">' +
                    '<img src="https://www.plantuml.com/plantuml/svg/' + encoded + '" ' +
                    'alt="PlantUML Diagram" ' +
                    'onerror="this.closest(\'.puml-container\').classList.add(\'puml-load-failed\');' +
                    'this.alt=\'PlantUML image failed to load\';" />' +
                    '</div>\n';
            } catch(e) {
                return fenceError('PlantUML render error', describeError(e), token.content);
            }
        }
        if (info === 'plot') {
            // The fence body is the raw function-plot options object (JSON). It is carried
            // on the element and hydrated after insertion: function-plot needs a laid-out
            // target and returns a live Chart instance, neither of which a synchronous
            // renderer rule can provide. escapeHtml's set (& < > ") is exactly the one
            // needed for a double-quoted attribute, and round-trips losslessly.
            return '<div class="plot-container" data-plot="' + escapeHtml(token.content) + '"></div>\n';
        }
        if (info === 'video') {
            var videoSrc = token.content.trim();
            // Try platform embed first
            var embedHtml = getPlatformEmbed(videoSrc, 'Embedded Video');
            if (embedHtml) return embedHtml;
            // Fallback to direct video file
            return '<video controls playsinline preload="metadata" style="max-width:100%;height:auto;border-radius:4px;">' +
                '<source src="' + escapeHtml(videoSrc) + '">' +
                'Your browser does not support the video element.' +
                '</video>\n';
        }
        return origFence ? origFence.call(this, tokens, idx, options, env, self) : '';
    }

    // === Video / Platform embed support ===
    // Detects direct video files (.mp4, .webm, .ogg, etc.) and renders <video>.
    // Detects known platform URLs and renders <iframe> embeds.
    var origImageRender = md.renderer.rules.image;
    md.renderer.rules.image = function(tokens, idx, options, env, self) {
        var token = tokens[idx];
        try {
            // A malformed image token can have no src attribute. Indexing
            // attrs[-1] used to throw and abort the entire document render.
            var srcIndex = token.attrIndex ? token.attrIndex('src') : -1;
            var src = (srcIndex >= 0 && token.attrs && token.attrs[srcIndex])
                ? String(token.attrs[srcIndex][1] || '')
                : '';
            var alt = token.content || '';
            if (!src) return escapeHtml(alt);

            // Platform embed URL patterns
            var embedHtml = getPlatformEmbed(src, alt);
            if (embedHtml) return embedHtml;

            // Direct video file
            var isVideo = /\.(mp4|webm|ogg|mov|avi|mkv)(\?|#|$)/i.test(src);
            if (isVideo) {
                return '<video controls playsinline preload="metadata" style="max-width:100%;height:auto;border-radius:4px;">' +
                    '<source src="' + escapeHtml(src) + '">' +
                    'Your browser does not support the video element.' +
                    '</video>\n';
            }
            return origImageRender ? origImageRender.call(this, tokens, idx, options, env, self) : escapeHtml(alt);
        } catch (e) {
            console.warn('[Image] render failed: ' + describeError(e));
            return escapeHtml(token && token.content ? token.content : '');
        }
    };

    // Facade (click-to-play) embed: a static tile that does NOT load the third-party
    // player until the user clicks. This keeps the preview clean on load and across
    // re-renders — the platform player (and its console noise / fingerprinting / DRM
    // errors) only loads on explicit interaction.
    function embedFacade(src, label, thumb) {
        var thumbSafe = thumb
            ? String(thumb).replace(/'/g, "%27").replace(/\\/g, "%5C")
            : '';
        var style = thumbSafe ? ' style="background-image:url(\'' + thumbSafe + '\')"' : '';
        return '<div class="embed-facade" tabindex="0" role="button" aria-label="Play ' + escapeHtml(label) + '" data-src="' + escapeHtml(src) + '" data-title="' + escapeHtml(label) + '"' + style + '>' +
            '<span class="embed-play" aria-hidden="true"></span>' +
            '<span class="embed-label">' + escapeHtml(label) + '</span>' +
            '</div>';
    }

    // After insertion, fetch and apply a thumbnail for facades that need one
    // (Vimeo / Bilibili public APIs). Keeps the dark tile on failure.
    function fetchFacadeThumb(facade, apiUrl, jsonField) {
        try {
            if (!facade || !apiUrl || !jsonField) return;
            var controller = (typeof AbortController === 'function') ? new AbortController() : null;
            var timer = setTimeout(function () {
                if (controller) { try { controller.abort(); } catch (e2) { /* ignore */ } }
            }, 8000);
            var options = controller ? { signal: controller.signal } : undefined;

            fetch(apiUrl, options).then(function(r) { return r.json(); }).then(function(j) {
                var u = jsonField.split('.').reduce(function(o, k) {
                    return (o && o[k] !== undefined) ? o[k] : null;
                }, j);
                if (u && facade.isConnected) facade.style.backgroundImage = "url('" + u + "')";
            }).catch(function() {
                // Keep the dark tile: a missing thumbnail is cosmetic.
            }).then(function() {
                clearTimeout(timer);
            });
        } catch (e) { /* keep dark bg */ }
    }

    // Add a <link rel=preconnect> hint for the video origin so the iframe loads faster.
    function preconnectVideo(src) {
        var origin;
        try { origin = new URL(src, location.href).origin; } catch (e) { return; }
        try {
            var links = document.querySelectorAll('link[rel="preconnect"]');
            for (var i = 0; i < links.length; i++) if (links[i].href === origin + '/') return;
            var link = document.createElement('link');
            link.rel = 'preconnect';
            link.href = origin;
            document.head.appendChild(link);
        } catch (e2) { /* a hint is optional */ }
    }

    // Load the real platform iframe when a facade is clicked. The click is a user
    // gesture, so autoplay is allowed; preconnect speeds up the iframe load.
    window.loadVideoEmbed = function(facade) {
        if (!facade || !facade.parentNode) return;
        var src = facade.getAttribute('data-src');
        if (!src) return;
        try {
            preconnectVideo(src);
            src = src.replace('autoplay=0', 'autoplay=1');
            if (src.indexOf('autoplay=') === -1) src += (src.indexOf('?') === -1 ? '?' : '&') + 'autoplay=1';
            var iframe = document.createElement('iframe');
            iframe.setAttribute('src', src);
            iframe.setAttribute('frameborder', '0');
            iframe.setAttribute('allow', 'accelerometer;autoplay;clipboard-write;encrypted-media;fullscreen;gyroscope;picture-in-picture');
            iframe.setAttribute('title', facade.getAttribute('data-title') || 'Video');
            if (facade.parentNode) facade.parentNode.replaceChild(iframe, facade);
        } catch (e) {
            console.warn('[Video] could not load embed: ' + describeError(e));
        }
    };

    /** Try to convert a URL into a platform embed facade, or null if not supported. */
    function getPlatformEmbed(url, label) {
        if (typeof url !== 'string' || !url) return null;
        // YouTube: youtube.com/watch?v=ID, youtu.be/ID, youtube.com/embed/ID
        var ytMatch = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/|youtube\.com\/embed\/)([a-zA-Z0-9_-]{11})/);
        if (ytMatch) {
            var ytId = ytMatch[1];
            return '<div class="embed-container">' + embedFacade(
                'https://www.youtube-nocookie.com/embed/' + ytId,
                label || 'YouTube Video',
                'https://img.youtube.com/vi/' + ytId + '/hqdefault.jpg') + '</div>\n';
        }

        // Bilibili: bilibili.com/video/BVxxx  or  player.bilibili.com/player.html?bvid=BVxxx
        var bvMatch = url.match(/bilibili\.com\/(?:video\/(BV[a-zA-Z0-9_]+)|[^"' ]*bvid=(BV[a-zA-Z0-9_]+))/);
        var bvid = bvMatch && (bvMatch[1] || bvMatch[2]);
        if (bvid) {
            return '<div class="embed-container" data-thumb-api="https://api.bilibili.com/x/web-interface/view?bvid=' + bvid + '" data-thumb-field="data.pic">' +
                embedFacade('https://player.bilibili.com/player.html?bvid=' + bvid + '&autoplay=0&high_quality=1',
                    label || 'Bilibili Video', null) + '</div>\n';
        }

        // Vimeo: vimeo.com/ID
        var viMatch = url.match(/vimeo\.com\/(\d+)/);
        if (viMatch) {
            return '<div class="embed-container" data-thumb-api="https://vimeo.com/api/oembed.json?url=' + encodeURIComponent('https://vimeo.com/' + viMatch[1]) + '" data-thumb-field="thumbnail_url">' +
                embedFacade('https://player.vimeo.com/video/' + viMatch[1],
                    label || 'Vimeo Video', null) + '</div>\n';
        }

        return null;
    }

    // === Mermaid render (after page load) ===
    function renderMermaid() {
        var pv = document.getElementById('preview');
        if (pv) runMermaidAll(pv);
    }

    // ===== Auto-scroll (follow-to-bottom) behavior =====
    // Content updates scroll the preview to the bottom so the newest content stays
    // visible (agent-friendly). While the user is actively operating the vertical
    // scrollbar — wheel, touch, keyboard, or thumb drag — auto-scroll is suspended
    // until they stop for `autoScrollCooldown` ms.
    var scrollState = {
        lastUserActivity: 0,       // 0 = never scrolled → always follow
        programmaticTarget: null   // scrollTop we asked for via auto-scroll, or null
    };

    function markUserScroll() {
        scrollState.lastUserActivity = Date.now();
    }

    // A scroll-position change is "the user's" unless it lands on the target we set
    // programmatically. Position comparison (not a timer) is used because the scroll
    // event from scrollTop assignment is async and can be delayed past any fixed
    // timeout under heavy layout (e.g. wide streaming tables) — a delayed event must
    // still be recognized as ours, or auto-scroll would misclassify it as a user
    // scroll and suppress itself for the whole cooldown.
    window.addEventListener('scroll', function() {
        if (scrollState.programmaticTarget !== null) {
            var scroller = document.scrollingElement || document.documentElement || document.body;
            if (Math.abs(scroller.scrollTop - scrollState.programmaticTarget) <= 2) {
                scrollState.programmaticTarget = null;  // reached our own target → ours
                return;
            }
            scrollState.programmaticTarget = null;  // user moved away from our target
        }
        markUserScroll();
    }, { passive: true });

    // Wheel / touch / keyboard gestures are captured explicitly so a gesture that
    // does not change scrollTop (e.g. wheel while already at the bottom) still
    // suspends auto-scroll while the user is operating the scrollbar.
    window.addEventListener('wheel', markUserScroll, { passive: true });
    window.addEventListener('touchmove', markUserScroll, { passive: true });
    window.addEventListener('keydown', function(e) {
        switch (e.key) {
            case 'PageUp': case 'PageDown':
            case 'ArrowUp': case 'ArrowDown':
            case 'Home': case 'End': case ' ':
                markUserScroll();
                break;
        }
    });

    function shouldAutoScroll() {
        if (previewConfig.autoScrollOnUpdate === false) return false;
        return Date.now() - scrollState.lastUserActivity >= previewConfig.autoScrollCooldown;
    }

    function scrollToBottom() {
        var scroller = document.scrollingElement || document.documentElement || document.body;
        scrollState.programmaticTarget = Math.max(0, scroller.scrollHeight - scroller.clientHeight);
        scroller.scrollTop = scrollState.programmaticTarget;
    }

    function maybeAutoScroll() {
        if (!shouldAutoScroll()) return;
        scrollToBottom();
    }

    // Scroll the preview to a vertical progress position (0-100) of the document.
    // Exposed for C# via MarkdownView.ScrollToProgressAsync.
    window.setScrollProgress = function(percent) {
        var p = Math.max(0, Math.min(100, Number(percent) || 0));
        var scroller = document.scrollingElement || document.documentElement || document.body;
        var max = scroller.scrollHeight - scroller.clientHeight;
        scroller.scrollTop = max > 0 ? max * (p / 100) : 0;
        // Treat the jump like a manual scroll so auto-scroll doesn't immediately
        // yank the view back to the bottom on the next content update.
        scrollState.lastUserActivity = Date.now();
    };

    // Current vertical scroll progress (0-100) of the document, for scroll-sync.
    window.getScrollProgress = function() {
        var scroller = document.scrollingElement || document.documentElement || document.body;
        var max = scroller.scrollHeight - scroller.clientHeight;
        if (max <= 0) return 0;
        return (scroller.scrollTop / max) * 100;
    };

    function scrollToHeading(el) {
        el.scrollIntoView({ block: 'start' });
        // Treat the jump like a manual scroll so auto-scroll doesn't yank back.
        scrollState.lastUserActivity = Date.now();
    }

    // Scroll to the heading whose rendered text matches (case-insensitive, exact
    // first, then prefix). Returns 1 if found, 0 otherwise. Exposed for C# via
    // MarkdownView.NavigateToHeadingAsync.
    window.navigateToHeading = function(text) {
        var target = String(text || '').replace(/^#+\s*/, '').trim();
        if (!target) return 0;
        var targetL = target.toLowerCase();
        var headings = document.querySelectorAll('h1, h2, h3, h4, h5, h6');
        var fallback = null;
        for (var i = 0; i < headings.length; i++) {
            var h = headings[i];
            var hL = (h.textContent || '').trim().toLowerCase();
            if (hL === targetL) { scrollToHeading(h); return 1; }
            if (!fallback && hL.indexOf(targetL) === 0) fallback = h;
        }
        if (fallback) { scrollToHeading(fallback); return 1; }
        return 0;
    };

    // All headings [{ text, level }] in document order — for building a TOC.
    // Return the array directly (not JSON.stringify): WebView2 / evaluateJavascript
    // JSON-serialize the last expression, so a string return would be double-encoded.
    window.getHeadings = function() {
        var out = [];
        var hs = document.querySelectorAll('h1, h2, h3, h4, h5, h6');
        for (var i = 0; i < hs.length; i++) {
            var level = parseInt(hs[i].tagName.charAt(1), 10);
            out.push({ text: (hs[i].textContent || '').trim(), level: level });
        }
        return out;
    };

    // === C# display enrichment ===
    // highlight.js tags C# declarations (keywords, title, params, strings) but
    // leaves type names and method calls as plain text. Wrap only the high-signal
    // untagged identifiers, so C# reads like an editor without touching any token
    // highlight.js already classified.
    function enhanceCsharpBlocks(root) {
        var wrappers = root.querySelectorAll(
            '.code-block-wrapper[data-code-lang="csharp"]');
        for (var i = 0; i < wrappers.length; i++) {
            var code = wrappers[i].querySelector('code');
            if (code) enrichCsharpCode(code);
        }
    }

    function enrichCsharpCode(codeEl) {
        if (!codeEl || typeof document.createTreeWalker !== 'function') return;
        var SHOW_TEXT = 4, FILTER_ACCEPT = 1, FILTER_REJECT = 2;
        var walker = document.createTreeWalker(codeEl, SHOW_TEXT, {
            acceptNode: function (node) {
                // Never touch text inside an existing token span.
                var p = node.parentNode;
                while (p && p !== codeEl) {
                    if (p.nodeName === 'SPAN' && p.className &&
                        String(p.className).indexOf('hljs-') >= 0) {
                        return FILTER_REJECT;
                    }
                    p = p.parentNode;
                }
                return FILTER_ACCEPT;
            }
        }, false);

        var nodes = [], node;
        while ((node = walker.nextNode())) nodes.push(node);
        for (var i = 0; i < nodes.length; i++) enrichCsharpTextNode(nodes[i]);
    }

    function enrichCsharpTextNode(node) {
        var text = node.nodeValue || '';
        if (!/[A-Z]/.test(text)) return;

        var re = /[A-Za-z_][A-Za-z0-9_]*/g;
        var frag = null, last = 0, match;
        while ((match = re.exec(text)) !== null) {
            var word = match[0];
            if (!/^[A-Z]/.test(word)) continue;

            var before = text.slice(0, match.index);
            var after = text.slice(re.lastIndex);
            var ownerMatch = /([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*$/.exec(before);
            var owner = ownerMatch ? ownerMatch[1] : '';

            // Leave instance members (obj.Name) plain: highlight.js has no
            // reliable type/property distinction here, and a wrong colour is
            // worse than no colour.
            if (owner && !/^[A-Z]/.test(owner) && !/^\s*\(/.test(after)) continue;

            var span = document.createElement('span');
            if (/^\s*\(/.test(after)) {
                // Console.WriteLine(...) / new Greeter(...)
                span.className = 'hljs-title function_';
            } else {
                // Console, System, IGreeter, DateTime.Now, ...
                span.className = 'hljs-type';
            }
            span.textContent = word;

            if (!frag) frag = document.createDocumentFragment();
            frag.appendChild(document.createTextNode(text.slice(last, match.index)));
            frag.appendChild(span);
            last = re.lastIndex;
        }

        if (!frag) return;
        frag.appendChild(document.createTextNode(text.slice(last)));
        node.parentNode.replaceChild(frag, node);
    }

    // === Main render function ===
    window.renderMarkdown = function(text, followToBottom) {
        // InvokeScript is public API: coerce anything callable into a string
        // instead of calling .length on an object / null.
        text = (text == null) ? '' : String(text);

        console.log('renderMarkdown called, length=' + text.length);
        var preview = document.getElementById('preview');
        if (!preview) { console.error('preview element not found'); return; }

        if (!text) {
            // Empty state: show a configurable placeholder, or a blank pane by default.
            try {
                preview.innerHTML = previewConfig.placeholder
                    ? '<p class="empty-placeholder">' + escapeHtml(previewConfig.placeholder) + '</p>'
                    : '';
                preview.removeAttribute('data-source');
                preview.removeAttribute('data-render-error');
            } catch (e0) {
                console.error('[Render] empty-state failed: ' + describeError(e0));
            }
            return;
        }

        preview.setAttribute('data-source', text);
        try { ctxHide(); } catch (eCtx) { /* menu cleanup is best effort */ }

        try {
            preview.innerHTML = md.render(text);
            preview.removeAttribute('data-render-error');
            enhanceCsharpBlocks(preview);

            // Intercept external links:
            // Replace href with javascript:void(0) to prevent ANY native navigation
            // on ALL platforms (WebView2 / Android WebView / iOS WKWebView / WASM).
            // Then use window.open() — the universal web standard — to open in
            // system browser / new tab. No platform-specific C# bridge needed.
            var links = preview.querySelectorAll('a[href]');
            for (var i = 0; i < links.length; i++) {
                try {
                    var a = links[i];
                    var href = a.getAttribute('href');
                    if (!href || href.startsWith('#') || href.startsWith('javascript:')) continue;
                    a.setAttribute('data-href', href);
                    a.href = 'javascript:void(0)';
                    a.setAttribute('rel', 'noopener noreferrer');
                    a.addEventListener('click', (function(url) {
                        return function(e) {
                            try { e.preventDefault(); } catch (e0) { /* ignore */ }
                            // Send [LINK] to C# via the cross-platform bridge.
                            // The post() function handles WebView2, Android (AvaloniaWebViewBridge),
                            // and iframe (window.parent.postMessage) — same chain used for console logs.
                            post('LINK', url);
                            // Fall back to window.open only when NO C# bridge is reachable
                            // (e.g. the raw page opened directly in a browser). With any bridge,
                            // navigation is routed through C# — window.open on Android WebView
                            // would navigate the current page and overwrite the preview.
                            if (!hasBridge() && /^(https?|mailto|tel):/i.test(url)) {
                                try { window.open(url, '_blank'); } catch (e1) { /* popup blocked */ }
                            }
                        };
                    })(href));
                } catch (eLink) {
                    // One malformed link must not break the rest of the document.
                    console.warn('[Link] could not wire a link: ' + describeError(eLink));
                }
            }

            // Facade video embeds: wire the click-to-play delegation once, and fetch
            // thumbnails for facades that need them (Vimeo / Bilibili).
            if (!preview._facadeClickWired) {
                preview._facadeClickWired = true;
                var activateFacade = function(e) {
                    var facade = e.target && e.target.closest ? e.target.closest('.embed-facade') : null;
                    if (facade) loadVideoEmbed(facade);
                };
                preview.addEventListener('click', activateFacade);
                preview.addEventListener('keydown', function(e) {
                    if (e.key !== 'Enter' && e.key !== ' ') return;
                    var facade = e.target && e.target.closest ? e.target.closest('.embed-facade') : null;
                    if (facade) { e.preventDefault(); loadVideoEmbed(facade); }
                });
            }
            var thumbFacades = preview.querySelectorAll('.embed-container[data-thumb-api] .embed-facade');
            for (var ti = 0; ti < thumbFacades.length; ti++) {
                try {
                    (function(f) {
                        var c = f.parentNode;
                        if (!c) return;
                        var api = c.getAttribute('data-thumb-api');
                        var field = c.getAttribute('data-thumb-field');
                        if (api && field) fetchFacadeThumb(f, api, field);
                    })(thumbFacades[ti]);
                } catch (eThumb) {
                    console.warn('[Video] thumbnail wiring failed: ' + describeError(eThumb));
                }
            }

            runMermaidAll(preview);
            runFunctionPlotAll(preview, followToBottom);
            if (followToBottom !== false) {
                maybeAutoScroll();
                // Mermaid / KaTeX / image rendering is async — the page height can
                // settle after the initial scroll, so re-apply the follow-to-bottom
                // a few times to end up exactly at the newest content.
                setTimeout(maybeAutoScroll, 150);
                setTimeout(maybeAutoScroll, 500);
                setTimeout(maybeAutoScroll, 1200);
            }
            console.log('✅ Render complete');
        } catch(e) {
            // Whole-document parse failure: show the source rather than a blank
            // pane, and leave an explicit marker for the DOM audit.
            console.error('❌ ' + describeError(e));
            try {
                preview.setAttribute('data-render-error', describeError(e));
                preview.innerHTML = '';
                var note = document.createElement('div');
                note.className = 'render-unavailable';
                note.textContent = '⚠ The Markdown could not be rendered: ' + describeError(e) +
                    ' — showing the source below.';
                var pre = document.createElement('pre');
                pre.className = 'render-fallback';
                pre.textContent = text;
                preview.appendChild(note);
                preview.appendChild(pre);
            } catch (e1) {
                console.error('[Render] fallback failed: ' + describeError(e1));
            }
        }
    };

    window.onMarkdownUpdate = function(text) { window.renderMarkdown(text); };
    window.md = md;
    window.escapeHtml = escapeHtml;
    ctxWireOnce();
    console.log('✅ Initialization complete');

    // Poll until all CDN libraries are loaded (or timeout), then notify C# side.
    // Blocking <script> tags guarantee execution order, but a defensive poll
    // verifies every library's global is truly available before signalling ready.
    // A timeout ensures tracking-prevented CDN scripts don't stall the preview.
    var readyAttempts = 0;
    (function checkReady() {
        // Check critical libraries — ones essential for basic markdown rendering
        var criticalReady = typeof window.markdownit === 'function';
        // Check optional libraries (may be blocked by browser tracking prevention)
        var mermaidReady = typeof window.mermaid === 'object';
        var plantumlReady = typeof window.plantumlEncoder === 'object';
        var plotReady = typeof window.functionPlot === 'function';
        var hljsReady = typeof window.hljs === 'object';

        if (criticalReady) {
            // Always signal once markdown-it is available.
            // Optional libraries that failed to load (e.g. due to tracking prevention)
            // will log a warning at render time but won't block the preview.
            post('READY', '');
            if (!mermaidReady) console.warn('[Ready] mermaid blocked or unavailable');
            if (!plantumlReady) console.warn('[Ready] plantuml-encoder blocked or unavailable');
            if (!plotReady) console.warn('[Ready] function-plot blocked or unavailable');
            if (!hljsReady) console.warn('[Ready] highlight.js blocked or unavailable');
            console.log('✅ Markdown Preview ready');
        } else if (++readyAttempts > 300) {
            // ~30 s. The early guard handles a missing markdown-it; this only
            // prevents an endless timer if the script loaded in an unusual order.
            console.warn('[Ready] markdown-it did not become available; signalling anyway');
            post('ERR', '[Init] markdown-it did not become available after 30s');
            post('READY', '');
        } else {
            setTimeout(checkReady, 100);
        }
    })();
})();
