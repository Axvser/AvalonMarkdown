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

### 2. 数据绑定方式（推荐）

绑定 Markdown 文本到 `Text` 属性，控件自动渲染并去重：

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

### 3. 事件驱动方式

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

### 属性

| 属性     | 类型     | 绑定模式 | 说明                                                   |
| -------- | -------- | -------- | ------------------------------------------------------ |
| `Text` | `string?` | TwoWay   | Markdown 文本，自动渲染（带 Myers diff 去重避免重复渲染） |

### 方法

| 方法                                   | 返回              | 说明                                                         |
| -------------------------------------- | ----------------- | ------------------------------------------------------------ |
| `RenderMarkdownAsync(string?)`       | `Task`          | 渲染 Markdown 内容                                           |
| `RestartPreviewAsync()`              | `Task`          | 重启预览器（重新建立本地 HTTP 服务器 + 导航）                  |
| `ApplyConfigAsync(string)`           | `Task`          | 执行 JS 配置表达式（如 `"setPreviewConfig({fontSize:16})"`） |
| `InvokeScriptAsync(string)`          | `Task<string?>` | 执行自定义 JavaScript                                        |
| `ApplyCustomCssAsync(string)`        | `Task`          | 注入自定义 CSS 以覆盖渲染器主题样式                           |

### 事件

| 事件              | 参数                                         | 触发时机                                                   |
| ----------------- | -------------------------------------------- | ---------------------------------------------------------- |
| `OnReady`       | `EventHandler`                             | 控件完全就绪（HTML 加载 + JS CDN 脚本加载完成），可安全调用 `RenderMarkdownAsync` |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>` | 内部可恢复错误                                              |

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
- **自动推送**：修改后自动调用 `ApplyCustomCssAsync` 注入到绑定的 MarkdownView 控件

## 渲染能力

- **Markdown** — markdown-it 14 + footnote / task-lists / 删除线
- **数学公式** — KaTeX（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11，VS Code 风格配色（支持通过 `ApplyCustomCssAsync` 自定义颜色覆盖）
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
┌───────────────────────────────────────────────────────────┐
│                    MarkdownView 控件                       │
│   (Avalonia UserControl + NativeWebView)                   │
├───────────┬───────────┬───────────┬───────────┬───────────┤
│  Desktop  │  Browser  │  Android  │    iOS    │  未来平台  │
│ WebView2  │   WASM    │ WebView   │ WKWebView │           │
│ http://   │ about:    │ http://   │ http://   │           │
│ 127.0.0.1 │ blank +   │ 127.0.0.1 │ 127.0.0.1 │           │
│ :dynport  │ doc.write │ :dynport  │ :dynport  │           │
└───────────┴───────────┴───────────┴───────────┴───────────┘
```

### 加载策略

所有平台统一使用 `EmbeddedHtmlSourceProvider` 在运行时读取嵌入资源并内联 `renderer.css` / `renderer.js`，然后根据不同平台采用不同加载路径：

- **Desktop（WebView2）** — 启动 `LocalHtmlServer`（循环回环 `http://127.0.0.1:dynamic-port`）→ 导航到该地址
  - 使用 `http://` 而非 `file://` 以避免第三方 iframe（如 YouTube）的同源策略限制
- **Android / iOS** — 与 Desktop 相同：启动 `LocalHtmlServer` → `http://127.0.0.1:dynamic-port` 导航
- **Browser（WASM）** — `about:blank` → `document.write` 注入完整 HTML（WASM 沙箱中无法启动 TCP 服务）

### 平台就绪信号差异

| 平台      | 就绪检测机制                                                                 |
| --------- | ---------------------------------------------------------------------------- |
| Desktop   | `NavigationCompleted` 时 CDN 脚本已加载完成，直接 `SetReady()`                 |
| Android   | `NavigationCompleted` 早于 CDN 脚本加载 → 轮询 `typeof window.renderMarkdown === 'function'`（200ms 间隔，15s 超时） |
| iOS       | 与 Android 相同（同一体系架构）                                                |
| Browser   | `InjectViaDocumentWriteAsync` 完成后通过 `document.write` 同步执行 CDN 脚本    |

## 依赖

| 组件                                        | 用途                   | 加载方式                |
| ------------------------------------------- | ---------------------- | ----------------------- |
| Avalonia                                    | UI 框架                 | NuGet                   |
| Avalonia.Controls.WebView                   | NativeWebView 控件      | NuGet                   |
| renderer.js（内联）                         | 渲染器核心逻辑          | 嵌入资源（构建时内联）    |
| renderer.css（内联）                        | 渲染器样式              | 嵌入资源（构建时内联）    |
| markdown-it 14.1.0 / footnote / task-lists  | Markdown 解析           | CDN（运行时加载）        |
| highlight.js 11.10.0                        | 代码高亮                | CDN（运行时加载）        |
| KaTeX 0.16.11                               | 数学公式渲染            | CDN（运行时加载）        |
| Mermaid 11.4.1                              | 图表渲染                | CDN（运行时加载）        |
| plantuml-encoder 1.4.0                      | PlantUML 编码           | CDN（运行时加载）        |

> **网络需求**：仅 `renderer.js` / `renderer.css` / `index.html` 通过嵌入资源在构建时内联。
> CDN 库（markdown-it、highlight.js、KaTeX、Mermaid、plantuml-encoder）**需要运行时网络加载**。
> 若 CDN 被网络或跟踪防护拦截，仅影响对应功能（如数学公式或图表无法渲染），基本 Markdown 预览不受影响。

## 主题系统

支持自动跟随系统主题（Light / Dark），所有活动实例响应式同步：

```csharp
// 系统主题变化时自动推送
// 1. C# 端：WebViewHost / NativeWebView 背景色
// 2. JS 端：setTheme('light'|'dark') 切换 CSS class + Mermaid 主题 + 重新渲染
```

通过 `MarkdownThemeView` 控件可实现实时颜色自定义，修改结果通过 `ApplyCustomCssAsync` 接口自动注入 WebView。

## 许可证

MIT © Axvser
