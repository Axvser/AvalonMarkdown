# AvalonMarkdown — working notes for Claude

## Writing Markdown for this renderer

Read `skills/avalonmarkdown-syntax/SKILL.md` **before** writing or editing any
Markdown that this control will render — README content, demo content, docs, or
generated output.

The dialect is wider than CommonMark (math, Mermaid, PlantUML, function plots,
video embeds) and several of its failure modes are **silent**: a misspelled fence
info string becomes a plain code block, a bare `$$` mid-line deletes two
characters, an unclosed `$$` block swallows the rest of the document, and a
PlantUML syntax error comes back as a *renderable* error picture that displays at
full size as if it were fine.

Copy the source from `skills/avalonmarkdown-syntax/references/templates.md` rather
than reconstructing it from memory, and validate before publishing:

```bash
python skills/avalonmarkdown-syntax/scripts/render_check.py path/to/doc.md
```

## Verifying a change to the renderer

There is no unit test project and no CI. Verification is three environment-gated
drivers in the demo app, each of which exits the process — enable exactly one:

| Command | Checks |
| --- | --- |
| `AVALON_THEME_AUDIT=1 dotnet run --project AvalonMarkdown.Test.Desktop` | every `MarkdownThemeViewModel` property reaches the CSS, and every generated rule has a live consumer |
| `AVALON_CAPTURE_ERRORS=1 dotnet run --project AvalonMarkdown.Test.Desktop` | drives the app through its phases, asserts fence dispatch and that the showcase assets load |
| `AVALON_RENDER_CHECK=1 AVALON_RENDER_CHECK_FILE=<abs path> dotnet run --project AvalonMarkdown.Test.Desktop` | renders one file through the real pipeline and DOM-audits it; driven by `render_check.py` |

Run without any of them for a normal visual check.

`skills/avalonmarkdown-syntax/scripts/render_check.py --self-test` asserts every
validator check fires against the known-bad fixtures — a validator with no
known-bad sample cannot be trusted, so keep the fixtures and the assertions in
step when adding a check.
