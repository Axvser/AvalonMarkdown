# AvalonMarkdown

> 🌏 [中文文档](README.zh.md)

A Markdown preview control built on **AvaloniaUI**'s **NativeWebView**.

## Installation

```bash
dotnet add package AvalonMarkdown
```

## Quick Start

### 1. Declare in XAML

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### 2. Data Binding (Recommended)

Bind Markdown text to the `Text` property; the control automatically renders and deduplicates updates:

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

### 3. Event-Driven Approach

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

## API Reference

### Properties

| Property | Type     | Binding Mode | Description |
| -------- | -------- | ------------ | ----------- |
| `Text`   | `string?` | TwoWay       | Markdown text; auto-renders with Myers diff deduplication to avoid redundant renders |

### Methods

| Method                                       | Return            | Description |
| -------------------------------------------- | ----------------- | ----------- |
| `RenderMarkdownAsync(string?)`               | `Task`            | Renders Markdown content |
| `RestartPreviewAsync()`                      | `Task`            | Restarts the previewer (re-establishes local HTTP server + navigation) |
| `ApplyConfigAsync(string)`                   | `Task`            | Executes a JS config expression (e.g., `"setPreviewConfig({fontSize:16})"`) |
| `InvokeScriptAsync(string)`                  | `Task<string?>`   | Executes custom JavaScript |
| `ApplyCustomCssAsync(string)`                | `Task`            | Injects custom CSS to override renderer theme styles |

### Events

| Event           | Parameters                                  | Trigger Condition |
| --------------- | ------------------------------------------- | ----------------- |
| `OnReady`       | `EventHandler`                              | Control is fully ready (HTML loaded + JS CDN scripts loaded); safe to call `RenderMarkdownAsync` |
| `ErrorOccurred` | `EventHandler<MarkdownViewErrorEventArgs>`  | Internal recoverable errors (JS runtime errors / CDN load timeout / script timeout / link open failures, etc.) |

### Built-in Error Panel

`MarkdownView` includes a built-in error panel at the bottom (initially hidden), which automatically appears when internal errors occur:

| Control             | Description |
| ------------------- | ----------- |
| `ErrorTitle`        | Error title (red) |
| `ErrorMessage`      | Error details (auto-wrapped) |
| `RetryButton`       | Click to call `RestartPreviewAsync()` and restart the preview |
| `DismissErrorButton`| Click to dismiss the error panel |

### Error Event Arguments

| Member      | Type       | Description |
| ----------- | ---------- | ----------- |
| `Title`     | `string`   | Error title |
| `Message`   | `string`   | Error details |
| `Timestamp` | `DateTime` | Time the error occurred |

## MarkdownThemeView (Theme Editor)

A ready-to-use theme editing control that provides RGB sliders for real-time customization of the MarkdownView rendering appearance.

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

Collapsed by default; expand the editing panel by clicking the title bar. Supports customization of:

### Colors (RGB Three-Channel Sliders + Real-Time Hex Preview)

- **6 Core Colors**: Background, Text, Link, Heading, Inline Code Text, Border
- **6 Extended Colors**: Secondary Background, Secondary Text, Inline Code Background, Code Block Background, Table Header Background, Scrollbar (Thumb/Hover)
- **Blockquote Colors**: Blockquote Left Border, Blockquote Background
- **Auto-Derived**: Modifying core color R/G/B channels automatically updates Secondary Background, Secondary Text, Inline Code Background, Code Block Background, Table Header Background, Blockquote Border, and Scrollbar colors

### Typography

| Property          | Default | Description |
| ----------------- | ------- | ----------- |
| `BodyFontSize`    | 14px    | — |
| `CodeFontSize`    | 13px    | — |
| `LineHeight`      | 1.6     | — |
| `BorderRadius`    | 6px     | Code block border radius |

### Mermaid Diagrams

| Property                         | Default | Description |
| -------------------------------- | ------- | ----------- |
| `MermaidTheme`                   | `dark`  | `dark` / `light` / `base` |
| `MermaidBgHex`                   | `#1E1E1E` | RGB three-channel slider |
| `MermaidContainerPadding`        | 8px     | — |
| `MermaidContainerMargin`         | 16px    | — |
| `MermaidBorderRadius`            | 4px     | — |

### PlantUML Diagrams

| Property                 | Default  | Description |
| ------------------------ | -------- | ----------- |
| `PumlBgHex`              | `#1E1E1E` | RGB three-channel slider |
| `PumlContainerPadding`   | 12px     | — |
| `PumlContainerMargin`    | 8px      | — |
| `PumlBorderRadius`       | 6px      | — |
| `PumlDarkInvert`         | 0.882    | CSS `filter: invert()` value in dark mode |

### highlight.js Code Highlighting Colors

Supports independent control of the following syntax highlighting color categories:

| Group                       | Properties (Examples)                 | Default     |
| --------------------------- | ------------------------------------- | ----------- |
| Keyword / Literal / Symbol  | `HljsKeyword` / `Literal` / `Symbol` / `Name` | `#569cd6` |
| Built-in / Type             | `HljsBuiltIn` / `Type`                | `#4ec9b0` |
| Class / Number              | `HljsClass` / `Number`                | `#b5cea8` |
| String / Meta-String        | `HljsString` / `MetaString`           | `#d69d85` |
| Title                       | `HljsTitle` / `TitleClass` / `TitleClassInherited` | `#DCDCAA` / `#4EC9B0` |
| Parameters / Variables      | `HljsParams` / `Variable` / `TemplateVariable` | `#9CDCFE` / `#bd63c5` |
| Comments / Quotes           | `HljsComment` / `Quote`               | `#6a9955` |
| Attributes / Tags / Meta    | `HljsAttr` / `Attribute` / `Meta` / `Tag` | `#9cdcfe` / `#9b9b9b` / `#569cd6` |
| Selectors                   | `HljsSelectorAttr` / `SelectorClass` / `SelectorId` etc. | `#d7ba7d` |
| Background / Foreground     | `HljsBackground` / `Foreground`       | `#1e1e1e` / `#dcdcdc` |

### Auto-Push

All property changes automatically mark dirty data; a 10Hz timer periodically calls `ApplyCustomCssAsync` to inject updates into the bound MarkdownView control (held via WeakReference, doesn't block GC).

## Rendering Capabilities

renderer.js loads third-party libraries via CDN and executes the full rendering pipeline inside the WebView:

- **Markdown** — markdown-it 14.1.0 + footnote 4.0.0 + task-lists 2.1.1 + strikethrough
- **Math Formulas** — KaTeX 0.16.11 (inline `$...$` / block `$$...$$`)
- **Code Highlighting** — highlight.js 11.10.0, VS Code-style color scheme (supports full token color customization via `ApplyCustomCssAsync` / Theme Editor)
- **Diagrams** — Mermaid 11.4.1 (flowcharts, sequence diagrams, pie charts, Git graphs, class diagrams, state diagrams)
- **PlantUML** — Encoded via `plantuml-encoder` 1.4.0 and rendered as SVG through the PlantUML online service, with dark/light theme adaptation (CSS `invert()` + `hue-rotate()`)
- **Video Embedding** — Supports direct video files (`.mp4` / `.webm` / `.ogg` / `.mov` / `.avi` / `.mkv`) and auto-detection of platform URLs
  - **YouTube** — `youtube.com/watch?v=ID` / `youtu.be/ID` → responsive iframe embedding
  - **Bilibili** — `bilibili.com/video/BVxxx` → responsive iframe embedding
  - **Vimeo** — `vimeo.com/ID` → responsive iframe embedding
- **Code Blocks** — Language labels · Copy button (with `navigator.clipboard` or `document.execCommand` fallback) · Height adjustment (+/- per-block independent control) · Configurable max height (`maxCodeBlockHeight`)
- **Task Lists** — Custom checkboxes
- **Footnotes / Tables / Blockquotes / Strikethrough**
- **External Links** — Auto-intercepted and opened in the system browser via C# bridge (`window.open` fallback for WASM environments)
- **Preview Configuration** — Dynamic adjustment via `setPreviewConfig` for font size (`fontSize`), line height (`lineHeight`), code language label display (`showCodeLanguage`), copy button toggle (`showCopyButton`), and code block max height (`maxCodeBlockHeight`)
- **Theme Editor** — Built-in `MarkdownThemeView` control providing RGB sliders for real-time customization of colors, typography, Mermaid/PlantUML styles, and code highlighting colors

### JS-Exposed Global Functions

| Function                                  | Description |
| ----------------------------------------- | ----------- |
| `window.renderMarkdown(text)`             | Renders Markdown text to the preview area |
| `window.onMarkdownUpdate(text)`           | Alias for `renderMarkdown` |
| `window.setPreviewConfig(config)`         | Updates preview configuration and re-renders |
| `window.setTheme(theme)`                  | Switches theme (`'light'` / `'dark'`), updates CSS class + Mermaid theme + re-renders |
| `window.setCustomCss(cssText)`            | Replaces `<style id="custom-theme-css">` content to override default styles |
| `window.showPreviewError(detail)`         | Displays a JS runtime error overlay |
| `window.dismissErrorOverlay()`            | Closes the JS runtime error overlay |
| `window.escapeHtml(s)`                    | HTML escape utility function |

### WebView Error Handling

- **C# Side**: `MarkdownView` includes a built-in bottom error panel (ErrorPanel) that automatically appears on errors, with retry and dismiss buttons
- **JS Side**: An `error-overlay` overlay (with close button) inside the WebView is automatically triggered by `window.onerror` and `unhandledrejection`
- All errors are simultaneously raised via the `ErrorOccurred` event for external subscription

## Cross-Platform Architecture

```
┌───────────────────────────────────────────────────────────┐
│                    MarkdownView Control                   │
│   (Avalonia UserControl + NativeWebView)                   │
├───────────┬───────────┬───────────┬───────────┬───────────┤
│  Desktop  │  Browser  │  Android  │    iOS    │  Future   │
│ WebView2  │   WASM    │ WebView   │ WKWebView │ Platforms │
│ http://   │ about:    │ http://   │ http://   │           │
│ 127.0.0.1 │ blank +   │ 127.0.0.1 │ 127.0.0.1 │           │
│ :dynport  │ doc.write │ :dynport  │ :dynport  │           │
└───────────┴───────────┴───────────┴───────────┴───────────┘
```

### Loading Strategy

All platforms use `EmbeddedHtmlSourceProvider` (implementing `IWebViewSourceProvider`) to read embedded resources at runtime and inline `renderer.css` / `renderer.js`, then follow different loading paths per platform:

- **Desktop (WebView2)** — Starts `LocalHtmlServer` (loopback `http://127.0.0.1:dynamic-port`) → navigates to that address
  - Uses `http://` instead of `file://` to avoid same-origin policy restrictions for third-party iframes (e.g., YouTube)
- **Android / iOS** — Same as Desktop: starts `LocalHtmlServer` → navigates to `http://127.0.0.1:dynamic-port`
- **Browser (WASM)** — `about:blank` → `document.write` injects full HTML (TCP services cannot be started within WASM sandbox)

> **Extension Point**: The `IWebViewSourceProvider` interface allows custom HTML content sources for injecting different page structures or CDN mirror addresses.

### Platform Readiness Signal Differences

| Platform  | Readiness Detection Mechanism |
| --------- | ----------------------------- |
| Desktop   | CDN scripts loaded by the time `NavigationCompleted` fires; calls `SetReady()` directly |
| Android   | `NavigationCompleted` fires before CDN scripts load → polls `typeof window.renderMarkdown === 'function'` (200ms interval, 15s timeout) |
| iOS       | Same as Android (same architecture) |
| Browser   | CDN scripts executed synchronously via `document.write` after `InjectViaDocumentWriteAsync` completes |

## Dependencies

| Component                                     | Purpose                   | Loading Method |
| --------------------------------------------- | ------------------------- | -------------- |
| Avalonia                                      | UI Framework              | NuGet          |
| Avalonia.Controls.WebView                     | NativeWebView Control     | NuGet          |
| renderer.js (inlined)                         | Renderer core logic       | Embedded resource (inlined at build time) |
| renderer.css (inlined)                        | Renderer styles           | Embedded resource (inlined at build time) |
| markdown-it 14.1.0 / footnote 4.0.0 / task-lists 2.1.1 | Markdown parsing          | CDN (loaded at runtime) |
| highlight.js 11.10.0                          | Code highlighting         | CDN (loaded at runtime) |
| KaTeX 0.16.11 / katex.min.css                 | Math formula rendering + CSS styles | CDN (loaded at runtime) |
| Mermaid 11.4.1                                | Diagram rendering         | CDN (loaded at runtime) |
| plantuml-encoder 1.4.0                        | PlantUML encoding         | CDN (loaded at runtime) |

> **Network Requirements**: Only `renderer.js` / `renderer.css` / `index.html` are embedded resources inlined at build time.
> CDN libraries (markdown-it and plugins, highlight.js, KaTeX with CSS, Mermaid, plantuml-encoder) **require runtime network loading**.
> If CDN is blocked by network or tracking protection, only the corresponding features are affected (e.g., math formulas or diagrams fail to render); basic Markdown preview remains functional.
> The control includes a 15-second JS readiness timeout detection; if exceeded, an error panel is displayed.

## Theme System

Supports automatic system theme following (Light / Dark), with all active instances (managed via a static WeakReference list) synchronizing reactively:

```csharp
// Auto-pushed on system theme change
// 1. C# side: WebViewHost / NativeWebView background color
// 2. JS side: setTheme('light'|'dark') toggles CSS class + Mermaid theme + re-renders
```

- On first `MarkdownView` instance creation, subscribes to the global `Application.Current.ActualThemeVariantChanged` event
- On theme change, iterates through the static instance list and calls `PushThemeToWebView` for each WebView
- Also updates the theme class in the HTML returned by `LocalHtmlServer` to ensure consistency on restart/refresh

Real-time color customization is available via the `MarkdownThemeView` control; modifications are automatically injected into the WebView through the `ApplyCustomCssAsync` interface. A 10Hz `DispatcherTimer` with dirty flag ensures efficient batching, avoiding excessive script calls during rapid slider dragging.

## License

MIT © Axvser