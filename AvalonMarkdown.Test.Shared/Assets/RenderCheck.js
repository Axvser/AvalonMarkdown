// Render-validation probe for the AvalonMarkdown preview (AVALON_RENDER_CHECK=1).
//
// Why this exists: most render failures in this renderer are SILENT.
//   * KaTeX is called with throwOnError:false, so it builds its own error span
//     and neither throws nor logs -- the repo's try/catch is never entered.
//   * A PlantUML server failure leaves a broken <img> with no event listener and
//     no console message anywhere.
//   * function-plot's invalid-JSON path logs nothing at all.
// A validator that only read console output would pass a thoroughly broken
// document. So this probe inspects the DOM.
//
// Contract (read by MainView.RenderCheck.cs):
//   window.__renderCheck.pending() -> number   work still in flight
//   window.__renderCheck.audit()   -> object   the full report
//   window.__renderCheck.env       -> object   library/network availability
//
// The probe never polls by itself: a busy-wait inside injected JS would hold the
// JS event loop, so requestAnimationFrame and mermaid's promises would never run
// and the probe would deadlock on its own subject. C# owns the waiting.
//
// ES5 only, matching renderer.js.

(function () {
    'use strict';

    var PREVIEW = 'preview';

    function pv() { return document.getElementById(PREVIEW); }

    function typeOf(name) {
        try { return typeof window[name]; } catch (e) { return 'undefined'; }
    }

    function text(el) { return (el && el.textContent) || ''; }

    function clip(s, n) {
        s = String(s == null ? '' : s);
        return s.length > n ? s.slice(0, n) + '…' : s;
    }

    function squish(s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').trim(); }

    // ── environment probe ───────────────────────────────────────────────────
    // The whole preview is CDN-loaded, so a blocked host looks exactly like a
    // broken document unless it is measured and reported separately.
    function env() {
        return {
            libs: {
                markdownit: typeOf('markdownit'),
                katex: typeOf('katex'),
                mermaid: typeOf('mermaid'),
                plantumlEncoder: typeOf('plantumlEncoder'),
                functionPlot: typeOf('functionPlot')
            },
            online: (typeof navigator !== 'undefined' && 'onLine' in navigator)
                ? !!navigator.onLine : null,
            theme: (document.documentElement.className.indexOf('theme-light') >= 0)
                ? 'light' : 'dark'
        };
    }

    // ── pending work ────────────────────────────────────────────────────────
    // Everything the renderer defers past "Render complete": plot hydration and
    // mermaid both run in a requestAnimationFrame, and PlantUML images keep
    // loading on the network.
    function pending() {
        var root = pv();
        if (!root) return 0;
        var n = root.querySelectorAll('.plot-container[data-plot]').length;
        n += root.querySelectorAll('pre.mermaid:not([data-processed])').length;
        var imgs = root.querySelectorAll('.puml-container img');
        for (var i = 0; i < imgs.length; i++) {
            if (!imgs[i].complete) n++;
        }
        return n;
    }

    // ── audit ───────────────────────────────────────────────────────────────
    function finding(id, severity, message, sample, count) {
        return {
            id: id,
            severity: severity,
            message: message,
            sample: sample == null ? '' : clip(squish(sample), 200),
            count: count == null ? 1 : count
        };
    }

    function each(sel, fn) {
        var root = pv();
        if (!root) return;
        var els = root.querySelectorAll(sel);
        for (var i = 0; i < els.length; i++) fn(els[i], i);
    }

    function count(sel) {
        var root = pv();
        return root ? root.querySelectorAll(sel).length : 0;
    }

    function audit() {
        var root = pv();
        var out = {
            findings: [],
            counts: {},
            vacuity: {},
            env: env()
        };
        if (!root) {
            out.findings.push(finding('preview-missing', 'fail',
                'no #preview element in the document'));
            return out;
        }

        // ── anti-vacuity gate ───────────────────────────────────────────────
        // Without this a blank preview passes every check below, which is worse
        // than no validator at all.
        var dataSource = root.getAttribute('data-source');
        var html = root.innerHTML || '';
        var abortPre = null;
        var kids = root.children;
        for (var k = 0; k < kids.length; k++) {
            var el = kids[k];
            // The abort artifact is exactly <pre>ESCAPED_SOURCE</pre>: no class,
            // no children. A real code fence is always wrapped and carries hljs.
            if (el.tagName === 'PRE' && !el.className && !el.children.length) {
                abortPre = el;
            }
        }

        out.vacuity = {
            dataSourceLength: dataSource ? dataSource.length : 0,
            innerHtmlLength: html.length,
            loadingPlaceholder: count('.loading-placeholder') > 0,
            abortPre: !!abortPre,
            renderErrorMarker: root.getAttribute('data-render-error') || '',
            errorOverlayVisible: (function () {
                var ov = document.getElementById('error-overlay');
                return !!ov && ov.style.display !== 'none' && ov.style.display !== '';
            })()
        };

        if (out.vacuity.loadingPlaceholder) {
            out.findings.push(finding('not-ready', 'environment',
                'the preview still shows its loading placeholder — nothing was rendered. ' +
                'The preview libraries most likely failed to load from the CDN.'));
            return out;
        }
        if (abortPre) {
            out.findings.push(finding('render-aborted', 'fail',
                'the whole render aborted: renderMarkdown threw and replaced the preview ' +
                'with the escaped source. See the console for the ❌ line.',
                text(abortPre), 1));
        }
        if (out.vacuity.renderErrorMarker) {
            out.findings.push(finding('render-fallback', 'fail',
                'renderMarkdown installed both its error marker and its plain-text ' +
                'fallback; the document did not render.',
                out.vacuity.renderErrorMarker, 1));
        }
        if (out.vacuity.dataSourceLength === 0 && html.length === 0) {
            out.findings.push(finding('nothing-rendered', 'fail',
                'the preview is empty: no data-source and no output'));
        }

        // ── fingerprint (proves the document actually rendered) ─────────────
        out.counts = {
            renderedChildren: kids.length,
            innerHtmlLength: html.length,
            headings: count('h1,h2,h3,h4,h5,h6'),
            tables: count('table'),
            codeBlocks: count('.code-block-wrapper'),
            lists: count('ul,ol'),
            blockquotes: count('blockquote'),
            links: count('a[data-href]'),
            footnotes: count('.footnotes'),
            facades: count('.embed-facade'),
            images: count('img'),
            katex: count('.katex'),
            mermaid: count('.mermaid-container'),
            puml: count('.puml-container'),
            chats: count('.chat-block'),
            plots: count('.plot-container')
        };

        // ── chat ────────────────────────────────────────────────────────────
        // A ```chat fence with a malformed info string renders `.chat-error`
        // instead of a panel; that is the one chat failure the static tier can
        // predict but only the DOM can prove.
        var chatErrors = 0, chatSample = '';
        each('.chat-error', function (el) {
            chatErrors++;
            if (!chatSample) chatSample = text(el);
        });
        if (chatErrors) {
            out.findings.push(finding('chat-error', 'fail',
                chatErrors + ' chat fence(s) failed to parse or render; the renderer ' +
                'shows the reason inline.', chatSample, chatErrors));
        }
        var chatMalformed = 0;
        each('.chat-block', function (el) {
            var wrap = el.parentElement;
            var status = wrap ? wrap.querySelector('.chat-status') : null;
            var statusClass = status ? String(status.className) : '';
            if (el.tagName !== 'DETAILS' ||
                !wrap ||
                !wrap.classList.contains('chat-block-wrap') ||
                !status ||
                !/chat-status-(thinking|tool|done|error|chat)/.test(statusClass) ||
                !status.querySelector('.chat-status-ring') ||
                !status.querySelector('.chat-status-logo') ||
                !el.querySelector('summary.chat-summary') ||
                !el.querySelector('.chat-chevron svg') ||
                !el.querySelector('.chat-body')) {
                chatMalformed++;
            }
        });
        if (chatMalformed) {
            out.findings.push(finding('chat-structure', 'fail',
                chatMalformed + ' chat block(s) are missing their summary, body, ' +
                'chevron or state badge.', '', chatMalformed));
        }

        // ── math ────────────────────────────────────────────────────────────
        // KaTeX's OWN error span carries an inline style and puts the message in
        // `title` with the raw LaTeX as text. The repo's catch block produces the
        // opposite and has no inline style -- that difference is what separates a
        // real LaTeX defect from "katex never loaded".
        var katexErrors = 0, katexEnv = 0, katexSample = '';
        each('span.katex-error, p.katex-error', function (el) {
            if (el.tagName === 'P') { katexEnv++; return; }           // repo catch, block
            if (el.hasAttribute('style')) {
                katexErrors++;
                if (!katexSample) katexSample = el.getAttribute('title') || text(el);
            } else {
                katexEnv++;                                            // repo catch, inline
            }
        });
        if (katexErrors) {
            out.findings.push(finding('katex-error', 'fail',
                'KaTeX could not parse ' + katexErrors + ' expression(s). KaTeX renders its ' +
                'own error span and logs nothing, so this is invisible without a DOM check.',
                katexSample, katexErrors));
        }
        if (katexEnv) {
            out.findings.push(finding('katex-unavailable', 'environment',
                katexEnv + ' math error node(s) came from the renderer\'s catch block rather ' +
                'than KaTeX itself — the KaTeX library is almost certainly not loaded.',
                '', katexEnv));
        }
        // The stylesheet is a separate remote <link>; if it 404s, math renders as
        // unstyled glyph soup with no error anywhere.
        if (out.counts.katex > 0) {
            var first = root.querySelector('.katex');
            var fam = first ? (window.getComputedStyle(first).fontFamily || '') : '';
            if (fam && fam.indexOf('KaTeX_') < 0) {
                out.findings.push(finding('katex-unstyled', 'warn',
                    'katex.min.css did not load, so math renders unstyled: ' + clip(fam, 80)));
            }
        }

        // ── mermaid ─────────────────────────────────────────────────────────
        // NOTE: neither `pre.mermaid` existing nor `data-processed` is a failure
        // signal. mermaid sets data-processed BEFORE rendering and assigns
        // innerHTML rather than replacing the element, so both are true on
        // success and failure alike. The error diagram it injects is the only
        // reliable marker: six .error-icon paths and two .error-text nodes.
        var mermaidFailed = 0, mermaidSample = '';
        each('.mermaid-container', function (c) {
            var pre = c.querySelector('pre.mermaid');
            if (!pre) return;
            if (pre.querySelector('.error-icon, .error-text')) {
                mermaidFailed++;
                if (!mermaidSample) {
                    var et = pre.querySelector('.error-text');
                    mermaidSample = et ? text(et) : text(pre);
                }
                return;
            }
            if (pre.hasAttribute('data-processed') && !pre.querySelector('svg')) {
                mermaidFailed++;
                if (!mermaidSample) mermaidSample = text(pre);
            }
        });
        if (mermaidFailed) {
            out.findings.push(finding('mermaid-render-error', 'fail',
                mermaidFailed + ' mermaid diagram(s) failed to render; mermaid injected its ' +
                'error diagram. The diagram source is destroyed by that path, so only the ' +
                'fence ordinal in the source can be pointed at.',
                mermaidSample, mermaidFailed));
        }
        var mermaidPending = count('.mermaid-container pre.mermaid:not([data-processed])');
        if (mermaidPending) {
            out.findings.push(finding('mermaid-not-processed', 'environment',
                mermaidPending + ' mermaid node(s) were never processed — mermaid did not run ' +
                'at all. This is the silent case: no warning, no DOM marker.',
                '', mermaidPending));
        }

        // ── plantuml ────────────────────────────────────────────────────────
        // MEASURED, and it refutes the obvious assumption: a rejected diagram does
        // NOT fail to load. plantuml.com answers a syntax error with HTTP 400 whose
        // body is itself a renderable SVG ("Syntax Error?"), so the browser shows it
        // and reports a perfectly normal naturalWidth. A broken diagram measured
        // 592x325 here; a valid one measured 251x159. Nothing in the DOM
        // distinguishes them, and <img> content is unreadable cross-origin.
        //
        // So this check covers NETWORK failure only. Whether the server actually
        // accepted a diagram is decided by the Python tier, which reads the
        // response body -- see check_plantuml() in render_check.py. Do not
        // "upgrade" this to a fail.
        var pumlNotLoaded = 0, pumlLoading = 0;
        var pumlState = [];
        each('.puml-container img', function (img) {
            pumlState.push((img.complete ? 'loaded' : 'pending') + ' ' +
                img.naturalWidth + 'x' + img.naturalHeight);
            if (!img.complete) { pumlLoading++; return; }
            if (img.naturalWidth === 0) pumlNotLoaded++;
        });
        out.counts.pumlImages = pumlState;
        if (pumlNotLoaded) {
            out.findings.push(finding('puml-image-not-loaded', 'environment',
                pumlNotLoaded + ' PlantUML image(s) never loaded — plantuml.com was ' +
                'unreachable or refused the request. Note that this does NOT mean the ' +
                'diagram was rejected: a syntax error comes back as a renderable error ' +
                'image and displays normally. Only the Python check can tell.',
                '', pumlNotLoaded));
        }
        if (pumlLoading) {
            out.findings.push(finding('puml-image-pending', 'environment',
                pumlLoading + ' PlantUML image(s) were still loading when the settle deadline ' +
                'expired', '', pumlLoading));
        }
        if (pumlState.length && !pumlNotLoaded && !pumlLoading) {
            out.unvalidatedPuml = true;
        }

        // ── error blocks with no class (text scan only) ─────────────────────
        // showPlotError and the PlantUML catch both emit a bare <pre style=…>
        // with no class, so these can only be found by reading the text.
        var seenUnknown = [];
        each('#preview pre', function (pre) {
            if (pre.closest('.code-block-wrapper')) return;    // a real code block
            var t = text(pre);
            if (t.indexOf('PlantUML render error:') >= 0) {
                out.findings.push(finding('puml-encode-error', 'environment',
                    'the PlantUML encoder failed before any request was made — ' +
                    'plantuml-encoder is not loaded', t));
                return;
            }
            if (t.indexOf('Plot render error:') >= 0) {
                var sev = 'fail';
                if (t.indexOf('is not loaded') >= 0) sev = 'environment';
                else if (t.indexOf('invalid JSON') >= 0) sev = 'fail';
                else if (t.indexOf('rejected expression') >= 0) sev = 'fail';
                var msg = 'function-plot could not draw a chart: ' + clip(squish(t), 160);
                var dash = t.indexOf('—');
                if (dash > 0) msg = 'function-plot: ' + clip(squish(t.slice(dash + 1)), 160);
                out.findings.push(finding(
                    sev === 'environment' ? 'plot-unavailable' : 'plot-error',
                    sev, msg, t));
                return;
            }
            // Tripwire. A closed list of known surfaces goes stale within a
            // release; this renderer keeps finding new ways to fail quietly.
            if (squish(t).indexOf('⚠') === 0) {
                seenUnknown.push(clip(squish(t), 120));
            }
        });
        for (var u = 0; u < seenUnknown.length; u++) {
            out.findings.push(finding('unknown-error-block', 'fail',
                'an error block in a shape this probe does not recognise — a new silent ' +
                'failure surface. Teach RenderCheck.js about it.',
                seenUnknown[u]));
        }

        // ── still awaiting hydration ────────────────────────────────────────
        var plotsPending = count('.plot-container[data-plot]');
        if (plotsPending) {
            out.findings.push(finding('plot-not-hydrated', 'environment',
                plotsPending + ' plot container(s) were never hydrated (the marker attribute ' +
                'is still present) — the rAF pass did not run', '', plotsPending));
        }

        // ── plot grid cells ─────────────────────────────────────────────────
        // x and y use independent scales; a content area whose aspect does not
        // match the domain aspect — or two axes whose tick intervals differ —
        // draws rectangular grid cells. Compare the pixel distance between
        // adjacent grid lines, skipping non-linear axes (a log axis has no
        // constant local tick spacing).
        var gridBad = 0, gridSample = '';
        each('.plot-container svg', function (svg) {
            function axisSpacing(axis) {
                var ticks = svg.querySelectorAll('g.' + axis + '.axis .tick');
                if (!ticks.length) return null;
                // grid:false gives every tick line zero length. There is no grid
                // to check in that case.
                var line = ticks[0].querySelector('line');
                if (!line) return null;
                var lineLen = parseFloat(line.getAttribute(axis === 'x' ? 'y2' : 'x2'));
                if (!isFinite(lineLen) || Math.abs(lineLen) < 1) return null;
                var pts = [];
                for (var i = 0; i < ticks.length; i++) {
                    var t = ticks[i];
                    var v = t.__data__;
                    if (typeof v !== 'number' || !isFinite(v)) return null;
                    var m = /translate\(\s*([-\d.]+)[, ]+([-\d.]+)\s*\)/
                        .exec(t.getAttribute('transform') || '');
                    if (!m) return null;
                    pts.push({
                        v: v,
                        p: axis === 'x' ? parseFloat(m[1]) : parseFloat(m[2])
                    });
                }
                if (pts.length < 3) return null;
                var spacings = [];
                for (var j = 1; j < pts.length; j++) {
                    var dp = Math.abs(pts[j].p - pts[j - 1].p);
                    if (dp > 0) spacings.push(dp);
                }
                if (spacings.length < 2) return null;
                var min = spacings[0], max = spacings[0];
                for (var k = 1; k < spacings.length; k++) {
                    if (spacings[k] < min) min = spacings[k];
                    if (spacings[k] > max) max = spacings[k];
                }
                // A log axis has wildly different local tick spacing; skip it.
                if (max - min > max * 0.01) return null;
                return min;
            }
            var sx = axisSpacing('x');
            var sy = axisSpacing('y');
            if (sx === null || sy === null || !sx || !sy) return;
            var ratio = sx / sy;
            if (ratio < 0.98 || ratio > 1.02) {
                gridBad++;
                if (!gridSample) {
                    gridSample = 'x=' + sx.toFixed(2) + 'px, y=' +
                        sy.toFixed(2) + 'px between grid lines';
                }
            }
        });
        if (gridBad) {
            out.findings.push(finding('plot-grid-not-square', 'fail',
                gridBad + ' plot(s) draw rectangular grid cells (' + gridSample + ')',
                '', gridBad));
        }

        // ── vacuity qualifier ───────────────────────────────────────────────
        // A pass with nothing to check must not read as a pass.
        var unvalidated = [];
        if (out.counts.mermaid === 0) unvalidated.push('mermaid: 0 fences');
        if (out.counts.puml === 0) unvalidated.push('plantuml: 0 fences');
        if (out.counts.plots === 0) unvalidated.push('plot: 0 fences');
        if (out.counts.katex === 0) unvalidated.push('math: 0 expressions');
        if (out.unvalidatedPuml) {
            unvalidated.push('plantuml: images loaded, but a rejected diagram is ' +
                'indistinguishable from a drawn one in the DOM — only the Python ' +
                'server check can decide');
        }
        delete out.unvalidatedPuml;
        out.unvalidated = unvalidated;

        var fails = 0;
        for (var f = 0; f < out.findings.length; f++) {
            if (out.findings[f].severity === 'fail') fails++;
        }
        out.failCount = fails;
        out.ok = fails === 0;
        return out;
    }

    // ── export audit ────────────────────────────────────────────────────────
    // Drives every exportable member through both formats. Kept separate from
    // audit() because rasterising is asynchronous, and -- more importantly --
    // because its worst failure is invisible: a canvas that drew nothing, or one
    // that exceeded the engine's area limit, returns a perfectly valid PNG data
    // URL and throws nothing. Hence the opt-in ink measurement.
    var exportState = { running: false, done: true, results: [] };

    function exportTargets() {
        var out = [];
        var root = pv();
        if (!root) return out;
        var groups = [
            ['.mermaid-container', 'mermaid'],
            ['.plot-container', 'plot'],
            ['.puml-container', 'puml']
        ];
        for (var g = 0; g < groups.length; g++) {
            var els = root.querySelectorAll(groups[g][0]);
            for (var i = 0; i < els.length; i++) out.push({ el: els[i], kind: groups[g][1] });
        }
        return out;
    }

    // Drives the context-menu wiring with a synthetic event. This is the only way
    // to check -- without a human right-clicking -- that the handler actually
    // claims the event (dispatchEvent returns false when a listener prevented the
    // default, which is what suppresses the platform menu) and that the menu opens.
    function menuProbe() {
        var targets = exportTargets();
        if (!targets.length) return { ok: false, reason: 'no exportable members' };
        var el = targets[0].el;
        var inner = el.querySelector('svg, img') || el;
        var out = { ok: true, kind: targets[0].kind };
        try {
            var ev = new MouseEvent('contextmenu', {
                bubbles: true, cancelable: true, clientX: 60, clientY: 60
            });
            out.defaultPrevented = !inner.dispatchEvent(ev);
            var menu = document.querySelector('.md-ctx-menu');
            out.menuShown = !!menu;
            if (menu) {
                var items = menu.querySelectorAll('.md-ctx-item');
                out.items = [];
                for (var i = 0; i < items.length; i++) out.items.push(items[i].textContent);
            }
        } catch (e) {
            out.ok = false;
            out.reason = 'threw: ' + e.message;
        }
        if (window.hideExportMenu) window.hideExportMenu();
        return out;
    }

    function exportsStart() {
        var menu = menuProbe();
        var targets = exportTargets();
        exportState = { running: true, done: false, results: [], menu: menu };
        window.__exportInkProbe = true;   // measure coverage; see exportRasterise

        var queue = [];
        for (var i = 0; i < targets.length; i++) {
            queue.push({ el: targets[i].el, kind: targets[i].kind, format: 'svg' });
            queue.push({ el: targets[i].el, kind: targets[i].kind, format: 'png' });
        }

        function push(rec) { exportState.results.push(rec); }

        function step() {
            if (!queue.length) {
                exportState.running = false;
                exportState.done = true;
                window.__exportInkProbe = false;   // per-pixel readback is test-only
                return;
            }
            var job = queue.shift();
            var rec = { kind: job.kind, format: job.format };

            if (!window.exportMemberAsync) {
                rec.ok = false;
                rec.error = 'the export API is not present';
                push(rec);
                step();
                return;
            }
            // A member that never rendered (a broken plot, a mermaid error diagram,
            // a PlantUML image that did not load) has nothing to export. Reported as
            // skipped, not failed: audit() already carries the real defect.
            if (!window.exportAvailable || !window.exportAvailable(job.el, job.kind)) {
                rec.ok = false;
                rec.skipped = true;
                rec.error = 'nothing to export — this ' + job.kind + ' did not render';
                push(rec);
                step();
                return;
            }
            // A detached container is from an earlier render; skip it rather than
            // reporting a failure the document did not cause.
            if (!job.el.isConnected) {
                rec.ok = false;
                rec.skipped = true;
                rec.error = 'container is no longer in the document';
                push(rec);
                step();
                return;
            }

            function done(res) {
                for (var k in res) {
                    if (Object.prototype.hasOwnProperty.call(res, k)) rec[k] = res[k];
                }
                push(rec);
                step();
            }

            try {
                window.exportMemberAsync(job.el, job.kind, job.format, done);
            } catch (e) {
                rec.ok = false;
                rec.error = 'threw: ' + e.message;
                push(rec);
                step();
            }
        }

        // Watchdog: a callback that never fires must not hang the harness.
        setTimeout(function () {
            if (!exportState.done) {
                exportState.running = false;
                exportState.done = true;
                exportState.timedOut = true;
            }
        }, 30000);

        step();
        return exportState.results.length;
    }

    window.__renderCheck = {
        pending: pending,
        audit: audit,
        env: env,
        exportsStart: exportsStart,
        exportsReady: function () { return exportState.done ? 1 : 0; },
        exports: function () { return exportState; },
        version: '1'
    };
})();
