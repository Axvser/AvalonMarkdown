# AvalonMarkdown

A Markdown preview control for **AvaloniaUI** — renders Markdown with math (KaTeX), syntax highlighting (highlight.js), diagrams (Mermaid / PlantUML), function plots, and video embeds.

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

### Function plots

A `plot` fence draws `y = f(x)`. Its body is the [function-plot](https://mauriciopoppe.github.io/function-plot/) options object as **raw JSON** — pass through anything the library documents:

````markdown
```plot
{
  "title": "sin(x)/x",
  "grid": true,
  "xAxis": { "domain": [-12, 12] },
  "yAxis": { "domain": [-0.4, 1.1] },
  "data": [
    { "fn": "sin(x)/x" },
    { "fn": "1/x", "color": "#e06c75" }
  ]
}
```
````

Only two things are ever defaulted, and your JSON always wins:

- **`color`** — a series without one takes a colour from the theme palette, by index. Series colours are per-function (`color` is a per-datum option), so they cannot come from CSS.
- **`disableZoom`** — defaults to `true`, keeping the graph static so the wheel keeps scrolling the page. Set `"disableZoom": false` to enable wheel zoom and panning on that block. This default is deliberate: the library's zoom binding consumes every wheel event over the graph, which traps the scroll wheel in a reading pane.

`width` and `height` are the **whole SVG including margins** (left 40 / right 20 / bottom 20, top 20 — or 40 when there is a `title`), so `"height": 350` yields a 310px plot area. If you omit `width`, it is fitted to the container.

A malformed body renders an inline error block instead of breaking the preview. The fence is matched on its info string exactly, so ordinary code fences — `json` included — are untouched.

Expression strings are checked against a character whitelist before they reach the library's evaluator (which compiles them to JavaScript). Comparison, ternary and modulo operators are allowed, so piecewise definitions work — `"fn": "x > 0 ? x : -x"` — but quotes, semicolons, braces, brackets and backslashes are rejected.

Two things to know about the expression syntax, both straight from the library:

- **Constants are upper-case** — `PI` and `E`, not `pi` / `e`. Lower-case forms are undefined and the series silently fails to draw.
- Available functions include `sin` `cos` `tan` `asin` `acos` `atan` `sqrt` `abs` `exp` `log` `ln` `max` `min`. Names outside that set (`floor`, `sign`, …) are undefined.

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

### Export a diagram as an image

Right-click a Mermaid diagram, a function plot or a PlantUML image — or long-press
it on touch — and choose **Save as SVG** or **Save as PNG**. The file is written
through the platform's save dialog.

Only those three members are intercepted. Right-clicking anywhere else in the
document still shows the platform's own menu. Set `EnableContextMenu="False"` to
turn the feature off entirely and get the platform menu back everywhere:

```xml
<md:MarkdownView Text="{Binding MarkdownContent}" EnableContextMenu="False" />
```

Math is deliberately not exportable: KaTeX renders positioned HTML, not SVG, so
there is no vector artefact to hand out — producing an image would mean
screenshotting the DOM.

Three things worth knowing:

- **An exported PlantUML image is black-on-white**, even in dark theme. That is
  PlantUML's own output, deliberately unfiltered — the dark-theme inversion you see
  on screen is a display-only filter. It prints and pastes correctly, but it will
  look different from a dark-theme Mermaid export.
- **Exported Mermaid and plot images carry an opaque background** taken from the
  page, so a dark-theme diagram stays legible when pasted onto a white page.
- **A very large diagram may refuse to save as PNG** (reported in the preview) —
  the JS→C# bridge crosses Android's Binder with a ~1 MB limit. SVG has no such
  limit; use it, or export a smaller diagram.

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
| `EnableContextMenu` | `bool` | Right-click / long-press an image to save it (default `true`) |
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
| Function plots | Plot container background / padding / margin / radius; axis, grid and title colour |

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
| `PlotBgR` `PlotBgG` `PlotBgB` | Function-plot container background (0–255) |
| `PlotContainerPadding` · `PlotContainerMargin` · `PlotBorderRadius` | Function-plot container padding / margin / radius |
| `PlotAxisColor` | Function-plot axis / grid / title colour (CSS colour `string`, code-only) |

**Code highlighting** — highlight.js token colors (`string`, e.g. `"#569cd6"`):

`HljsKeyword`, `HljsLiteral`, `HljsSymbol`, `HljsName`, `HljsBuiltIn`, `HljsType`, `HljsClass`, `HljsNumber`, `HljsString`, `HljsMetaString`, `HljsTitle`, `HljsTitleClass`, `HljsTitleClassInherited`, `HljsParams`, `HljsVariable`, `HljsTemplateVariable`, `HljsComment`, `HljsQuote`, `HljsAttr`, `HljsAttribute`, `HljsMeta`, `HljsTag`, `HljsSelectorAttr`, `HljsSelectorClass`, `HljsSelectorId`, `HljsSelectorPseudo`, `HljsSelectorTag`, `HljsBullet`, `HljsSection`, `HljsLink`, `HljsRegexp`, `HljsTemplateTag`, `HljsDoctag`, `HljsBackground`, `HljsForeground` — plus diff markers `HljsAdditionBg`/`HljsAdditionColor` and `HljsDeletionBg`/`HljsDeletionColor`.

> The built-in `MarkdownThemeView` panel covers colors, typography, and diagrams; code-highlighting tokens (`Hljs*`) and `PlotAxisColor` are configured in code (or bound in your own UI).

---

## Authoring reference & validator

[`skills/avalonmarkdown-syntax/`](skills/avalonmarkdown-syntax/) documents the exact
Markdown dialect this control renders — every member with worked good/bad examples,
including the delimiter rules for math and the expression grammar for function plots.

It ships a validator that answers a question the preview cannot: *will this actually
render?* Most failures here are silent — KaTeX draws its own error span without
logging, a `$$` block left open deletes the rest of the document, and a PlantUML
syntax error comes back as a **renderable error picture** that displays at full size
as if it were fine.

```bash
python skills/avalonmarkdown-syntax/scripts/render_check.py doc.md
```

Two tiers. A static pass re-derives the renderer's own dispatch rules offline and
instantly. With a build present it also renders the file through the real WebView
pipeline and inspects the DOM, which is the only way to know that a formula compiled
or a diagram parsed.

```bash
python skills/avalonmarkdown-syntax/scripts/render_check.py --self-test
```

`--self-test` asserts every check fires against the known-bad fixtures in
`skills/avalonmarkdown-syntax/fixtures/` — a validator with no known-bad sample
cannot be trusted, so the fixtures and the assertions are part of the deliverable.

## Demo

The repository contains runnable sample apps (`AvalonMarkdown.Test.Desktop` and Android/iOS/Browser variants) that exercise both levels.

## License

[MIT](LICENSE.txt)
