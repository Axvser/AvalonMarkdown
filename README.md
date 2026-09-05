# AvalonMarkdown

A Markdown preview control for **AvaloniaUI** — renders Markdown with math (KaTeX), syntax highlighting (highlight.js), diagrams (Mermaid / PlantUML), and video embeds.

> 🌏 [中文文档](README.zh.md)

## Installation

```bash
dotnet add package AvalonMarkdown
```

Everything you need lives on two types:

- **`MarkdownView`** — show a Markdown document and react to the user (links, scroll, headings).
- **`MarkdownThemeViewModel`** — style how that document looks.

---

## MarkdownView — rendering & interaction

Add the control to a window and feed it Markdown content.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:md="clr-namespace:AvalonMarkdown.Views;assembly=AvalonMarkdown">
    <md:MarkdownView x:Name="Preview" />
</Window>
```

### Show content

Bind the `Text` property (this is the recommended way — it updates automatically):

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" />
```

Or render explicitly after the control is ready:

```csharp
Preview.OnReady += async (_, _) =>
{
    await Preview.RenderMarkdownAsync("# Hello World\n\n**Bold** *Italic*");
};
```

When content is empty you can show a hint with `Placeholder` (blank by default):

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" Placeholder="Type something…" />
```

### Handle link clicks

Every hyperlink click fires `LinkClicked`. Set `Handled = true` to take over navigation yourself; otherwise the link opens in the system browser.

```csharp
Preview.LinkClicked += (_, e) =>
{
    if (e.Url.StartsWith("app://"))
    {
        OpenInternalPage(e.Url);   // your in-app routing
        e.Handled = true;          // suppress the default browser launch
    }
};
```

### Jump inside the document (anchors & TOC)

Markdown headings are linkable from within the same page, so `[See §2](#2-text-formatting)`-style links just work.

To build a clickable table of contents in your own UI:

```csharp
var headings = await Preview.GetHeadingsAsync();          // [{ Text, Level }, …]
var ok       = await Preview.NavigateToHeadingAsync("Chapter 2"); // scroll to a heading
```

### Sync an editor with the preview (scroll progress)

Report/restore the vertical read position — handy for editor ⇄ preview split views:

```csharp
double? progress = await Preview.GetScrollProgressAsync(); // 0–100
await Preview.ScrollToProgressAsync(50);
```

### Configuration & diagnostics

Content updates auto-scroll to the newest content, and pause while you operate the scrollbar. Behaviour can be tuned with `ApplyConfigAsync`:

```csharp
await Preview.ApplyConfigAsync("setPreviewConfig({ autoScrollOnUpdate: true, autoScrollCooldown: 2000 })");
```

`ErrorOccurred` and `ConsoleMessage` events let you surface or log any rendering/script message.

### MarkdownView reference

| Member | Type | What it does |
| --- | --- | --- |
| `Text` | `string?` | Markdown content (two-way bindable) |
| `Placeholder` | `string?` | Hint shown when content is empty |
| `OnReady` | `event` | Fires when the preview is ready to render |
| `LinkClicked` | `event` | Fires on hyperlink clicks; `Handled = true` takes over navigation |
| `ErrorOccurred` | `event` | Fires on recoverable internal errors |
| `ConsoleMessage` | `event` | Fires for each WebView console message (log/warn/error) |
| `RenderMarkdownAsync(text)` | `Task` | Renders Markdown content |
| `GetHeadingsAsync()` | `Task<IReadOnlyList<MarkdownHeading>>` | Lists all headings (`Text`, `Level`) |
| `NavigateToHeadingAsync(heading)` | `Task<bool>` | Scrolls to the heading; returns whether it was found |
| `GetScrollProgressAsync()` | `Task<double?>` | Current vertical progress (0–100) |
| `ScrollToProgressAsync(percent)` | `Task` | Scrolls to a vertical progress position (0–100) |
| `ApplyConfigAsync(expression)` | `Task` | Applies preview behaviour configuration |
| `ApplyCustomCssAsync(css)` | `Task` | Applies a full custom stylesheet |
| `RestartPreviewAsync()` | `Task` | Recreates the preview page |

---

## MarkdownThemeViewModel — styling

Configures how the document looks: colors, typography, code highlighting, and diagram styling. Every property carries a `[Description]` (hover it in IntelliSense to see what it controls).

### Option A — use the built-in theme editor

`MarkdownThemeView` is a ready-made editor panel for the properties below. Point it at your preview; any change is applied immediately.

```xml
<md:MarkdownView x:Name="Preview" />
<md:MarkdownThemeView Target="{Binding #Preview}" />
```

The panel is collapsed by default — click its header to expand it.

### Option B — drive it programmatically

Create a `MarkdownThemeViewModel`, change the properties you care about, and let it push changes to one or more previews:

```csharp
var theme = new MarkdownThemeViewModel
{
    BgR = 30, BgG = 30, BgB = 30,   // page background
    TextR = 212, TextG = 212, TextB = 212, // body text
    HljsKeyword = "#569cd6",
    MermaidContainerPadding = 8,
};

theme.RegisterRenderer(myPreview);
theme.StartAutoApply();   // any further property change auto-applies
```

Prefer a one-shot apply instead?

```csharp
await myPreview.ApplyCustomCssAsync(theme.GenerateCss());
```

> The same styling applies whatever light/dark theme your app currently uses.

### What you can configure

| Group | Covers |
| --- | --- |
| Core colors | Page background, text, links, headings, inline code, borders |
| Surface colors | Secondary background (e.g. code headers), secondary text, inline-code background, code-block background, table header |
| Typography | Code font size; code-block / pre corner radius |
| Code highlighting | Color of each syntax token (keyword, string, number, comment, …) |
| Diagrams | Mermaid & PlantUML container background / padding / margin / radius; PlantUML dark invert strength |

### Supported style properties

**Colors** — each surface is three `int` channels `R` / `G` / `B` (0–255); each `XxxHex` is a read-only `#RRGGBB` preview.

| Surface | Channel properties | Preview |
| --- | --- | --- |
| Background | `BgR` `BgG` `BgB` | `BgHex` |
| Body text | `TextR` `TextG` `TextB` | `TextHex` |
| Link | `LinkR` `LinkG` `LinkB` | `LinkHex` |
| Heading | `HeadingR` `HeadingG` `HeadingB` | `HeadingHex` |
| Inline code text | `CodeR` `CodeG` `CodeB` | `CodeHex` |
| Border | `BorderR` `BorderG` `BorderB` | `BorderHex` |
| Secondary background | `BgSecR` `BgSecG` `BgSecB` | `BgSecHex` |
| Secondary text | `TextSecR` `TextSecG` `TextSecB` | `TextSecHex` |
| Inline-code background | `CodeBgR` `CodeBgG` `CodeBgB` | `CodeBgHex` |
| Code block background | `PreBgR` `PreBgG` `PreBgB` | `PreBgHex` |
| Table header background | `TableBgR` `TableBgG` `TableBgB` | `TableBgHex` |

**Typography & layout** (`double` / `int`, in px unless noted)

| Property | Meaning |
| --- | --- |
| `CodeFontSize` | Code font size |
| `BorderRadius` | Code-block / pre corner radius |
| `MermaidBgR` `MermaidBgG` `MermaidBgB` | Mermaid container background (0–255) |
| `MermaidContainerPadding` · `MermaidContainerMargin` · `MermaidBorderRadius` | Mermaid container padding / margin / radius |
| `PumlBgR` `PumlBgG` `PumlBgB` | PlantUML container background (0–255) |
| `PumlContainerPadding` · `PumlContainerMargin` · `PumlBorderRadius` | PlantUML container padding / margin / radius |
| `PumlDarkInvert` | PlantUML dark invert strength (0–1) |

**Code highlighting** — highlight.js token colors (`string`, e.g. `"#569cd6"`):

`HljsKeyword`, `HljsLiteral`, `HljsSymbol`, `HljsName`, `HljsBuiltIn`, `HljsType`, `HljsClass`, `HljsNumber`, `HljsString`, `HljsMetaString`, `HljsTitle`, `HljsTitleClass`, `HljsTitleClassInherited`, `HljsParams`, `HljsVariable`, `HljsTemplateVariable`, `HljsComment`, `HljsQuote`, `HljsAttr`, `HljsAttribute`, `HljsMeta`, `HljsTag`, `HljsSelectorAttr`, `HljsSelectorClass`, `HljsSelectorId`, `HljsSelectorPseudo`, `HljsSelectorTag`, `HljsBullet`, `HljsSection`, `HljsLink`, `HljsRegexp`, `HljsTemplateTag`, `HljsDoctag`, `HljsBackground`, `HljsForeground` — plus diff markers `HljsAdditionBg`/`HljsAdditionColor` and `HljsDeletionBg`/`HljsDeletionColor`.

> The built-in `MarkdownThemeView` panel covers colors, typography, and diagrams; code-highlighting tokens are configured in code via the `Hljs*` properties above (or bound in your own UI).

---

## Demo

The repository contains runnable sample apps (`AvalonMarkdown.Test.Desktop` and Android/iOS/Browser variants) that exercise both levels.

## License

[MIT](LICENSE.txt)
