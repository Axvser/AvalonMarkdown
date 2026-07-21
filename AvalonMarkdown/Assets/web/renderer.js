(function() {
    // === Cross-platform C# bridge ===
    // Uses the standard chrome.webview.postMessage API (supported by WebView2,
    // Avalonia WebView Browser backend via av-webview.mjs polyfill, and
    // Android native WebView via Avalonia's built-in JavaScriptInterface).
    var origLog = console.log, origWarn = console.warn, origError = console.error;
    var post = function(level, msg) {
        try { window.chrome.webview.postMessage('[' + level + '] ' + msg); } catch(e) {
            // Fallback: for environments without chrome.webview (e.g. old Android),
            // try window.parent.postMessage and Avalonia bridge.
            try { window.parent.postMessage('[' + level + '] ' + msg, '*'); } catch(e2) {
                try { window.AvaloniaWebViewBridge.postMessage('[' + level + '] ' + msg); } catch(e3) {}
            }
        }
    };
    console.log = function() {
        var m = Array.prototype.map.call(arguments, function(a) { return typeof a === 'string' ? a : JSON.stringify(a); }).join(' ');
        origLog.apply(console, arguments); post('LOG', m);
    };
    console.warn = function() {
        var m = Array.prototype.map.call(arguments, function(a) { return typeof a === 'string' ? a : JSON.stringify(a); }).join(' ');
        origWarn.apply(console, arguments); post('WARN', m);
    };
    console.error = function() {
        var m = Array.prototype.map.call(arguments, function(a) { return typeof a === 'string' ? a : JSON.stringify(a); }).join(' ');
        origError.apply(console, arguments); post('ERR', m);
    };

    // === Global error capture — shown within the control instead of silent swallow ===
    window.onerror = function(msg, source, line, col, error) {
        // Silently log all errors, no UI overlay.
        // Subscribe to MarkdownView.ErrorOccurred for custom error handling.
        if (error && !(error instanceof Error)) {
            post('WARN', '[ResourceLoadError] ' + msg);
        } else {
            var detail = msg;
            if (source) detail += '\n   at ' + source + ':' + line + ':' + col;
            if (error && error.stack) detail += '\n' + error.stack;
            post('ERR', detail);
        }
        return true;
    };
    window.addEventListener('unhandledrejection', function(e) {
        var detail = e.reason ? (e.reason.message || e.reason.toString()) : 'Unhandled Promise rejection';
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

    console.log('Markdown Preview initializing');
    console.log('UA: ' + navigator.userAgent);

    // ===== Preview configuration =====
    var previewConfig = {
        fontSize: 14,
        lineHeight: 1.6,
        showCodeLanguage: true,
        showCopyButton: true,
        maxCodeBlockHeight: 480
    };

    window.setPreviewConfig = function(config) {
        if (config.fontSize !== undefined) previewConfig.fontSize = config.fontSize;
        if (config.lineHeight !== undefined) previewConfig.lineHeight = config.lineHeight;
        if (config.showCodeLanguage !== undefined) previewConfig.showCodeLanguage = config.showCodeLanguage;
        if (config.showCopyButton !== undefined) previewConfig.showCopyButton = config.showCopyButton;
        if (config.maxCodeBlockHeight !== undefined) previewConfig.maxCodeBlockHeight = config.maxCodeBlockHeight;
        document.body.style.fontSize = previewConfig.fontSize + 'px';
        document.body.style.lineHeight = previewConfig.lineHeight;
        // Apply code block max height
        applyCodeBlockMaxHeight();
        // Re-render to apply language label / copy button visibility
        var preview = document.getElementById('preview');
        var text = preview.getAttribute('data-source') || '';
        if (text) window.renderMarkdown(text);
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
    function runMermaidAll(container) {
        var els = container.querySelectorAll('.mermaid');
        if (els.length === 0) return;
        var nodes = [];
        for (var i = 0; i < els.length; i++) nodes.push(els[i]);
        try {
            var result = mermaid.run({ nodes: nodes });
            if (result && typeof result.then === 'function') {
                result.catch(function(e) {
                    console.warn('[Mermaid] Render error: ' + e.message);
                });
            }
        } catch(e) {
            console.warn('[Mermaid] Render error: ' + e.message);
        }
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

    function setTheme(theme) {
        if (theme !== 'dark' && theme !== 'light') theme = 'dark';
        currentTheme = theme;
        document.documentElement.className = 'theme-' + theme;
        console.log('[Theme] Switched to: ' + theme);

        if (typeof mermaid !== 'undefined') {
            mermaid.initialize(getMermaidThemeVars(theme));
            mermaidInitialized = true;
            // Mermaid replaces <pre class="mermaid"> with SVG after rendering,
            // so calling mermaid.run on already-rendered nodes has no effect.
            // Here we re-render the entire Markdown
            // to rebuild .mermaid elements with the new theme
            reRenderMarkdown();
            console.log('[Theme] Mermaid theme updated');
        }
    }

    function reRenderMarkdown() {
        var preview = document.getElementById('preview');
        if (!preview) return;
        var source = preview.getAttribute('data-source');
        if (!source) return;
        try {
            preview.innerHTML = md.render(source);
            runMermaidAll(preview);
        } catch(e) {
            console.error('❌ ' + e.message);
        }
    }

    /**
     * Replace or create a <style id="custom-theme-css"> in the document head.
     * The cssText must use the exact same selector naming as the built-in
     * renderer.css so that all CSS variables and highlight.js rules are
     * correctly overridden.
     */
    function setCustomCss(cssText) {
        var style = document.getElementById('custom-theme-css');
        if (!style) {
            style = document.createElement('style');
            style.id = 'custom-theme-css';
            document.head.appendChild(style);
        }
        style.textContent = cssText;
        console.log('[Theme] Custom CSS applied (' + (cssText ? cssText.length : 0) + ' chars)');
    }

    // C# call entry
    window.setTheme = setTheme;
    window.setCustomCss = setCustomCss;

    // ===== Initialize Mermaid (follows current theme) =====
    if (typeof mermaid !== 'undefined') {
        mermaid.initialize(getMermaidThemeVars(currentTheme));
        mermaidInitialized = true;
        console.log('[Mermaid] Theme initialized');
    }

    // === Markdown-it configuration (VS Code style) ===
    var md = window.markdownit({
        html: true,
        linkify: true,
        typographer: true,
        highlight: function(str, lang) {
            var codeHtml;
            if (lang && typeof hljs !== 'undefined' && hljs.getLanguage(lang)) {
                try {
                    codeHtml = hljs.highlight(str, { language: lang, ignoreIllegals: true }).value;
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
            return '<div class="code-block-wrapper">' + header +
                '<pre class="hljs"><code>' + codeHtml + '</code></pre></div>';
        }
    });
    md.use(window.markdownitFootnote);
    md.use(window.markdownitTaskLists);
    try { md.enable(['strikethrough']); } catch(e) {}

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
    function renderKatexInline(latex) {
        try {
            // Detect display math from \begin env
            var display = /\\begin\{(align|equation|gather|cd|alignat)\}/i.test(latex);
            return katex.renderToString(latex, { displayMode: display, throwOnError: false });
        } catch(e) {
            return '<span class="katex-error" title="' + escapeHtml(latex) + '">' + escapeHtml(e.message) + '</span>';
        }
    }
    function renderKatexBlock(latex) {
        try {
            return '<p class="katex-block">' + katex.renderToString(latex, { displayMode: true, throwOnError: false }) + '</p>';
        } catch(e) {
            return '<p class="katex-block katex-error" title="' + escapeHtml(latex) + '">' + escapeHtml(e.message) + '</p>';
        }
    }
    function escapeHtml(s) {
        return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }

    md.renderer.rules.math_inline = function(tokens, idx) {
        return renderKatexInline(tokens[idx].content);
    };
    md.renderer.rules.math_block = function(tokens, idx) {
        return renderKatexBlock(tokens[idx].content) + '\n';
    };

    // === Mermaid / PlantUML code blocks ===
    var origFence = md.renderer.rules.fence;
    md.renderer.rules.fence = function(tokens, idx, options, env, self) {
        var token = tokens[idx];
        var info = token.info ? token.info.trim().toLowerCase() : '';
        if (info === 'mermaid') {
            return '<div class="mermaid-container">' +
                '<pre class="mermaid" style="text-align:center;">' +
                escapeHtml(token.content) +
                '</pre></div>\n';
        }
        if (info === 'plantuml' || info === 'puml') {
            try {
                var encoded = window.plantumlEncoder.encode(token.content);
                var themeClass = currentTheme === 'light' ? 'puml-light' : 'puml-dark';
                return '<div class="puml-container ' + themeClass + '">' +
                    '<img src="https://www.plantuml.com/plantuml/svg/' + encoded + '" ' +
                    'alt="PlantUML Diagram" />' +
                    '</div>\n';
            } catch(e) {
                return '<pre style="color:#f14c4c;border:1px solid #f14c4c;padding:8px;">' +
                    '⚠ PlantUML render error: ' + escapeHtml(e.message) + '\n\n' +
                    escapeHtml(token.content) + '</pre>\n';
            }
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
    };

    // === Video / Platform embed support ===
    // Detects direct video files (.mp4, .webm, .ogg, etc.) and renders <video>.
    // Detects known platform URLs and renders <iframe> embeds.
    var origImageRender = md.renderer.rules.image;
    md.renderer.rules.image = function(tokens, idx, options, env, self) {
        var token = tokens[idx];
        var src = token.attrs[token.attrIndex('src')][1];
        var alt = token.content;

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
        return origImageRender ? origImageRender(tokens, idx, options, env, self) : '';
    };

    /** Try to convert a URL into a platform embed iframe HTML, or null if not supported. */
    function getPlatformEmbed(url, label) {
        // YouTube: youtube.com/watch?v=ID, youtu.be/ID, youtube.com/embed/ID
        var ytMatch = url.match(/(?:youtube\.com\/watch\?v=|youtu\.be\/|youtube\.com\/embed\/)([a-zA-Z0-9_-]{11})/);
        if (ytMatch) {
            return '<div class="embed-container">' +
                '<iframe src="https://www.youtube.com/embed/' + ytMatch[1] + '" ' +
                'frameborder="0" allowfullscreen allow="accelerometer;autoplay;clipboard-write;encrypted-media;gyroscope;picture-in-picture" ' +
                'title="' + escapeHtml(label || 'YouTube Video') + '"></iframe></div>\n';
        }

        // Bilibili: bilibili.com/video/BVxxx  or  player.bilibili.com/player.html?bvid=BVxxx
        var bvMatch = url.match(/bilibili\.com\/(?:video\/(BV[a-zA-Z0-9_]+)|[^"' ]*bvid=(BV[a-zA-Z0-9_]+))/);
        var bvid = bvMatch && (bvMatch[1] || bvMatch[2]);
        if (bvid) {
            return '<div class="embed-container">' +
                '<iframe src="https://player.bilibili.com/player.html?bvid=' + bvid + '&autoplay=0&high_quality=1" ' +
                'frameborder="0" allowfullscreen sandbox="allow-scripts allow-same-origin allow-popups" ' +
                'title="' + escapeHtml(label || 'Bilibili Video') + '"></iframe></div>\n';
        }

        // Vimeo: vimeo.com/ID
        var viMatch = url.match(/vimeo\.com\/(\d+)/);
        if (viMatch) {
            return '<div class="embed-container">' +
                '<iframe src="https://player.vimeo.com/video/' + viMatch[1] + '" ' +
                'frameborder="0" allowfullscreen allow="autoplay;fullscreen;picture-in-picture" ' +
                'title="' + escapeHtml(label || 'Vimeo Video') + '"></iframe></div>\n';
        }

        return null;
    }

    // === Mermaid render (after page load) ===
    function renderMermaid() {
        if (typeof mermaid !== 'undefined') {
            var pv = document.getElementById('preview');
            if (pv) runMermaidAll(pv);
        }
    }

    // === Main render function ===
    window.renderMarkdown = function(text) {
        console.log('renderMarkdown called, length=' + (text ? text.length : 0));
        var preview = document.getElementById('preview');
        if (!preview) { console.error('preview element not found'); return; }
        if (!text) {
            preview.innerHTML = '<p style="color:#888;text-align:center;margin-top:80px;">← Enter Markdown</p>';
            preview.removeAttribute('data-source');
            return;
        }
        preview.setAttribute('data-source', text);
        try {
            preview.innerHTML = md.render(text);
            // Intercept all links: prevent WebView navigation and notify C# via postMessage
            // so it can open in system browser (C# side handles the actual URL opening).
            var links = preview.querySelectorAll('a[href]');
            for (var i = 0; i < links.length; i++) {
                var a = links[i];
                var href = a.getAttribute('href');
                if (!href || href.startsWith('#') || href.startsWith('javascript:')) continue;
                a.setAttribute('rel', 'noopener noreferrer');
                a.addEventListener('click', (function(url) {
                    return function(e) {
                        e.preventDefault();
                        post('LINK', url);
                    };
                })(href));
            }
            runMermaidAll(preview);
            console.log('✅ Render complete');
        } catch(e) {
            console.error('❌ ' + e.message);
            preview.innerHTML = '<pre>' + escapeHtml(text) + '</pre>';
        }
    };

    window.onMarkdownUpdate = function(text) { window.renderMarkdown(text); };
    window.md = md;
    window.escapeHtml = escapeHtml;
    console.log('✅ Initialization complete');

    // Notify C# side that all CDN scripts and renderer.js are ready
    post('READY', '');
})();
