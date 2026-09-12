# Member reference

Every construct the renderer supports, with the markup it produces. Loaded on
demand from [SKILL.md](../SKILL.md) — that file carries the rules you need while
writing; this one is the lookup table.

Two companions: [templates.md](templates.md) has copy-paste-ready source for each
member, verified by rendering; and the demo's showcase pages under
`AvalonMarkdown.Test.Shared/Assets/Showcase/` are a much larger corpus of the same
syntax, validated on every run.

The renderer is markdown-it 14 with `html: true`, `linkify: true`,
`typographer: true`, plus markdown-it-footnote and markdown-it-task-lists. Five
renderer rules are overridden: heading ids, inline math, block math, fences and
images.

## Text

| Write | Produces | Notes |
| --- | --- | --- |
| `**bold**` / `__bold__` | `<strong>` | |
| `*italic*` / `_italic_` | `<em>` | |
| `~~struck~~` | `<s>` | |
| `` `code` `` | `<code>` | background applied only inside `p > code` and `li > code` |
| `<u>underline</u>` | `<u>` | raw HTML; there is no Markdown underline |
| `---` / `***` / `___` | `<hr>` | |

Not supported: `==highlight==`, sub/superscript, definition lists, emoji
shortcodes (emoji pass through as literal characters), automatic table of
contents. Typographic quotes, dashes and ellipses are applied by `typographer`.

## Headings and anchors

`#` through `######`. Headings `h1` and `h2` carry a bottom border.

Every heading with non-empty text gets a generated `id`:

1. Take the rendered plain text (markup stripped, entities decoded).
2. Lower-case and trim.
3. Remove everything that is not a Unicode letter, Unicode number, `_`, whitespace
   or `-`.
4. Collapse runs of whitespace and `_` to a single `-`, collapse repeated `-`, and
   trim leading/trailing `-`.
5. If the result is empty, use `section`.

Duplicates are suffixed in document order: the second `## Setup` becomes
`setup-1`, the third `setup-2`.

A heading whose text renders to nothing gets **no** `id`, so anchor links to it
cannot resolve.

```markdown
## 2. Text formatting      -> #2-text-formatting
## 中文标题                -> #中文标题
## Setup  /  ## Setup     -> #setup and #setup-1
```

## Links

| Form | Behaviour |
| --- | --- |
| `[text](https://…)` | handed to the host app via `LinkClicked`; opens a browser only if unhandled |
| `[text](#slug)` | native in-page jump, never intercepted |
| `<https://…>` and bare URLs | auto-linked (`linkify`) |
| `[text](app://route)` | handed to the host app as-is — this is how in-app routing is done |

Intercepted links get `data-href` with the original URL, `href` rewritten to
`javascript:void(0)`, and `rel="noopener noreferrer"`.

A hand-written target works: `<a id="target"></a>` then `[go](#target)`.

## Lists

| Write | Notes |
| --- | --- |
| `- item`, `* item`, `+ item` | `<ul>` |
| `1. item` | `<ol>` |
| indentation | nesting |
| `- [x] done` / `- [ ] todo` | task list items |

Task list items get `class="task-list-item"`, their list gets
`class="contains-task-list"`, and the checkbox is rendered **disabled** — it
reflects state rather than capturing input.

## Tables

GFM pipe tables. The header row is required; the delimiter row may use `---`,
`:--` (left), `--:` (right) or `:-:` (centre). Even rows are striped.

```markdown
| Column | Meaning |
| --- | --- |
| `alpha` | first   |
```

## Blockquotes

`>` at the start of a line; nests with `>>`. Styled with a left border and a
background.

## Math

See [SKILL.md](../SKILL.md#math-katex) for the delimiter rules — they have real
edge cases. Summary of the produced markup:

| Write | Produces |
| --- | --- |
| `$…$` | inline `.katex` span |
| `$$` on its own line, … , `$$` on its own line | `<p class="katex-block">` |

Inline math containing `\begin{align}`, `\begin{equation}`, `\begin{gather}`,
`\begin{cd}` or `\begin{alignat}` is promoted to display mode automatically.

Block math does **not** have to be on its own lines — the content between the
opening and closing `$$` is taken verbatim, and blank lines inside it are allowed.
What ends the block is the closing `$$`, end of file, or a dedent.

A failed expression produces `<span class="katex-error">`; the raw LaTeX is kept in
the element's `title` attribute. The error colour comes from KaTeX itself, so it is
not themeable.

## Code fences

````markdown
```csharp
public class Hello { }
```
````

Produces, in this order of nesting:

```
<pre><code class="language-csharp">
  <div class="code-block-wrapper">
    <div class="code-header">
      <span class="code-lang">csharp</span>
      <span class="code-actions">
        <button class="copy-btn">Copy</button>
        <button class="resize-btn">−</button>
        <button class="resize-btn">+</button>
      </span>
    </div>
    <pre class="hljs"><code>…highlighted spans…</code></pre>
  </div>
</code></pre>
```

The outer `<pre>` is stripped of its own border and background by a
`:has(.code-block-wrapper)` rule. The `.code-lang` label is omitted when the fence
has no language, but the header (and the height buttons) are always present.

Highlighting happens only when highlight.js knows the language; otherwise the body
is escaped as plain text. An unrecognised language is not an error.

The `+` / `−` buttons change that block's `max-height` in 80px steps and record it
in a `data-height` attribute. The initial cap is a preview configuration value.

## Special fences

The info string is compared exactly after `trim().toLowerCase()`. Nothing else is
intercepted — in particular a `json` fence stays a highlighted code block.

| Info string | Produces |
| --- | --- |
| `mermaid` | `<div class="mermaid-container"><pre class="mermaid">…</pre></div>`, replaced by an SVG once mermaid runs |
| `plantuml` / `puml` | `<div class="puml-container puml-light\|puml-dark"><img src="https://www.plantuml.com/plantuml/svg/…"></div>` |
| `plot` | `<div class="plot-container" data-plot="…escaped JSON…"></div>`, hydrated into `<svg class="function-plot">` |
| `video` | a platform facade or a `<video>` element |

`puml-dark` adds `filter: invert(0.882) hue-rotate(180deg)` to the image so a
white-background diagram matches the dark theme.

**Graph types that need `"sampler": "builtIn"`.** `plot` defaults to the `interval`
sampler, which only understands plain `y = f(x)`. These throw
`X is not supported in the 'interval' sampler` unless the sampler is switched:

| Datum | Needs |
| --- | --- |
| `fnType: "points"` | `"sampler": "builtIn"` |
| `fnType: "parametric"` | `"sampler": "builtIn"` |
| `fnType: "polar"` | `"sampler": "builtIn"` |
| `fnType: "vector"` | `"sampler": "builtIn"` |
| `graphType: "scatter"` | `"sampler": "builtIn"` |

**The parameter name differs per graph type** — a genuine trap, since a wrong name
reports only `symbol "…" is undefined`:

| `fnType` | Parameter | Example |
| --- | --- | --- |
| linear (default) | `x` | `"fn": "sin(x)"` |
| `parametric` | `t` | `"x": "sin(3*t)", "y": "sin(4*t)"` |
| `polar` | **`theta`** | `"r": "1 - cos(theta)"` |

`polar` is the odd one out: its evaluator scope is `{theta: <angle>}` — verified in
the library bundle (`builtIn(t.d, "r", {theta: i})`), not inferred.

**Exportable members.** `mermaid`, `plot` and `plantuml` can be saved as an image
(right-click, or long-press on touch). Math cannot — KaTeX renders positioned HTML
rather than SVG. Two consequences for authors who care about the exported file:

- An exported PlantUML diagram is **black-on-white regardless of theme**, because
  the dark-theme inversion is a display-only filter and the export is deliberately
  the unfiltered original.
- Mermaid and plot exports carry an opaque page-derived background, so a dark-theme
  diagram remains legible pasted onto a white page.

## Video and images

An image or a `video` fence resolves in this order:

1. A known platform URL → a click-to-play facade (`<div class="embed-facade">`)
   inside a 16:9 `.embed-container`. The third-party player is only loaded when the
   tile is clicked or activated with Enter/Space, at which point the facade is
   replaced by an `<iframe>`.
2. A direct video file — `.mp4`, `.webm`, `.ogg`, `.mov`, `.avi`, `.mkv` — → a
   `<video controls>` element.
3. Otherwise → an ordinary `<img>` with `max-width: 100%`.

Recognised platform URLs:

| Platform | Pattern |
| --- | --- |
| YouTube | `youtube.com/watch?v=ID`, `youtu.be/ID`, `youtube.com/embed/ID` — an 11-character id |
| Bilibili | `bilibili.com/video/BV…` or any URL with `bvid=BV…` |
| Vimeo | `vimeo.com/123456` |

Not recognised: YouTube `/shorts/`, `youtube-nocookie.com` URLs, and anything
whose id is not 11 characters — those fall through to a plain image or a raw
`<video>`.

## Footnotes

```markdown
A claim needing support.[^1]

[^1]: The supporting note.
```

Renders `<sup class="footnote-ref">` at the reference and a `.footnotes` section at
the end, with `#fn1` / `#fnref1` anchors. Both directions work because they are
`#`-links and are therefore never intercepted.

## Raw HTML

`html: true` means raw HTML passes through unescaped — `<u>`, `<span style>`,
`<details>`/`<summary>`, `<a id>`, `<br>` all work. There is no sanitisation step,
so treat any Markdown you did not write as executable content.

## Theming hooks

Per-container control lives on these classes, both in the default stylesheet and in
the CSS generated by the theme view model:

| Class | Theme group |
| --- | --- |
| `.mermaid-container` | Mermaid background / padding / margin / radius |
| `.puml-container` | PlantUML background / padding / margin / radius |
| `.plot-container` | Plot background / padding / margin / radius, plus the axis colour that drives `.plot-container svg` via `currentColor` |

A single `color` on `.plot-container` colours the whole coordinate frame — grid,
axes, tick labels and title — because the stylesheet overrides the library's
hard-coded black strokes with `currentColor`.
