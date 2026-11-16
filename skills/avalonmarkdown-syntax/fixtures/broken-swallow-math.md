# Unterminated math block

Expected: `math-block-unterminated` (fail).

This lives in its own file because the failure **consumes everything after the
opening `$$`**. The block scanner in renderer.js sets `state.line = next + 1`
whether or not it found a closing delimiter, so the remainder of the document —
including headings, fences and all — is handed to KaTeX as one math expression.
A document that has this bug loses its bottom half, and nothing is logged.

$$
x = 1

This paragraph is swallowed. So is the heading below.
