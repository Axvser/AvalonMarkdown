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

| 方法                             | 返回              | 说明                                  |
| -------------------------------- | ----------------- | ------------------------------------- |
| `RenderMarkdownAsync(string?)` | `Task`          | 渲染 Markdown 内容                    |
| `RestartPreviewAsync()`        | `Task`          | 重启预览器（重新加载 HTML + JS 环境） |
| `ApplyConfigAsync(string)`     | `Task`          | 执行 JS 配置调用（如 `setPreviewConfig({...})`） |
| `InvokeScriptAsync(string)`    | `Task<string?>` | 执行自定义 JavaScript                 |
| `ApplyCustomCssAsync(string)`  | `Task`          | 注入自定义 CSS 文本以覆盖渲染器主题样式 |

### 事件

| 事件              | 参数                                         | 触发时机                                        |
| ----------------- | -------------------------------------------- | ----------------------------------------------- |
| `OnReady`       | `EventHandler`                             | 控件完全就绪，可安全调用`RenderMarkdownAsync` |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>` | 内部可恢复错误                                  |
| `LinkClicked` | `EventHandler<LinkClickedEventArgs>` | Markdown 内容中的超链接被点击。默认行为根据平台自动选择：**Desktop** → 系统默认浏览器，**Browser** → 新标签页，**Mobile** → 系统默认浏览器。设置 `Handled = true` 可完全自定义 |

### 错误事件参数

| 成员          | 类型         | 说明         |
| ------------- | ------------ | ------------ |
| `Title`     | `string`   | 错误标题     |
| `Message`   | `string`   | 错误详情     |
| `Timestamp` | `DateTime` | 错误发生时间 |

## MarkdownThemeView（主题编辑器）

内置的即用型主题编辑控件，提供 RGB 滑块实时自定义 MarkdownView 渲染外观。

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

支持自定义：
- **6 种核心颜色**：背景、文字、链接、标题、行内代码、边框（RGB 三通道滑块）
- **6 种扩展颜色**：次级背景、次级文字、行内代码背景、代码块背景、表格表头背景
- **排版设置**：正文字号、代码字号、行高、圆角
- **highlight.js 颜色**：关键字、字符串、注释、类型等独立控制
- **自动推送**：修改后自动注入 CSS 到绑定的 MarkdownView 控件

## 渲染能力

- **Markdown** — markdown-it 14 + footnote / task-lists / 删除线
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS Code 风格配色（支持自定义颜色覆盖）
- **图表** — Mermaid 11（流程图、时序图、饼图、Git 图、类图）
- **PlantUML** — 通过 `plantuml-encoder` 编码后调用 PlantUML 在线服务渲染 SVG，自动适配深色/浅色主题
- **视频嵌入** — 支持直接视频文件（`.mp4` / `.webm` / `.ogg` / `.mov` / `.avi` / `.mkv`）和平台 URL 自动识别
  - **YouTube** — `youtube.com/watch?v=ID` / `youtu.be/ID` → 响应式 iframe 嵌入
  - **Bilibili** — `bilibili.com/video/BVxxx` → 响应式 iframe 嵌入
  - **Vimeo** — `vimeo.com/ID` → 响应式 iframe 嵌入
- **代码块** — 语言标签 · 复制按钮 · 高度调节（+/- 逐块独立控制）· 可配置最大高度
- **任务列表** — 自定义复选框
- **脚注 / 表格 / 引用 / 删除线**
- **预览配置** — 通过 `setPreviewConfig` 动态调整字体大小、行高、代码语言标签显示、复制按钮开关、代码块最大高度
- **主题编辑器** — 内置 `MarkdownThemeView` 控件，提供 RGB 滑块实时自定义颜色与排版

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

## 依赖

| 组件                             | 用途          |
| -------------------------------- | ------------- |
| Avalonia 12.0.0                  | UI 框架       |
| Avalonia.Controls.WebView 12.0.0 | WebView       |
| markdown-it 14.1.0               | Markdown 解析 |
| highlight.js 11.10.0             | 代码高亮      |
| KaTeX 0.16.11                    | 数学公式渲染  |
| Mermaid 11.4.1                   | 图表渲染      |
| plantuml-encoder 1.4.0          | PlantUML 编码 |

所有 JS/CSS 依赖通过 `EmbeddedHtmlSourceProvider` 在构建时内联到 HTML，
**无需网络连接**，仅 KaTeX CSS 字体与 PlantUML SVG 渲染需在线服务。

## 主题系统

支持自动跟随系统主题（Light / Dark），所有活动实例响应式同步：

```csharp
// 系统主题变化时自动推送
// 1. C# 端：WebViewHost / NativeWebView 背景色
// 2. JS 端：setTheme('light'|'dark') 切换 CSS class + Mermaid 主题 + 重新渲染
```

通过 `MarkdownThemeView` 控件可实现实时颜色自定义，修改结果通过 `setCustomCss()` JS 接口自动注入 WebView。

## 许可证

MIT © Axvser
