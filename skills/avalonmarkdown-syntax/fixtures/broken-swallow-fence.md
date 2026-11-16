# Unclosed code fence

Expected: `unclosed-fence` (fail).

Its own file for the same reason as the math case: nothing closes the fence
below, so every following line becomes part of the code block. Note that a `~~~`
line would **not** close it either — a fence only closes with its own marker
character and at least as many of them.

```python
def f():
    return 1

This line, the heading below, and the rest of the file are all code now.
