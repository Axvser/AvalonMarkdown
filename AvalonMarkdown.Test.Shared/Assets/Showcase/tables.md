# Tables — showcase

Every table variant the renderer produces, kept as a real `.md` asset so
`render_check.py` validates the exact file the demo renders.

Tables are GFM pipe tables: a header row, a delimiter row, then body rows. The
delimiter row is what makes it a table — `| --- |` alone is a paragraph.

---

## 1. The basics

### 1.1 Default alignment

The delimiter row's dashes carry no colons, so every column is left-aligned.

| Name | Role | Location |
| --- | --- | --- |
| Ada | Engineer | London |
| Grace | Compiler author | Arlington |
| Alan | Logician | Wilmslow |

### 1.2 Explicit alignment

`:--` left, `:-:` centre, `--:` right. The header follows the column's alignment.

| Left | Centre | Right |
| :--- | :----: | ----: |
| a | b | c |
| longer cell | x | 1 |
| x | much longer cell | 100000 |

### 1.3 Mixed alignment in one row

| Code | Description | Price |
| :--- | :---------- | ----: |
| `SKU-001` | A short description | 9.99 |
| `SKU-0002` | A considerably longer description that wraps | 1249.50 |

---

## 2. Cell contents

### 2.1 Inline formatting

| Syntax | Renders as | Notes |
| --- | --- | --- |
| `**bold**` | **bold** | |
| `*italic*` | *italic* | |
| `~~struck~~` | ~~struck~~ | |
| `<u>u</u>` | <u>u</u> | raw HTML, since Markdown has no underline |

### 2.2 Inline code, and the pipe problem

A `|` inside a cell must be escaped as `\|` **even inside a code span** — the
table parser splits on pipes before it parses inline markup. The last row below
shows the failure mode: an unescaped pipe silently splits the cell.

| Expression | Meaning |
| --- | --- |
| `a \| b` | bitwise or |
| `--flag` | a leading dash is fine |
| <code>x \|\| y</code> | logical or, via a raw HTML span |

### 2.3 Links and emphasis combined

| Resource | Link | Status |
| --- | --- | --- |
| Spec | [CommonMark](https://commonmark.org) | **stable** |
| GFM | [Tables extension](https://github.github.com/gfm/#tables-extension-) | *stable* |
| In-page | [back to the top](#tables-showcase) | anchor |

### 2.4 Math in cells

| Quantity | Formula | Value |
| --- | --- | --- |
| Area | $A = \pi r^2$ | 12.57 |
| Roots | $x = \frac{-b \pm \sqrt{b^2-4ac}}{2a}$ | 2 |

### 2.5 CJK, emoji and long words

| 项目 | 说明 | 状态 |
| --- | --- | --- |
| 前缀树 | 用于路由匹配的树形结构 | ✅ 完成 |
| 布隆过滤器 | 概率型集合，存在假阳性 | 🚧 进行中 |
| LZ77 | 滑动窗口压缩 | ❌ 未开始 |

| Long token | Note |
| --- | --- |
| `aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa` | an unbroken run with no spaces to wrap on |
| short | fine |

---

## 3. Edge shapes

### 3.1 A single column

| Only |
| --- |
| one |
| two |

### 3.2 Header only, no body rows

| Column A | Column B |
| --- | --- |

### 3.3 Empty cells

| A | B | C |
| --- | --- | --- |
| 1 | | 3 |
| | 2 | |
| | | |

### 3.4 A wide table

Eight columns; the container scrolls rather than the layout breaking.

| # | Name | Type | Default | Range | Unit | Since | Notes |
| --: | --- | --- | --- | --- | --- | --- | --- |
| 1 | `fontSize` | int | 14 | 8–32 | px | 1.0 | body text |
| 2 | `lineHeight` | double | 1.6 | 1–3 | × | 1.0 | |
| 3 | `showCodeLanguage` | bool | true | | | 1.2 | |
| 4 | `showCopyButton` | bool | true | | | 1.2 | |
| 5 | `maxCodeBlockHeight` | int | 480 | 0–4000 | px | 1.3 | 0 disables |
| 6 | `autoScrollOnUpdate` | bool | true | | | 2.0 | |
| 7 | `autoScrollCooldown` | int | 2000 | 0–60000 | ms | 2.0 | |
| 8 | `placeholder` | string | "" | | | 3.1 | blank by default |

### 3.5 Many rows

| n | n² | √n |
| --: | --: | --: |
| 1 | 1 | 1.000 |
| 2 | 4 | 1.414 |
| 3 | 9 | 1.732 |
| 4 | 16 | 2.000 |
| 5 | 25 | 2.236 |
| 6 | 36 | 2.449 |
| 7 | 49 | 2.646 |
| 8 | 64 | 2.828 |
| 9 | 81 | 3.000 |
| 10 | 100 | 3.162 |

### 3.6 Footnotes reached from a cell

| Claim | Source |
| --- | --- |
| HTML passes through unescaped[^1] | renderer |
| KaTeX renders with `throwOnError:false`[^2] | renderer |

[^1]: See the `html` option in the markdown-it configuration.
[^2]: That is why a bad expression produces a coloured span instead of an exception.

---

## 4. What is *not* a table

These are here so a regression that starts treating them as tables is visible.

### 4.1 A delimiter row with no header row

| --- | --- |

### 4.2 A header row with no delimiter row

| Name | Role |
| Ada | Engineer |

### 4.3 Block content in a cell is not supported

A list inside a cell stays literal text — GFM table cells hold inline content only.

| Cell | Content |
| --- | --- |
| list | - a<br>- b |

---

## 5. A table in a blockquote

> | Metric | Value |
> | --- | --: |
> | throughput | 1200 |
> | latency | 8 |
>
> A blockquote can hold a table, and the table keeps its own alignment.
