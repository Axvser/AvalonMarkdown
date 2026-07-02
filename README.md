# AvalonMarkdown

跨平台 Markdown 实时预览器，基于 **AvaloniaUI 12** + **NativeWebView**。
核心控件 `MarkdownView` 写一次，支持 Desktop / Browser / Android / iOS 全平台。

## 快速开始

```bash
# Desktop
dotnet run --project AvalonMarkdown.Desktop -c Debug

# Browser（访问 http://localhost:5235）
dotnet run --project AvalonMarkdown.Browser -c Debug
```

## 平台支持

| 平台 | 状态 | WebView 方式 |
|------|------|-------------|
| 🖥️ Desktop | ✅ 可用 | WebView2 (Windows) / WebKit (macOS/Linux) |
| 🌐 Browser (WASM) | ✅ 可用 | Iframe (`about:blank` + `document.write`) |
| 📱 Android | ✅ 已发布测试 | WebView 原生控件 |
| 📱 iOS | ⏳ 待测试 | — |

## 架构概要

```
┌─ AvalonMarkdown (共享主项目) ──────────────────────────┐
│                                                        │
│  MarkdownView (统一控件)                                │
│    ├─ 工具栏（Reload / Hide / Status）                  │
│    ├─ NativeWebView（封装）                              │
│    └─ 错误面板（C# + JS 双层）                           │
│                                                        │
│  EmbeddedHtmlSourceProvider                             │
│    ├─ 通过 AssetLoader 读取嵌入式资源                    │
│    ├─ 内联 renderer.css / renderer.js                   │
│    └─ 注入当前系统主题 class                            │
│                                                        │
│  Assets/web/                                            │
│    ├─ index.html   — 预览页面模板                       │
│    ├─ renderer.css — VS 风格双主题样式                  │
│    └─ renderer.js  — 渲染引擎 IIFE                     │
│                                                        │
├─ AvalonMarkdown.Desktop ── MainWindow → MainView → MarkdownView
├─ AvalonMarkdown.Browser ── MainView → MarkdownView
├─ AvalonMarkdown.Android ── MainView → MarkdownView
└─ AvalonMarkdown.iOS ────── MainView → MarkdownView
```

## 渲染能力

- **Markdown 解析** — markdown-it 14 + footnote / task-lists
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS 风格配色（190+ 语言）
- **图表** — Mermaid 11（流程图 / 时序图 / 饼图 / Git 图）
- **代码块** — 语言标签 · 复制按钮 · 高度 +/- 调节
- **任务列表** — 自定义 SVG 复选框
- **脚注 / 表格 / 引用 / 删除线**

## 技术栈

| 组件 | 版本 | 用途 |
|------|------|------|
| Avalonia | 12.0.1 | UI 框架 |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM |
| markdown-it | 14.1.0 | Markdown 解析 |
| highlight.js | 11.10.0 | 代码高亮 |
| KaTeX | 0.16.11 | 数学公式 |
| Mermaid | 11.4.1 | 图表 |

JS/CSS 依赖通过 CDN 加载。

## 许可证

MIT
