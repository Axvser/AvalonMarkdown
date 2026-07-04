# AvalonMarkdown

跨平台 Markdown 预览控件，基于 **AvaloniaUI** + **NativeWebView**。
写一次控件，运行于 Desktop (WebView2) / Browser (WASM) / Android / iOS。

## 安装

```bash
dotnet add package AvalonMarkdown
```

## 快速开始

### 1. 在 XAML 中声明

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### 2. 在代码中渲染

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Preview.OnReady += async (_, _) =>
        {
            await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
        };
    }
}
```

## API 参考

### 方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `RenderMarkdownAsync(string?)` | `Task` | 渲染 Markdown 内容 |
| `RestartPreviewAsync()` | `Task` | 重启预览器（重新加载 HTML + JS 环境） |
| `ApplyConfigAsync(string)` | `Task` | 执行 JS 配置调用 |
| `InvokeScriptAsync(string)` | `Task<string?>` | 执行自定义 JavaScript |

### 事件

| 事件 | 参数 | 触发时机 |
|------|------|----------|
| `OnReady` | `EventHandler` | 控件完全就绪，可安全调用 `RenderMarkdownAsync` |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>` | 内部可恢复错误 |

### 错误事件参数

| 成员 | 类型 | 说明 |
|------|------|------|
| `Title` | `string` | 错误标题 |
| `Message` | `string` | 错误详情 |
| `Timestamp` | `DateTime` | 错误发生时间 |

## 渲染能力

- **Markdown** — markdown-it 14 + footnote / task-lists
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS Code 风格配色
- **图表** — Mermaid 11（流程图、时序图、饼图、Git 图、类图）
- **代码块** — 语言标签 · 复制按钮 · 高度调节
- **任务列表** — 自定义复选框
- **脚注 / 表格 / 引用 / 删除线 / 上下标**

## 跨平台架构

```
┌─────────────────────────────────────────────────────────┐
│                    MarkdownView 控件                      │
│   (Avalonia UserControl + NativeWebView)                 │
├─────────┬──────────┬───────────┬────────────┬────────────┤
│ Desktop │ Browser  │  Android  │    iOS     │  未来平台   │
│ WebView2 │ WASM     │ WebView   │ WKWebView  │           │
│ file://  │ about:   │ data:     │ data:      │           │
│ 临时文件 │ blank +  │ base64    │ base64     │           │
│         │ doc.write │           │            │           │
└─────────┴──────────┴───────────┴────────────┴────────────┘
```

**加载策略：**
- **Desktop** — 写临时 HTML 文件 → `file:///` 导航（CDN 脚本完整执行）
- **Browser (WASM)** — `about:blank` → `document.write` 注入（兼容 WASM iframe）
- **Android / iOS** — `data:text/html;base64` 直接加载（无需 JavaScript 注入）

## 主题系统

支持自动跟随系统主题（Light / Dark），所有活动实例响应式同步：

```csharp
// 系统主题变化时自动推送
// 1. C# 端：WebViewHost / NativeWebView 背景色
// 2. JS 端：setTheme('light'|'dark') 切换 CSS class + Mermaid 主题
```

## 依赖

| 组件 | 用途 |
|------|------|
| Avalonia 12.0.1 | UI 框架 |
| CommunityToolkit.Mvvm 8.4 | MVVM |
| Avalonia.Controls.WebView 12.0.1 | 跨平台 WebView 封装 |
| markdown-it 14.1.0 | Markdown 解析 |
| highlight.js 11.10.0 | 代码高亮 |
| KaTeX 0.16.11 | 数学公式渲染 |
| Mermaid 11.4.1 | 图表渲染 |

所有 JS/CSS 依赖通过 `EmbeddedHtmlSourceProvider` 在构建时内联到 HTML，
**无需网络连接**，仅 KaTeX CSS 字体从 CDN 按需加载。

## 许可证

MIT © Axvser
