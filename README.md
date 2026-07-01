# AvalonMarkdown

Markdown 实时预览器，基于 **AvaloniaUI 12** + **Web**。

## 平台支持

| 平台 | 状态 | 渲染方式 |
|------|------|----------|
| 🖥️ Desktop (Windows / macOS / Linux) | ✅ 可用 | WebView (`NativeWebView`) |
| 🌐 Browser (WebAssembly) | ✅ 可用 | 纯 HTML/JS (`<textarea>` + `<div>`) |
| 📱 Android / iOS | ⏳ 待测试 | — |

## 快速开始

```bash
# 桌面端
dotnet run --project AvalonMarkdown.Desktop -c Debug

# 浏览器端（访问 http://localhost:5235）
dotnet run --project AvalonMarkdown.Browser -c Debug
```

## 渲染能力

- **Markdown 解析** — markdown-it 14 + footnote / task-lists 插件
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS 风格配色（190+ 语言）
- **图表** — Mermaid（流程图、时序图等）
- **代码块** — 语言标签 · 复制按钮 · 高度 +/- 调节 · 垂直滚动条
- **任务列表** — 自定义复选框（SVG checkmark）
- **表格 / 引用 / 脚注 / 删除线**

## 架构

```
Assets/web/renderer.css  ─┐
Assets/web/renderer.js   ─┤── 共享渲染引擎
                          │
Desktop (NativeWebView) ←─┘  Browser (纯 HTML/JS)
```

共享 `renderer.css` + `renderer.js` 被双平台引用，核心逻辑一份即可。

## 技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| Avalonia | 12.0.1 | UI 框架 |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM |
| markdown-it | 14.1.0 | Markdown 解析 |
| highlight.js | 11.10.0 | 代码高亮 |
| KaTeX | 0.16.11 | 数学公式 |
| Mermaid | 11.4.1 | 图表 |

所有 JS/CSS 依赖通过 CDN 加载。

## 许可证

MIT
