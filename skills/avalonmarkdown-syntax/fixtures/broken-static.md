# Static-only failures

Both of these are invisible to the render tier — they produce no error artifact
in the DOM at all — which is why the static tier is load-bearing rather than a
convenience.

## Bare double dollar mid-line

Expected: `math-inline-empty-dollar` (fail).

The renderer's inline math rule has a branch for `match - start === 0` that
appends an **empty** string and advances past both characters, so the two dollars
are silently deleted. The line below renders as "Total is  before tax." with a
double space and nothing to indicate anything went wrong.

Total is $$ before tax.

## Loose single dollar

Expected: `math-inline-unclosed` (fail).

An opener whose next character is a word character cannot open, so `$5` stays
literal — that part is correct. The case below is a genuine unclosed opener: the
preceding character is a space, so it can open, and no closer follows.

The price rose by $ and then it stopped.
