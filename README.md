# AvalonMarkdown

A Markdown preview control for **AvaloniaUI** — renders Markdown with math (KaTeX), syntax highlighting (highlight.js), diagrams (Mermaid / PlantUML), function plots, video embeds, and collapsible chat / status blocks.

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

`width` and `height` are the **whole SVG including margins** (left 40 / right 20 / bottom 20, top 20 — or 40 when there is a `title`), so `"height": 350` yields a 310px plot area. If you omit `width`, it is fitted to the container. If you omit `height`, it is 350.

**Grid cells stay square.** function-plot scales x and y independently, so a plot area whose aspect differs from the domain aspect would draw rectangular cells. For linear axes the renderer expands whichever domain needs it, keeping the requested range visible and the chart filling its content area — the extra range simply shows more of the graph — and gives both axes the same tick step, so adjacent grid lines are the same pixel distance apart. Log axes are left alone.

A malformed body renders an inline error block instead of breaking the preview. The fence is matched on its info string exactly, so ordinary code fences — `json` included — are untouched.

Expression strings are checked against a character whitelist before they reach the library's evaluator (which compiles them to JavaScript). Comparison, ternary and modulo operators are allowed, so piecewise definitions work — `"fn": "x > 0 ? x : -x"` — but quotes, semicolons, braces, brackets and backslashes are rejected.

Two things to know about the expression syntax, both straight from the library:

- **Constants are upper-case** — `PI` and `E`, not `pi` / `e`. Lower-case forms are undefined and the series silently fails to draw.
- Available functions include `sin` `cos` `tan` `asin` `acos` `atan` `sqrt` `abs` `exp` `log` `ln` `max` `min`. Names outside that set (`floor`, `sign`, …) are undefined.

### Chat / collapsible blocks

A `chat` fence renders a collapsed `<details>` panel — useful for streaming agent output where "思考中" or "工具调用中" is a step you can expand:

````markdown
```chat[思考中]
正在检查 `renderer.js` 的分派规则…
```

```chat[工具调用中][open]
运行 `dotnet build`：

- `--configuration Release`
```
````

The bracketed label is free-form and displayed as written. A second bracket group configures the panel — `open` (or `expanded`) starts it expanded, `collapsed` keeps it shut (the default), and `static` / `idle` freezes the status halo (`spin` is the default). The body is ordinary Markdown, rendered recursively, so inline code, lists, tables and nested fences work inside it. A label cannot contain `]`; a malformed info string or an unknown option renders a visible error block rather than silently becoming a code block.

The top-right status badge picks its icon from the step state: **thinking → lightbulb**, **tool → wrench**, **done → check**, **error → cross**, otherwise a chat bubble. The state is inferred from the label (`思考` / `think`, `工具` / `tool`, `完成` / `done`, `错误` / `fail`) or can be set explicitly with `[thinking]`, `[tool]`, `[done]`, `[error]` or `[chat]`. The halo spins while the step is live; `done` / `error` hide the outer ring entirely. `[static]` freezes the halo on non-terminal states, and `[spin]` forces it to keep animating. A custom icon can be supplied with `svg="M12 2 L22 12 L12 22 L2 12 Z"` — quote the value when the path contains commas — or with a full `<svg>…</svg>` string; path data is stroked with the badge colour, while a full SVG may set its own fill/stroke. The colour and glow are configurable through the `Chat*` properties on `MarkdownThemeViewModel`.

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
| Typography | Code font size; code-block / pre corner radius; h1–h3 top/bottom margins |
| Code highlighting | Color of each syntax token (keyword, string, number, comment, …) |
| Diagrams | Mermaid & PlantUML container background / padding / margin / radius; PlantUML dark invert strength |
| Function plots | Plot container background / padding / margin / radius; axis, grid and title colour |
| Chat / status blocks | Chat background / body padding / margin / right-side radius (the left edge is square and borderless); status badge colour and glow |

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
| `HeadingMarginTop` · `HeadingMarginBottom` | h1–h3 top / bottom margin (px) |
| `MermaidBgR` `MermaidBgG` `MermaidBgB` | Mermaid container background (0–255) |
| `MermaidContainerPadding` · `MermaidContainerMargin` · `MermaidBorderRadius` | Mermaid container padding / margin / radius |
| `PumlBgR` `PumlBgG` `PumlBgB` | PlantUML container background (0–255) |
| `PumlContainerPadding` · `PumlContainerMargin` · `PumlBorderRadius` | PlantUML container padding / margin / radius |
| `PumlDarkInvert` | PlantUML dark invert strength (0–1) |
| `PlotBgR` `PlotBgG` `PlotBgB` | Function-plot container background (0–255) |
| `PlotContainerPadding` · `PlotContainerMargin` · `PlotBorderRadius` | Function-plot container padding / margin / radius |
| `PlotAxisR` · `PlotAxisG` · `PlotAxisB` | Function-plot axis / grid / title colour channels (0–255); `PlotAxisColor` is the read-only `#RRGGBB` preview |
| `ChatBgR` `ChatBgG` `ChatBgB` | Chat block background (0–255) |
| `ChatContainerPadding` · `ChatContainerMargin` · `ChatBorderRadius` | Chat body padding / container margin / right-side radius (the left edge is square and borderless) |
| `ChatAccentR/G/B/A` · `ChatGlassR/G/B/A` | Status-badge and glow RGBA channels (0–255, alpha included); `ChatAccentColor` / `ChatGlassColor` are read-only `rgba()` previews |
| `ChatGlassBlur` | Status-badge glow blur radius (px) |

**Code highlighting** — highlight.js token colors (`string`, e.g. `"#569cd6"`):

`HljsKeyword`, `HljsLiteral`, `HljsSymbol`, `HljsName`, `HljsBuiltIn`, `HljsType`, `HljsClass`, `HljsNumber`, `HljsString`, `HljsMetaString`, `HljsTitle`, `HljsTitleClass`, `HljsTitleClassInherited`, `HljsParams`, `HljsVariable`, `HljsTemplateVariable`, `HljsComment`, `HljsQuote`, `HljsAttr`, `HljsAttribute`, `HljsMeta`, `HljsTag`, `HljsSelectorAttr`, `HljsSelectorClass`, `HljsSelectorId`, `HljsSelectorPseudo`, `HljsSelectorTag`, `HljsBullet`, `HljsSection`, `HljsLink`, `HljsRegexp`, `HljsTemplateTag`, `HljsDoctag`, `HljsBackground`, `HljsForeground` — plus diff markers `HljsAdditionBg`/`HljsAdditionColor` and `HljsDeletionBg`/`HljsDeletionColor`.

> The built-in `MarkdownThemeView` panel covers colors, typography, diagrams, plots and the chat background/padding/radius/glow, with sliders for the plot axis and the badge/glow RGBA channels; only the code-highlighting tokens (`Hljs*`) are configured in code (or bound in your own UI).

---

## Working on the renderer

Verification is an offline C# unit suite plus four environment-gated drivers in
the demo app. The drivers each exit the process — enable **exactly one** (a guard
fails fast if more than one is set):

| Command | Checks |
| --- | --- |
| `dotnet run --project AvalonMarkdown.Test.Unit` | pure C#: Myers diff, JavaScript-literal encoding, export-name sanitising, theme CSS under hostile culture / out-of-range values, and the local HTTP server under malformed, oversized, concurrent and abandoned clients. Offline, no window. |
| `AVALON_THEME_AUDIT=1 dotnet run --project AvalonMarkdown.Test.Desktop` | every `MarkdownThemeViewModel` property reaches the generated CSS, and every generated rule has a live consumer |
| `AVALON_CAPTURE_ERRORS=1 dotnet run --project AvalonMarkdown.Test.Desktop` | drives the app through its phases; asserts fence dispatch and that the showcase assets load |
| `AVALON_RENDER_CHECK=1 AVALON_RENDER_CHECK_FILE=<abs path> dotnet run --project AvalonMarkdown.Test.Desktop` | renders one file through the real pipeline and DOM-audits it; normally driven by `render_check.py` |
| `AVALON_FAULT_CHECK=1 dotnet run --project AvalonMarkdown.Test.Desktop` | fault injection: junk configs, malformed fences, pathological Markdown, each optional CDN library removed in turn, hanging export paths, and subscribers that throw. Writes `fault-check.log` / `fault-check.json`. |

Run without any driver for an ordinary visual check.

### Fault tolerance

The preview is built to degrade visibly, never silently:

- **markdown-it missing** — a plain-text renderer is installed and `READY` is
  posted immediately, so the control never waits out its 15-second timeout.
- **optional libraries missing** (mermaid, KaTeX, function-plot, plantuml-encoder,
  the footnote / task-list plugins) — each affected fence renders a local
  explanation and the rest of the document still renders.
- **one bad fence or image token** — the fence/image renderer rules are
  isolated; the remaining Markdown renders and the failing member shows its
  source plus the error.
- **bad config, script or CSS** — values are clamped and coerced; an invalid
  script or stylesheet cannot leave the preview blank.
- **exports** — every rasterise/fetch path has a timeout and a single-shot
  callback; a failed export reports a toast instead of disabling the menu
  forever.
- **event boundaries** — async-void handlers and every public event subscriber
  (`OnReady`, `ErrorOccurred`, `ConsoleMessage`, `LinkClicked`) are guarded, so a
  throwing subscriber cannot terminate the host.
- **renders** — requests are serialised and stale ones dropped; a failed render
  leaves its text retryable rather than recording it as rendered.
- **local HTTP server** — bounded request headers, a connection cap, a
  per-client deadline, GET/HEAD only, and idempotent disposal that releases the
  port before a restart binds a new one.
- **restart / re-attach** — a restart re-renders the current text instead of
  leaving the pane blank, and a control removed from and re-added to the visual
  tree re-initialises itself.

## Authoring reference & validator

[`skills/avalonmarkdown-syntax/`](skills/avalonmarkdown-syntax/) documents the exact
Markdown dialect this control renders — every member with worked good/bad examples,
including the delimiter rules for math and the expression grammar for function plots.

[`references/templates.md`](skills/avalonmarkdown-syntax/references/templates.md) holds
copy-paste-ready source for each member. Those snippets are not written from memory:
`scripts/check_templates.py` extracts every one of them, renders the result through the
real pipeline, and fails if anything breaks.

### Installing it as a Claude Code skill

The folder is a valid Claude Code skill, so it works as-is for **your** projects —
if you are generating Markdown that this control will render, install it into the
project where the generation happens:

```bash
mkdir -p .claude/skills
cp -r <path-to>/AvalonMarkdown/skills/avalonmarkdown-syntax .claude/skills/
```

Claude Code then loads it automatically and consults it before writing Markdown.
It is deliberately **not** installed into this repository's own `.claude/` — a
skill for authoring documents is not something the repository itself needs, and
committing an agent instruction file here would push it on everyone who clones.

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

Its toolbar carries three **special-member showcases** alongside the general tour —
`Tables`, `Diagrams` and `Plots`. Each isolates one kind of element and runs a large
set of cases at it, so a regression in that member is obvious rather than buried:

- **Tables** — alignment, escaped pipes inside code spans, math and footnotes in
  cells, CJK and emoji, an eight-column layout, plus three shapes that must *not*
  become tables.
- **Diagrams** — 15 Mermaid types and 11 PlantUML types, with CJK and HTML labels,
  alongside a deliberately broken diagram of each kind so the failure appearance is
  documented next to the successes.
- **Plots** — the full Penner/easings.net set (sine through bounce, `in`/`out`/`inOut`)
  plus non-`y = f(x)` graph types: parametric, polar, implicit, scatter, vectors.

They live as real `.md` files under `AvalonMarkdown.Test.Shared/Assets/Showcase/`
rather than C# string literals, so the validator checks the exact file the demo
renders. The capture driver asserts all three load, since a wrong asset key fails
silently into a fallback error block.

## License

[MIT](LICENSE.txt)
