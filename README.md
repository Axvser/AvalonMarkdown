# AvalonMarkdown

A Markdown preview control built on **AvaloniaUI**'s **NativeWebView** — renders Markdown with math (KaTeX), code highlighting (highlight.js), diagrams (Mermaid / PlantUML), and video embeds.

> 🌏 [中文文档](README.zh.md)

## Installation

```bash
dotnet add package AvalonMarkdown
```

## MarkdownView

A drop-in Markdown preview control.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### Rendering content

Recommended — bind the `Text` property (updates are deduplicated automatically):

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

Or render explicitly once the control is ready:

```csharp
Preview.OnReady += async (_, _) =>
{
    await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
};
```

### Link navigation

Every hyperlink click is raised as `LinkClicked`. Set `Handled = true` to take over navigation (e.g. in-app routing); otherwise the link opens in the system browser by default.

```csharp
Preview.LinkClicked += (_, e) =>
{
    if (e.Url.StartsWith("app://"))
    {
        NavigateInternal(e.Url);
        e.Handled = true; // suppress the default system-browser launch
    }
};
```

### Preview configuration

Content updates auto-scroll to the newest content, pausing while you operate the scrollbar. Tune via `ApplyConfigAsync`:

```csharp
await Preview.ApplyConfigAsync("setPreviewConfig({ autoScrollOnUpdate: true, autoScrollCooldown: 2000 })");
```

### Key API

| Member | Type | Description |
| --- | --- | --- |
| `Text` | `string?` | Markdown content (two-way bindable) |
| `OnReady` | `event` | Fires when the preview is ready to render |
| `LinkClicked` | `event` | Fires on every hyperlink click; `Handled = true` intercepts navigation |
| `ErrorOccurred` | `event` | Fires on internal recoverable errors |
| `RenderMarkdownAsync(text)` | `Task` | Renders Markdown content |
| `RestartPreviewAsync()` | `Task` | Restarts the preview |
| `ApplyConfigAsync(js)` | `Task` | Applies preview configuration |
| `ApplyCustomCssAsync(css)` | `Task` | Injects custom CSS overrides |
| `InvokeScriptAsync(js)` | `Task<string?>` | Executes custom JavaScript |
| `ScrollToProgressAsync(percent)` | `Task` | Scrolls the preview to a vertical progress position (0–100) |
| `GetScrollProgressAsync()` | `Task<double?>` | Returns the current vertical scroll progress (0–100) |
| `NavigateToHeadingAsync(heading)` | `Task<bool>` | Scrolls to the matching heading (for TOC); returns whether found |
| `GetHeadingsAsync()` | `Task<IReadOnlyList<MarkdownHeading>>` | Lists all headings (text + level) to build a table of contents |

## MarkdownThemeView

A built-in theme editor that targets a `MarkdownView`. RGB sliders for colors, plus controls for typography, code highlighting, and Mermaid/PlantUML styles — changes auto-apply.

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

Collapsed by default; click the title bar to expand.

## License

[MIT](LICENSE.txt)
