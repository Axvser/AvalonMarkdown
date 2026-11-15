# Negative fixture

One deliberately-broken instance of every failure surface the checker claims to
catch. `render_check.py --self-test` asserts that each one fires.

The two failure modes that **swallow the rest of the document** — an unterminated
`$$` block and an unclosed code fence — live in `broken-swallow-math.md` and
`broken-swallow-fence.md` instead, because either one here would consume the
cases below it.

## Bad mermaid

Expected: `mermaid-render-error` (fail). Mermaid injects an error diagram whose
`.error-text` reads "Syntax error in text".

```mermaid
graph TD
    A[Start --> B{Unclosed label
    B -->|Yes| C
```

## Bad PlantUML

Expected: `puml-image-failed` (fail). The server answers 400, so the `<img>`
never acquires dimensions.

```plantuml
@startuml
this is not valid plantuml at all !!!
@enduml
```

## Plot with malformed JSON

Expected: `plot-error` (fail) — trailing comma, so `JSON.parse` throws. This is
the one plot failure that logs nothing at all.

```plot
{ "data": [ { "fn": "x^2" }, ] }
```

## Plot with a whitelisted-out expression

Expected: `plot-error` (fail) — the pipe is rejected before the expression ever
reaches the library, so it logs a warning as well.

```plot
{ "data": [ { "fn": "x | 2" } ] }
```

## Misspelled fence info string

Expected: `info-near-miss` (fail). `mermiad` is within edit distance 2 of
`mermaid`, so the diagram silently degrades into a highlighted code block.

```mermiad
graph TD
    A --> B
```

## Math that KaTeX cannot parse

Expected: `katex-error` (fail). Unbalanced braces — KaTeX renders its own error
span and logs nothing.

Inline: $\frac{1}{$

Block:

$$
\sqrt{1 + }
$$

## Malformed chat info string

Expected: `chat-info-malformed` (static) and `chat-error` (render). The renderer
turns the bad info string into a visible error block, so this one is not silent —
the static check still names the line and the accepted forms.

```chat[未闭合
```

## Unknown chat option

Expected: `chat-unknown-option` (static) and `chat-error` (render).

```chat[思考中][bogus]
```
