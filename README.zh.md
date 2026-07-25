# AvalonMarkdown

> 🌏 [English Documentation](README.md)

Markdown 预览控件，基于 **AvaloniaUI** 的 **NativeWebView** 构建。

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

| 属性   | 类型     | 绑定模式 | 说明                                                                       |
| ------ | -------- | -------- | -------------------------------------------------------------------------- |
| `Text` | `string?` | TwoWay   | Markdown 文本，自动渲染（带 Myers diff 去重避免重复渲染）                   |

### 方法

| 方法                                      | 返回              | 说明                                                                                             |
| ----------------------------------------- | ----------------- | ------------------------------------------------------------------------------------------------ |
| `RenderMarkdownAsync(string?)`            | `Task`            | 渲染 Markdown 内容                                                                               |
| `RestartPreviewAsync()`                   | `Task`            | 重启预览器（重新建立本地 HTTP 服务器 + 导航）                                                    |
| `ApplyConfigAsync(string)`                | `Task`            | 执行 JS 配置表达式（如 `"setPreviewConfig({fontSize:16})"`）                                     |
| `InvokeScriptAsync(string)`               | `Task<string?>`   | 执行自定义 JavaScript                                                                             |
| `ApplyCustomCssAsync(string)`             | `Task`            | 注入自定义 CSS 以覆盖渲染器主题样式                                                               |

### 事件

| 事件            | 参数                                       | 触发时机                                                                                                             |
| --------------- | ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------- |
| `OnReady`       | `EventHandler`                             | 控件完全就绪（HTML 加载 + JS CDN 脚本加载完成），可安全调用 `RenderMarkdownAsync`                                     |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>` | 内部可恢复错误（JS 运行时错误 / CDN 加载超时 / 脚本超时 / 链接打开失败等）                                            |

### 内置错误面板

`MarkdownView` 底部自带错误面板（初始隐藏），当内部错误发生时自动显示：

| 控件            | 说明                                                 |
| --------------- | ---------------------------------------------------- |
| `ErrorTitle`    | 错误标题（红色）                                     |
| `ErrorMessage`  | 错误详情（自动换行）                                 |
| `RetryButton`   | 点击调用 `RestartPreviewAsync()` 重启预览            |
| `DismissErrorButton` | 点击关闭错误面板                                |

### 错误事件参数

| 成员        | 类型         | 说明         |
| ----------- | ------------ | ------------ |
| `Title`     | `string`     | 错误标题     |
| `Message`   | `string`     | 错误详情     |
| `Timestamp` | `DateTime`   | 错误发生时间 |

## MarkdownThemeView（主题编辑器）

内置的即用型主题编辑控件，提供 RGB 滑块实时自定义 MarkdownView 渲染外观。

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

默认收起，点击标题栏展开编辑面板。支持自定义：

### 颜色（RGB 三通道滑块 + 实时十六进制预览）

- **6 种核心颜色**：背景、文字、链接、标题、行内代码文字、边框
- **6 种扩展颜色**：次级背景、次级文字、行内代码背景、代码块背景、表格表头背景、滚动条（滑块/悬停）
- **引用块颜色**：引用块左边框、引用块背景
- **自动派生**：修改核心颜色 R/G/B 时，次级背景、次级文字、行内代码背景、代码块背景、表格表头背景、引用块边框、滚动条颜色自动跟随

### 排版

| 属性             | 默认值 | 说明             |
| ---------------- | ------ | ---------------- |
| 正文字号 (`BodyFontSize`) | 14px   | —                |
| 代码字号 (`CodeFontSize`) | 13px   | —                |
| 行高   (`LineHeight`)     | 1.6    | —                |
| 圆角   (`BorderRadius`)   | 6px    | 代码块圆角       |

### Mermaid 图表

| 属性                            | 默认值 | 说明               |
| ------------------------------- | ------ | ------------------ |
| 主题 (`MermaidTheme`)           | `dark` | `dark` / `light` / `base` |
| 容器背景 (`MermaidBgHex`)       | `#1E1E1E` | RGB 三通道滑块     |
| 容器内边距 (`MermaidContainerPadding`) | 8px  | —                  |
| 容器外边距 (`MermaidContainerMargin`)  | 16px | —                  |
| 容器圆角 (`MermaidBorderRadius`)       | 4px  | —                  |

### PlantUML 图表

| 属性                          | 默认值  | 说明                    |
| ----------------------------- | ------- | ----------------------- |
| 容器背景 (`PumlBgHex`)        | `#1E1E1E` | RGB 三通道滑块          |
| 容器内边距 (`PumlContainerPadding`) | 12px | —                       |
| 容器外边距 (`PumlContainerMargin`)  | 8px  | —                       |
| 容器圆角 (`PumlBorderRadius`)       | 6px  | —                       |
| 深色反转滤镜 (`PumlDarkInvert`)     | 0.882 | 深色模式下 CSS `filter: invert()` 值 |

### highlight.js 代码高亮颜色

支持独立控制以下语法高亮颜色类别：

| 组               | 属性（部分示例）                   | 默认值      |
| ---------------- | ---------------------------------- | ----------- |
| 关键字/字面量/符号 | `HljsKeyword` / `Literal` / `Symbol` / `Name` | `#569cd6` |
| 内置函数/类型     | `HljsBuiltIn` / `Type`            | `#4ec9b0` |
| 类名/数字         | `HljsClass` / `Number`            | `#b5cea8` |
| 字符串元字符串     | `HljsString` / `MetaString`       | `#d69d85` |
| 标题              | `HljsTitle` / `TitleClass` / `TitleClassInherited` | `#DCDCAA` / `#4EC9B0` |
| 参数/变量         | `HljsParams` / `Variable` / `TemplateVariable` | `#9CDCFE` / `#bd63c5` |
| 注释/引用         | `HljsComment` / `Quote`           | `#6a9955` |
| 属性/标签/元      | `HljsAttr` / `Attribute` / `Meta` / `Tag` | `#9cdcfe` / `#9b9b9b` / `#569cd6` |
| 选择器            | `HljsSelectorAttr` / `SelectorClass` / `SelectorId` 等 | `#d7ba7d` |
| 背景/前景         | `HljsBackground` / `Foreground`   | `#1e1e1e` / `#dcdcdc` |

### 自动推送

所有属性修改后自动标记脏数据，由 10Hz 定时器统一调用 `ApplyCustomCssAsync` 注入到绑定的 MarkdownView 控件（WeakReference 持有，不阻塞 GC）。

## 渲染能力

renderer.js 通过 CDN 加载第三方库，在 WebView 内执行完整渲染管线：

- **Markdown** — markdown-it 14.1.0 + footnote 4.0.0 + task-lists 2.1.1 + 删除线
- **数学公式** — KaTeX 0.16.11（行内 `$...$` / 块级 `$$...$$`）
- **代码高亮** — highlight.js 11.10.0，VS Code 风格配色（支持通过 `ApplyCustomCssAsync` / 主题编辑器自定义所有 token 颜色）
- **图表** — Mermaid 11.4.1（流程图、时序图、饼图、Git 图、类图、状态图）
- **PlantUML** — 通过 `plantuml-encoder` 1.4.0 编码后调用 PlantUML 在线服务渲染 SVG，深色/浅色主题自适应（CSS `invert()` + `hue-rotate()`）
- **视频嵌入** — 支持直接视频文件（`.mp4` / `.webm` / `.ogg` / `.mov` / `.avi` / `.mkv`）和平台 URL 自动识别
  - **YouTube** — `youtube.com/watch?v=ID` / `youtu.be/ID` → 响应式 iframe 嵌入
  - **Bilibili** — `bilibili.com/video/BVxxx` → 响应式 iframe 嵌入
  - **Vimeo** — `vimeo.com/ID` → 响应式 iframe 嵌入
- **代码块** — 语言标签 · 复制按钮（`navigator.clipboard` 或 `document.execCommand` 回退）· 高度调节（+/- 逐块独立控制）· 可配置最大高度（`maxCodeBlockHeight`）
- **任务列表** — 自定义复选框
- **脚注 / 表格 / 引用 / 删除线**
- **外部链接** — 自动拦截，通过 C# bridge 触发系统浏览器打开（WASM 环境回退 `window.open`）
- **预览配置** — 通过 `setPreviewConfig` 动态调整字体大小（`fontSize`）、行高（`lineHeight`）、代码语言标签显示（`showCodeLanguage`）、复制按钮开关（`showCopyButton`）、代码块最大高度（`maxCodeBlockHeight`）
- **主题编辑器** — 内置 `MarkdownThemeView` 控件，提供 RGB 滑块实时自定义颜色、排版、Mermaid / PlantUML 样式与代码高亮配色

### JS 端公开全局函数

| 函数                                  | 说明                                                          |
| ------------------------------------- | ------------------------------------------------------------- |
| `window.renderMarkdown(text)`         | 渲染 Markdown 文本到预览区                                    |
| `window.onMarkdownUpdate(text)`       | `renderMarkdown` 别名                                         |
| `window.setPreviewConfig(config)`     | 更新预览配置并重新渲染                                        |
| `window.setTheme(theme)`              | 切换主题（`'light'` / `'dark'`），更新 CSS class + Mermaid 主题 + 重新渲染 |
| `window.setCustomCss(cssText)`        | 替换 `<style id="custom-theme-css">` 内容以覆盖默认样式        |
| `window.showPreviewError(detail)`     | 显示 JS 运行时错误覆盖层                                      |
| `window.dismissErrorOverlay()`        | 关闭 JS 运行时错误覆盖层                                      |
| `window.escapeHtml(s)`                | HTML 转义工具函数                                             |

### WebView 错误处理

- **C# 端**：`MarkdownView` 内置底部错误面板（ErrorPanel），发生错误时自动显示，提供重试与关闭按钮
- **JS 端**：WebView 内也有 `error-overlay` 覆盖层（带关闭按钮），由 `window.onerror` 和 `unhandledrejection` 自动捕获触发
- 所有错误同步通过 `ErrorOccurred` 事件抛出，可供外部订阅处理

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

所有平台统一使用 `EmbeddedHtmlSourceProvider`（实现 `IWebViewSourceProvider` 接口）在运行时读取嵌入资源并内联 `renderer.css` / `renderer.js`，然后根据不同平台采用不同加载路径：

- **Desktop（WebView2）** — 启动 `LocalHtmlServer`（循环回环 `http://127.0.0.1:dynamic-port`）→ 导航到该地址
  - 使用 `http://` 而非 `file://` 以避免第三方 iframe（如 YouTube）的同源策略限制
- **Android / iOS** — 与 Desktop 相同：启动 `LocalHtmlServer` → `http://127.0.0.1:dynamic-port` 导航
- **Browser（WASM）** — `about:blank` → `document.write` 注入完整 HTML（WASM 沙箱中无法启动 TCP 服务）

> **扩展点**：`IWebViewSourceProvider` 接口允许自定义 HTML 内容源，用于注入不同的页面结构或 CDN 镜像地址。

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
| markdown-it 14.1.0 / footnote 4.0.0 / task-lists 2.1.1  | Markdown 解析           | CDN（运行时加载）        |
| highlight.js 11.10.0                        | 代码高亮                | CDN（运行时加载）        |
| KaTeX 0.16.11 / katex.min.css                | 数学公式渲染 + CSS 样式     | CDN（运行时加载）        |
| Mermaid 11.4.1                              | 图表渲染                | CDN（运行时加载）        |
| plantuml-encoder 1.4.0                      | PlantUML 编码           | CDN（运行时加载）        |

> **网络需求**：仅 `renderer.js` / `renderer.css` / `index.html` 通过嵌入资源在构建时内联。
> CDN 库（markdown-it 及其插件、highlight.js、KaTeX 含 CSS、Mermaid、plantuml-encoder）**需要运行时网络加载**。
> 若 CDN 被网络或跟踪防护拦截，仅影响对应功能（如数学公式或图表无法渲染），基本 Markdown 预览不受影响。
> 控件内部有 15 秒 JS 就绪超时检测，超时后通过错误面板提示。

## 主题系统

支持自动跟随系统主题（Light / Dark），所有活动实例（通过 WeakReference 静态列表管理）响应式同步：

```csharp
// 系统主题变化时自动推送
// 1. C# 端：WebViewHost / NativeWebView 背景色
// 2. JS 端：setTheme('light'|'dark') 切换 CSS class + Mermaid 主题 + 重新渲染
```

- 首次创建 `MarkdownView` 实例时，订阅 `Application.Current.ActualThemeVariantChanged` 全局事件
- 主题变化后遍历静态实例列表，逐一调用 `PushThemeToWebView` 推送到每个 WebView
- 同时更新 `LocalHtmlServer` 返回的 HTML 中的 theme class，确保重启/刷新时一致

通过 `MarkdownThemeView` 控件可实现实时颜色自定义，修改结果通过 `ApplyCustomCssAsync` 接口自动注入 WebView。使用 10Hz `DispatcherTimer` 脏标记推送，避免高频滑块拖动时产生过多脚本调用。

## 许可证

MIT © Axvser
