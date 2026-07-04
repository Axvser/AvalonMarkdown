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

    // === 全局错误捕获 — 在控件内显示而非静默吞掉 ===
    window.onerror = function(msg, source, line, col, error) {
        // 静默记录所有错误，不显示 UI 浮层。
        // 需要错误处理时请订阅 MarkdownView.ErrorOccurred 事件。
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

    // 供 C# 侧手动调用（window.showPreviewError(detail)），默认不自动触发
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
    // 从 html 标签的 class 读取当前主题，而非硬编码
    var currentTheme = document.documentElement.className.indexOf('theme-light') >= 0 ? 'light' : 'dark';
    var mermaidInitialized = false;

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
        console.log('[Theme] 切换到: ' + theme);

        if (typeof mermaid !== 'undefined') {
            mermaid.initialize(getMermaidThemeVars(theme));
            mermaidInitialized = true;
            // Mermaid 渲染后会替换 <pre class="mermaid"> 为 SVG，
            // 对已渲染节点再调 mermaid.run 无效。此处通过重渲染整个 Markdown
            // 重建 .mermaid 元素并使用新主题
            reRenderMarkdown();
            console.log('[Theme] Mermaid 主题已更新');
        }
    }

    function reRenderMarkdown() {
        var preview = document.getElementById('preview');
        if (!preview) return;
        var source = preview.getAttribute('data-source');
        if (!source) return;
        try {
            preview.innerHTML = md.render(source);
            preview.querySelectorAll('.mermaid').forEach(function(el) {
                try { mermaid.run({ nodes: [el] }); } catch(e) {}
            });
        } catch(e) {
            console.error('❌ ' + e.message);
        }
    }

    // C# 调用入口
    window.setTheme = setTheme;

    // ===== 初始化 Mermaid（跟随当前主题）=====
    if (typeof mermaid !== 'undefined') {
        mermaid.initialize(getMermaidThemeVars(currentTheme));
        mermaidInitialized = true;
        console.log('[Mermaid] 主题已初始化');
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

    // 通知 C# 侧所有 CDN 脚本和 renderer.js 已就绪
    try { window.chrome.webview.postMessage('[READY]'); } catch(e) {}
})();
