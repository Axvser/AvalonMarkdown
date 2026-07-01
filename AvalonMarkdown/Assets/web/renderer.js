(function() {
    // === Console → C# bridge ===
    var origLog = console.log, origWarn = console.warn, origError = console.error;
    var post = function(level, msg) {
        try { window.chrome.webview.postMessage('[' + level + '] ' + msg); } catch(e) {}
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

    console.log('Markdown Preview 初始化');
    console.log('UA: ' + navigator.userAgent);

    // ===== 预览配置 =====
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
        // 应用代码块最大高度
        applyCodeBlockMaxHeight();
        // 重新渲染以应用语言标签/复制按钮的显隐
        var preview = document.getElementById('preview');
        var text = preview.getAttribute('data-source') || '';
        if (text) window.renderMarkdown(text);
        console.log('[Config] 已更新', JSON.stringify(previewConfig));
    };

    function applyCodeBlockMaxHeight() {
        var style = document.getElementById('cb-max-height-style');
        if (!style) {
            style = document.createElement('style');
            style.id = 'cb-max-height-style';
            document.head.appendChild(style);
        }
        // 记录当前高度到预览配置，方便 resize 函数读取
        if (previewConfig.maxCodeBlockHeight > 0) {
            style.textContent = '.code-block-wrapper pre { max-height: ' + previewConfig.maxCodeBlockHeight + 'px; overflow-y: auto; }';
        } else {
            style.textContent = '.code-block-wrapper pre { max-height: none; overflow-y: visible; }';
        }
    }

    // 初始化时应用一次
    applyCodeBlockMaxHeight();

    // ===== 代码块高度调节（每个代码块独立）=====
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

    // ===== 复制代码 =====
    window.copyCode = function(btn) {
        var wrapper = btn.closest('.code-block-wrapper');
        var code = wrapper ? wrapper.querySelector('code') : null;
        var text = code ? code.textContent : '';
        if (!text) return;
        // 优先使用 Clipboard API
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function() {
                btn.textContent = '✓ 已复制';
                btn.classList.add('copied');
                setTimeout(function() { btn.textContent = '复制'; btn.classList.remove('copied'); }, 2000);
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
            btn.textContent = '✓ 已复制';
            btn.classList.add('copied');
            setTimeout(function() { btn.textContent = '复制'; btn.classList.remove('copied'); }, 2000);
        } catch(e) {}
        document.body.removeChild(ta);
    }

    // ===== 主题管理 =====
    var currentTheme = 'dark';
    var mermaidInitialized = false;

    function setTheme(theme) {
        if (theme !== 'dark' && theme !== 'light') theme = 'dark';
        currentTheme = theme;
        document.documentElement.className = 'theme-' + theme;
        console.log('[Theme] 切换到: ' + theme);

        // 重新初始化 Mermaid 主题
        var mermaidTheme = theme === 'dark' ? 'dark' : 'default';
        if (typeof mermaid !== 'undefined') {
            mermaid.initialize({
                startOnLoad: false,
                theme: mermaidTheme,
                securityLevel: 'loose'
            });
            mermaidInitialized = true;
            // 重新渲染已有的 mermaid 图表
            document.querySelectorAll('.mermaid-container .mermaid').forEach(function(el) {
                try { mermaid.run({ nodes: [el] }); } catch(e) {}
            });
            console.log('[Theme] Mermaid 主题已更新: ' + mermaidTheme);
        }
    }

    // C# 调用入口
    window.setTheme = setTheme;

    // ===== 初始化 Mermaid（跟随当前主题）=====
    if (typeof mermaid !== 'undefined') {
        var initialMermaidTheme = currentTheme === 'dark' ? 'dark' : 'default';
        mermaid.initialize({
            startOnLoad: false,
            theme: initialMermaidTheme,
            securityLevel: 'loose'
        });
        mermaidInitialized = true;
        console.log('[Mermaid] 主题: ' + initialMermaidTheme);
    }

    // === Markdown-it 配置（VS Code 风格）===
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
            // 构建头部：语言标签 + 复制按钮
            var parts = [];
            if (previewConfig.showCodeLanguage && lang) {
                parts.push('<span class="code-lang">' + escapeHtml(lang) + '</span>');
            }
            var actions = [];
            if (previewConfig.showCopyButton) {
                actions.push('<button class="copy-btn" onclick="copyCode(this)">复制</button>');
            }
            // 高度 +/- 按钮（每个代码块独立控制）
            actions.push('<button class="resize-btn" onclick="decreaseCodeHeight(this)" title="缩小">−</button>');
            actions.push('<button class="resize-btn" onclick="increaseCodeHeight(this)" title="放大">+</button>');
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

    // === 数学公式: VS Code 风格 $...$ / ... ===
    // 复制自 @vscode/markdown-it-katex 的解析逻辑
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

    // === 渲染器 ===
    function renderKatexInline(latex) {
        try {
            // detect display math from \begin env
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

    // === Mermaid: `mermaid 代码块 ===
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
        return origFence ? origFence.call(this, tokens, idx, options, env, self) : '';
    };

    // === Mermaid 渲染（页面加载后） ===
    function renderMermaid() {
        if (typeof mermaid !== 'undefined') {
            try {
                mermaid.run({ nodes: [document.querySelector('.mermaid-container')] });
            } catch(e) {
                console.warn('Mermaid renders after DOM');
            }
        }
    }

    // === 主渲染函数 ===
    window.renderMarkdown = function(text) {
        console.log('renderMarkdown 被调用, 长度=' + (text ? text.length : 0));
        var preview = document.getElementById('preview');
        if (!preview) { console.error('preview 元素不存在'); return; }
        if (!text) {
            preview.innerHTML = '<p style="color:#888;text-align:center;margin-top:80px;">← 输入 Markdown</p>';
            preview.removeAttribute('data-source');
            return;
        }
        preview.setAttribute('data-source', text);
        try {
            preview.innerHTML = md.render(text);
            preview.querySelectorAll('.mermaid').forEach(function(el) {
                try { mermaid.run({ nodes: [el] }); } catch(e) {}
            });
            console.log('✅ 渲染完成');
        } catch(e) {
            console.error('❌ ' + e.message);
            preview.innerHTML = '<pre>' + escapeHtml(text) + '</pre>';
        }
    };

    window.onMarkdownUpdate = function(text) { window.renderMarkdown(text); };
    window.md = md;
    window.escapeHtml = escapeHtml;
    console.log('✅ 初始化完成');
})();
