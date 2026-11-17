// Fault-injection probe for the AvalonMarkdown preview (AVALON_FAULT_CHECK=1).
//
// The render-check probe answers "did this document render correctly?". This
// probe answers a different question: "does the preview survive being used
// wrongly?" It drives public JS entry points with hostile input, removes optional
// CDN globals one at a time, and asserts that the document still renders and the
// failure is visible rather than silent.
//
// Contract (read by MainView.FaultCheck.cs):
//   window.__faultCheck.run()      -> number of queued tests (starts the run)
//   window.__faultCheck.ready()    -> 1 when done
//   window.__faultCheck.results()  -> { results: [...], done: true }
//
// ES5 syntax, matching renderer.js.

(function () {
    'use strict';

    var state = { running: false, done: true, results: [] };
    var queue = [];

    function pv() { return document.getElementById('preview'); }
    function text(el) { return (el && el.textContent) || ''; }
    function bodyText() { return text(pv()); }
    function count(sel) {
        var root = pv();
        return root ? root.querySelectorAll(sel).length : 0;
    }
    function has(sel) { return count(sel) > 0; }
    function assert(cond, msg) { if (!cond) throw new Error(msg); }
    function describe(e) {
        if (e == null) return 'unknown error';
        if (typeof e === 'string') return e;
        return e.message ? String(e.message) : String(e);
    }
    function repeat(s, n) {
        var out = '';
        for (var i = 0; i < n; i++) out += s;
        return out;
    }
    function skip(msg) {
        var e = new Error(msg);
        e.__skipped = true;
        throw e;
    }

    function render(md) { window.renderMarkdown(md); }

    function frame() {
        return new Promise(function (resolve) {
            requestAnimationFrame(function () { resolve(); });
        });
    }
    function settle() {
        return frame().then(frame).then(function () {
            return new Promise(function (resolve) { setTimeout(resolve, 120); });
        });
    }
    function withTimeout(promise, ms, message) {
        return Promise.race([
            promise,
            new Promise(function (_, reject) {
                setTimeout(function () { reject(new Error(message || 'timed out')); }, ms);
            })
        ]);
    }

    function resetConfig() {
        window.setPreviewConfig({
            fontSize: 14,
            lineHeight: 1.6,
            showCodeLanguage: true,
            showCopyButton: true,
            maxCodeBlockHeight: 480,
            autoScrollOnUpdate: true,
            autoScrollCooldown: 2000,
            placeholder: '',
            contextMenuEnabled: true
        });
    }

    function test(name, fn) { queue.push({ name: name, fn: fn }); }

    function finish(rec, error) {
        if (error && error.__skipped) {
            rec.skipped = true;
            rec.error = describe(error);
        } else if (error) {
            rec.ok = false;
            rec.error = describe(error);
        } else {
            rec.ok = true;
        }
        state.results.push(rec);
        setTimeout(runNext, 0);
    }

    function runNext() {
        if (!queue.length) {
            state.running = false;
            state.done = true;
            return;
        }
        var job = queue.shift();
        var rec = { name: job.name };
        var settled = false;
        function done(error) {
            if (settled) return;
            settled = true;
            finish(rec, error);
        }
        try {
            var result = job.fn(rec);
            if (result && typeof result.then === 'function') {
                result.then(function () { done(null); }, function (e) { done(e); });
            } else {
                done(null);
            }
        } catch (e) {
            done(e);
        }
    }

    // ── configuration ───────────────────────────────────────────────────────
    test('config-invalid-inputs', function () {
        window.setPreviewConfig(null);
        window.setPreviewConfig(undefined);
        window.setPreviewConfig('not an object');
        window.setPreviewConfig({
            fontSize: 'abc',
            lineHeight: NaN,
            maxCodeBlockHeight: -100,
            autoScrollCooldown: -1,
            placeholder: 12345,
            contextMenuEnabled: 'false'
        });

        var size = parseFloat(window.getComputedStyle(document.body).fontSize);
        assert(isFinite(size) && size >= 1 && size <= 96,
            'font-size must stay sane, got ' + size);

        render('# config still works');
        assert(bodyText().indexOf('config still works') >= 0,
            'the preview must render after a junk config');
        resetConfig();
    });

    test('config-clamps-extremes', function () {
        window.setPreviewConfig({
            fontSize: 9999,
            lineHeight: 0,
            maxCodeBlockHeight: 999999,
            autoScrollCooldown: 9999999
        });

        var size = parseFloat(window.getComputedStyle(document.body).fontSize);
        // The computed line-height is resolved to px (0.8 × 96px = 76.8px), so
        // assert on the declared value instead.
        var lineHeight = parseFloat(document.body.style.lineHeight);
        assert(size >= 1 && size <= 96, 'font-size must be clamped, got ' + size);
        assert(lineHeight >= 0.8 && lineHeight <= 4,
            'line-height must be clamped, got ' + lineHeight);

        var style = document.getElementById('cb-max-height-style');
        assert(style && style.textContent.indexOf('999999') < 0,
            'max code height must be clamped');
        resetConfig();
    });

    // ── render inputs ───────────────────────────────────────────────────────
    test('render-hostile-inputs', function () {
        var inputs = [null, undefined, 0, false, 123, {}, []];
        for (var i = 0; i < inputs.length; i++) {
            render(inputs[i]);   // must not throw
        }
        render('# hostile inputs survived');
        assert(bodyText().indexOf('hostile inputs survived') >= 0,
            'the preview must still render');
    });

    test('render-empty-clears-state', function () {
        render('# something');
        render('');
        var root = pv();
        assert(!root.hasAttribute('data-source'), 'empty render must clear data-source');
        assert(root.innerHTML === '' || has('.empty-placeholder'),
            'empty render must be blank or show the placeholder');
    });

    test('render-large-document', function (rec) {
        var body = repeat('Lorem ipsum dolor sit amet, consectetur adipiscing elit. ', 7000);
        var doc = '# Large\n\n' + body + '\n\n## Large Tail\n';
        var started = Date.now();
        render(doc);
        rec.detail = 'length=' + doc.length + ' ms=' + (Date.now() - started);
        assert(!pv().hasAttribute('data-render-error'), 'large render must not fail');
        assert(has('#preview h1') && has('#preview h2'),
            'both headings from the large document must render');
    });

    test('render-pathological-markdown', function () {
        var lineSep = String.fromCharCode(0x2028);
        var doc =
            '# Pathological\n\n' +
            repeat('>', 120) + ' deep quote\n\n' +
            '```mermaid\nunclosed fence\n\n' +
            '$$\nunclosed math\n\n' +
            lineSep + '\u0000 emoji 🙂 عربى\n';
        render(doc);   // must not throw
        assert(!pv().hasAttribute('data-render-error'),
            'pathological input must not abort the render');
        assert(bodyText().indexOf('Pathological') >= 0,
            'the heading before the damage must still render');
        render('# recovered');
        assert(bodyText().indexOf('recovered') >= 0,
            'a later render must recover');
    });

    test('whole-render-failure-is-visible', function () {
        var original = window.md.render;
        window.md.render = function () { throw new Error('probe render failure'); };
        try {
            render('# will fail');
            assert(pv().hasAttribute('data-render-error'),
                'a failed render must leave a data-render-error marker');
            assert(has('.render-unavailable'),
                'a failed render must explain itself in the preview');
            assert(has('.render-fallback'),
                'a failed render must show the source, not a blank pane');
            assert(bodyText().indexOf('will fail') >= 0,
                'the fallback must contain the original source');
        } finally {
            window.md.render = original;
        }
        render('# recovered after failure');
        assert(bodyText().indexOf('recovered after failure') >= 0,
            'the renderer must recover after a failed render');
    });

    test('fence-isolation', function () {
        render([
            '# Isolation',
            '',
            'A valid paragraph.',
            '',
            '```plot',
            '{ this is not valid json',
            '```',
            '',
            '```chat[label][nonsense]',
            'bad option',
            '```',
            '',
            '![alt]()',
            '',
            '## Still Here'
        ].join('\n'));

        return settle().then(function () {
            assert(bodyText().indexOf('A valid paragraph.') >= 0,
                'content before a bad fence must render');
            assert(bodyText().indexOf('Still Here') >= 0,
                'content after a bad fence must render');
            assert(has('.plot-container pre'), 'the bad plot must show an error block');
            assert(has('.chat-error'), 'the bad chat fence must show an error block');
            assert(!pv().hasAttribute('data-render-error'),
                'one bad fence must not abort the whole document');
        });
    });

    test('chat-heading-ids-stay-unique', function () {
        render('# Dup\n\n```chat[label]\n# Dup\n```\n\n# Dup\n');
        var headings = pv().querySelectorAll('h1');
        var ids = [];
        var seen = {};
        for (var i = 0; i < headings.length; i++) {
            var id = headings[i].id || '';
            assert(id !== '', 'every heading must get an id');
            assert(!seen[id], 'duplicate heading id: ' + id);
            seen[id] = true;
            ids.push(id);
        }
        assert(ids.length === 3, 'expected three headings, got ' + ids.join(','));
    });

    test('csharp-fence-highlighting', function (rec) {
        render([
            '# C# aliases',
            '',
            '```csharp',
            'public sealed class A { public int N => 1; }',
            '```',
            '',
            '```cs',
            'public record B(int X);',
            '```',
            '',
            '```c#',
            'public enum E { A, B }',
            '```',
            '',
            '```c-sharp',
            'public interface I { void M(); }',
            '```',
            '',
            '```dotnet',
            'public class D { public static void Main() { Console.WriteLine("Hello World!"); } }',
            '```'
        ].join('\n'));

        assert(window.hljs && window.hljs.getLanguage('csharp'),
            'highlight.js must know the csharp language');
        var blocks = pv().querySelectorAll('.code-block-wrapper');
        assert(blocks.length === 5,
            'expected five C# alias blocks, got ' + blocks.length);

        var details = [];
        for (var i = 0; i < blocks.length; i++) {
            var langEl = blocks[i].querySelector('.code-lang');
            var lang = langEl ? text(langEl) : '';
            var spans = blocks[i].querySelectorAll(
                'code span[class^="hljs-"], code span[class*=" hljs-"]').length;
            details.push(lang + ':' + spans);
            assert(spans > 0,
                'C# alias "' + lang + '" produced no highlight spans');
        }

        // The display enrichment must also color the cases highlight.js leaves
        // plain: Console (type) and WriteLine (method call).
        var last = blocks[blocks.length - 1];
        var typeEl = last.querySelector('code .hljs-type');
        var fnEl = last.querySelector('code .hljs-title.function_');
        assert(typeEl && text(typeEl).indexOf('Console') >= 0,
            'the C# type Console must be wrapped as .hljs-type');
        assert(fnEl && text(fnEl).indexOf('WriteLine') >= 0,
            'the C# call WriteLine must be wrapped as .hljs-title.function_');
        rec.detail = details.join(', ') + ', type=' + text(typeEl) + ', call=' + text(fnEl);
    });

    // ── optional CDN libraries removed one at a time ────────────────────────
    function withGlobal(name, value, fn) {
        var old = window[name];
        var had = Object.prototype.hasOwnProperty.call(window, name);
        try {
            window[name] = value;
        } catch (e) {
            skip('window.' + name + ' is not writable in this host');
        }
        if (window[name] !== value) {
            if (had) window[name] = old;
            skip('window.' + name + ' is not writable in this host');
        }
        var restored = false;
        function restore() {
            if (restored) return;
            restored = true;
            if (had) window[name] = old;
            else {
                try { delete window[name]; } catch (e) { window[name] = old; }
            }
        }

        var result;
        try {
            result = fn();
        } catch (e) {
            restore();
            throw e;
        }

        // Async tests must keep the global removed until they finish; restoring
        // in a finally block here would undo the fault before the assertions run.
        if (result && typeof result.then === 'function') {
            return result.then(function (value) {
                restore();
                return value;
            }, function (e) {
                restore();
                throw e;
            });
        }

        restore();
        return result;
    }

    test('library-missing-mermaid', function () {
        return withGlobal('mermaid', undefined, function () {
            render('# Mermaid Missing\n\n```mermaid\ngraph TD\nA-->B\n```\n\n## After Mermaid');
            return settle().then(function () {
                assert(has('.render-unavailable'),
                    'a missing mermaid must leave a visible explanation');
                assert(bodyText().indexOf('After Mermaid') >= 0,
                    'the rest of the document must render');
                assert(!pv().hasAttribute('data-render-error'),
                    'a missing mermaid must not abort the document');
            });
        });
    });

    test('library-missing-function-plot', function () {
        return withGlobal('functionPlot', undefined, function () {
            render('# Plot Missing\n\n```plot\n{"data":[{"fn":"x"}]}\n```\n\n## After Plot');
            return settle().then(function () {
                var errors = pv().querySelectorAll('.plot-container pre');
                var joined = '';
                for (var i = 0; i < errors.length; i++) joined += text(errors[i]);
                assert(joined.indexOf('not loaded') >= 0,
                    'the plot must explain that function-plot is unavailable');
                assert(bodyText().indexOf('After Plot') >= 0,
                    'the rest of the document must render');
            });
        });
    });

    test('library-missing-katex', function () {
        return withGlobal('katex', undefined, function () {
            render('# Math Missing\n\nInline $x^2$ and block:\n\n$$\ny = x\n$$\n\n## After Math');
            return settle().then(function () {
                assert(has('.katex-error'), 'math must show an unavailable marker');
                assert(bodyText().indexOf('After Math') >= 0,
                    'the rest of the document must render');
                assert(!pv().hasAttribute('data-render-error'),
                    'a missing KaTeX must not abort the document');
            });
        });
    });

    test('library-missing-plantuml', function () {
        return withGlobal('plantumlEncoder', undefined, function () {
            render('# UML Missing\n\n```plantuml\n@startuml\nA -> B\n@enduml\n```\n\n## After UML');
            return settle().then(function () {
                assert(bodyText().indexOf('plantuml-encoder is unavailable') >= 0,
                    'the fence must explain that the encoder is unavailable');
                assert(bodyText().indexOf('After UML') >= 0,
                    'the rest of the document must render');
            });
        });
    });

    // ── placeholder / config through the literal bridge ─────────────────────
    test('placeholder-special-characters', function () {
        var placeholder = 'a\'b"c\\d\ne\t`f` 中文 🙂';
        window.setPreviewConfig({ placeholder: placeholder });
        render('');
        var shown = text(pv());
        assert(shown === placeholder,
            'placeholder text must round-trip exactly (got ' + JSON.stringify(shown) + ')');
        resetConfig();
    });

    // ── rapid updates ───────────────────────────────────────────────────────
    test('rapid-render-burst', function () {
        for (var i = 0; i < 40; i++) {
            render('# Rapid ' + i + '\n\nbody ' + i);
        }
        assert(bodyText().indexOf('Rapid 39') >= 0,
            'the last rapid render must win');
        assert(count('#preview h1') === 1,
            'rapid renders must not leave stale documents behind');
    });

    // ── scroll / heading APIs ───────────────────────────────────────────────
    test('scroll-and-heading-apis-clamp', function () {
        window.setScrollProgress(NaN);
        window.setScrollProgress(Infinity);
        window.setScrollProgress(-500);
        window.setScrollProgress(5000);

        var progress = window.getScrollProgress();
        assert(isFinite(progress) && progress >= 0 && progress <= 100,
            'scroll progress must stay finite and in range, got ' + progress);
        assert(window.navigateToHeading(null) === 0,
            'navigateToHeading(null) must return 0');
        assert(window.navigateToHeading('') === 0,
            'navigateToHeading("") must return 0');
    });

    // ── console / theme / custom CSS safety ─────────────────────────────────
    test('console-circular-object-safe', function () {
        var circular = {};
        circular.self = circular;
        console.log('circular', circular);
        console.warn('circular', circular);
        console.error('circular', circular);
        assert(true, 'unreachable');
    });

    test('custom-css-hostile-inputs', function () {
        window.setCustomCss(null);
        window.setCustomCss(undefined);
        window.setCustomCss(123);
        window.setCustomCss('');
        render('# after custom css');
        assert(bodyText().indexOf('after custom css') >= 0,
            'the preview must still render after junk CSS');
    });

    test('theme-invalid-values', function () {
        window.setTheme('purple');
        assert(document.documentElement.className.indexOf('theme-dark') >= 0,
            'an unknown theme must fall back to dark');
        window.setTheme(null);
        assert(document.documentElement.className.indexOf('theme-dark') >= 0,
            'a null theme must fall back to dark');
        window.setTheme('dark');
    });

    // ── export failures return instead of hanging ───────────────────────────
    test('export-invalid-member-calls-back', function () {
        function attempt(container) {
            return withTimeout(new Promise(function (resolve) {
                window.exportMemberAsync(container, 'plot', 'png', function (res) {
                    resolve(res);
                });
            }), 5000, 'export did not call back');
        }

        return attempt(document.body).then(function (res) {
            assert(res && res.ok === false, 'exporting a non-member must fail, not hang');

            var detached = document.createElement('div');
            detached.className = 'plot-container';
            return attempt(detached);
        }).then(function (res) {
            assert(res && res.ok === false,
                'exporting a detached member must fail, not hang');
        });
    });

    // ── error event does not throw back into the caller ─────────────────────
    test('onerror-synthetic-is-safe', function () {
        window.onerror('fault-probe-error', 'https://example.com/probe.js', 1, 1,
            new Error('fault-probe-error'));
        render('# after synthetic error');
        assert(bodyText().indexOf('after synthetic error') >= 0,
            'a synthetic error must not break rendering');
    });

    window.__faultCheck = {
        run: function () {
            state = { running: true, done: false, results: [] };
            runNext();
            return queue.length;
        },
        ready: function () { return state.done ? 1 : 0; },
        results: function () { return state; },
        version: '1'
    };
})();
