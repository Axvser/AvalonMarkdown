# Function plots — showcase

Every curve below is drawn by a `plot` fence. Kept as a real `.md` asset, not a C#
string, so `render_check.py` can validate the exact file the demo renders.

The expression grammar is deliberately narrow (see
`skills/avalonmarkdown-syntax/references/members.md`): digits, letters,
`+ - * / ^ ( ) . ,`, whitespace and `% ? : < > = !`. No helper functions can be
defined, so each piecewise easing is written out in full — which makes this a
useful stress test of the expression parser as well as of the renderer.

Constants are **upper-case**: `PI`, `E`.

---

## 1. Easing functions

The standard Penner / easings.net set. The domain is `[0, 1]` and the range is
widened to `[-0.3, 1.3]` so the overshoot of `back`, `elastic` and `bounce` is
visible rather than clipped.

Each plot shows one family: `linear` for reference, then `in`, `out`, `inOut`.

### 1.1 Sine

```plot
{
  "title": "Sine",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "1 - cos(x*PI/2)", "color": "#569cd6" },
    { "fn": "sin(x*PI/2)", "color": "#4ec9b0" },
    { "fn": "-(cos(PI*x) - 1)/2", "color": "#d69d85" }
  ]
}
```

### 1.2 Quadratic

```plot
{
  "title": "Quadratic",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x^2", "color": "#569cd6" },
    { "fn": "1 - (1-x)^2", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 2*x^2 : 1 - (-2*x+2)^2/2", "color": "#d69d85" }
  ]
}
```

### 1.3 Cubic

```plot
{
  "title": "Cubic",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x^3", "color": "#569cd6" },
    { "fn": "1 - (1-x)^3", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 4*x^3 : 1 - (-2*x+2)^3/2", "color": "#d69d85" }
  ]
}
```

### 1.4 Quartic

```plot
{
  "title": "Quartic",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x^4", "color": "#569cd6" },
    { "fn": "1 - (1-x)^4", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 8*x^4 : 1 - (-2*x+2)^4/2", "color": "#d69d85" }
  ]
}
```

### 1.5 Quintic

```plot
{
  "title": "Quintic",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x^5", "color": "#569cd6" },
    { "fn": "1 - (1-x)^5", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 16*x^5 : 1 - (-2*x+2)^5/2", "color": "#d69d85" }
  ]
}
```

### 1.6 Exponential

`x == 0` and `x == 1` are pinned so the curve does not evaluate to a fractional
power of a negative base at the endpoints.

```plot
{
  "title": "Exponential",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x == 0 ? 0 : 2^(10*x - 10)", "color": "#569cd6" },
    { "fn": "x == 1 ? 1 : 1 - 2^(-10*x)", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 2^(20*x - 10)/2 : (2 - 2^(-20*x + 10))/2", "color": "#d69d85" }
  ]
}
```

### 1.7 Circular

```plot
{
  "title": "Circular",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "1 - sqrt(1 - x^2)", "color": "#569cd6" },
    { "fn": "sqrt(1 - (x-1)^2)", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? (1 - sqrt(1 - (2*x)^2))/2 : (sqrt(1 - (-2*x+2)^2) + 1)/2", "color": "#d69d85" }
  ]
}
```

### 1.8 Back — overshoots below 0 and above 1

```plot
{
  "title": "Back",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "2.70158*x^3 - 1.70158*x^2", "color": "#569cd6" },
    { "fn": "1 + 2.70158*(x-1)^3 + 1.70158*(x-1)^2", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? (2*x)^2 * ((2.5949095 + 1)*2*x - 2.5949095)/2 : ((2*x - 2)^2 * ((2.5949095 + 1)*(2*x - 2) + 2.5949095) + 2)/2", "color": "#d69d85" }
  ]
}
```

### 1.9 Elastic — the widest overshoot

```plot
{
  "title": "Elastic",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x == 0 ? 0 : x == 1 ? 1 : -2^(10*x - 10)*sin((x*10 - 10.75)*(2*PI)/3)", "color": "#569cd6" },
    { "fn": "x == 0 ? 0 : x == 1 ? 1 : 2^(-10*x)*sin((x*10 - 0.75)*(2*PI)/3) + 1", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? -(2^(20*x - 10)*sin((20*x - 11.125)*(2*PI)/4.5))/2 : (2^(-20*x + 10)*sin((20*x - 11.125)*(2*PI)/4.5))/2 + 1", "color": "#d69d85" }
  ]
}
```

### 1.10 Bounce — a four-piece curve

```plot
{
  "title": "Bounce (out)",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x", "color": "#888888" },
    { "fn": "x < 0.36363636363636365 ? 7.5625*x^2 : x < 0.7272727272727273 ? 7.5625*(x - 0.5454545454545454)^2 + 0.75 : x < 0.9090909090909091 ? 7.5625*(x - 0.8181818181818182)^2 + 0.9375 : 7.5625*(x - 0.9545454545454546)^2 + 0.984375", "color": "#4ec9b0" }
  ]
}
```

### 1.11 Every easing, one mode at a time

The palette holds eight colours, so eleven series repeat beyond that — the
explicit `color` values below are carried over so a series keeps the same colour
across all three plots.

```plot
{
  "title": "easeIn — all families",
  "grid": true,
  "height": 320,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x^2", "color": "#569cd6" },
    { "fn": "x^3", "color": "#4ec9b0" },
    { "fn": "x^4", "color": "#d69d85" },
    { "fn": "x^5", "color": "#b5cea8" },
    { "fn": "1 - cos(x*PI/2)", "color": "#dcdcaa" },
    { "fn": "x == 0 ? 0 : 2^(10*x - 10)", "color": "#c586c0" },
    { "fn": "1 - sqrt(1 - x^2)", "color": "#9cdcfe" },
    { "fn": "2.70158*x^3 - 1.70158*x^2", "color": "#ce9178" },
    { "fn": "x == 0 ? 0 : x == 1 ? 1 : -2^(10*x - 10)*sin((x*10 - 10.75)*(2*PI)/3)", "color": "#e06c75" },
    { "fn": "x < 0.36363636363636365 ? 7.5625*x^2 : x < 0.7272727272727273 ? 7.5625*(x - 0.5454545454545454)^2 + 0.75 : x < 0.9090909090909091 ? 7.5625*(x - 0.8181818181818182)^2 + 0.9375 : 7.5625*(x - 0.9545454545454546)^2 + 0.984375", "color": "#98c379" }
  ]
}
```

```plot
{
  "title": "easeOut — all families",
  "grid": true,
  "height": 320,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "1 - (1-x)^2", "color": "#569cd6" },
    { "fn": "1 - (1-x)^3", "color": "#4ec9b0" },
    { "fn": "1 - (1-x)^4", "color": "#d69d85" },
    { "fn": "1 - (1-x)^5", "color": "#b5cea8" },
    { "fn": "sin(x*PI/2)", "color": "#dcdcaa" },
    { "fn": "x == 1 ? 1 : 1 - 2^(-10*x)", "color": "#c586c0" },
    { "fn": "sqrt(1 - (x-1)^2)", "color": "#9cdcfe" },
    { "fn": "1 + 2.70158*(x-1)^3 + 1.70158*(x-1)^2", "color": "#ce9178" },
    { "fn": "x == 0 ? 0 : x == 1 ? 1 : 2^(-10*x)*sin((x*10 - 0.75)*(2*PI)/3) + 1", "color": "#e06c75" },
    { "fn": "x < 0.36363636363636365 ? 7.5625*x^2 : x < 0.7272727272727273 ? 7.5625*(x - 0.5454545454545454)^2 + 0.75 : x < 0.9090909090909091 ? 7.5625*(x - 0.8181818181818182)^2 + 0.9375 : 7.5625*(x - 0.9545454545454546)^2 + 0.984375", "color": "#98c379" }
  ]
}
```

```plot
{
  "title": "easeInOut — all families",
  "grid": true,
  "height": 320,
  "xAxis": { "domain": [-0.05, 1.05] },
  "yAxis": { "domain": [-0.3, 1.3] },
  "data": [
    { "fn": "x < 0.5 ? 2*x^2 : 1 - (-2*x+2)^2/2", "color": "#569cd6" },
    { "fn": "x < 0.5 ? 4*x^3 : 1 - (-2*x+2)^3/2", "color": "#4ec9b0" },
    { "fn": "x < 0.5 ? 8*x^4 : 1 - (-2*x+2)^4/2", "color": "#d69d85" },
    { "fn": "x < 0.5 ? 16*x^5 : 1 - (-2*x+2)^5/2", "color": "#b5cea8" },
    { "fn": "-(cos(PI*x) - 1)/2", "color": "#dcdcaa" },
    { "fn": "x < 0.5 ? 2^(20*x - 10)/2 : (2 - 2^(-20*x + 10))/2", "color": "#c586c0" },
    { "fn": "x < 0.5 ? (1 - sqrt(1 - (2*x)^2))/2 : (sqrt(1 - (-2*x+2)^2) + 1)/2", "color": "#9cdcfe" },
    { "fn": "x < 0.5 ? (2*x)^2 * ((2.5949095 + 1)*2*x - 2.5949095)/2 : ((2*x - 2)^2 * ((2.5949095 + 1)*(2*x - 2) + 2.5949095) + 2)/2", "color": "#ce9178" },
    { "fn": "x < 0.5 ? -(2^(20*x - 10)*sin((20*x - 11.125)*(2*PI)/4.5))/2 : (2^(-20*x + 10)*sin((20*x - 11.125)*(2*PI)/4.5))/2 + 1", "color": "#e06c75" }
  ]
}
```

---

## 2. Ordinary curves

### 2.1 Trigonometric, with a LaTeX-free title

```plot
{
  "title": "sin, cos, tan",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [-6.283185307179586, 6.283185307179586] },
  "yAxis": { "domain": [-3, 3] },
  "data": [
    { "fn": "sin(x)", "color": "#569cd6" },
    { "fn": "cos(x)", "color": "#4ec9b0" },
    { "fn": "tan(x)", "color": "#d69d85", "nSamples": 2000 }
  ]
}
```

### 2.2 Damped oscillation — an envelope around a wave

```plot
{
  "title": "Damped oscillation",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [0, 18.84955592153876] },
  "yAxis": { "domain": [-1.2, 1.2] },
  "data": [
    { "fn": "exp(-x/4)*sin(x)", "color": "#569cd6" },
    { "fn": "exp(-x/4)", "color": "#888888", "skipTip": true },
    { "fn": "-exp(-x/4)", "color": "#888888", "skipTip": true }
  ]
}
```

### 2.3 A rational function, with its asymptote

`1/x` is undefined at 0. The library draws the two branches as separate segments;
the asymptote is drawn as a `points` series so it stays a straight line.

```plot
{
  "title": "1/x and its asymptote",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [-6, 6] },
  "yAxis": { "domain": [-6, 6] },
  "data": [
    { "fn": "1/x", "color": "#569cd6", "nSamples": 2000 },
    { "fnType": "points", "sampler": "builtIn", "points": [[0, -6], [0, 6]],
      "color": "#888888", "skipTip": true }
  ]
}
```

### 2.4 A discontinuous step

```plot
{
  "title": "Piecewise step",
  "grid": true,
  "height": 260,
  "xAxis": { "domain": [-4, 4] },
  "yAxis": { "domain": [-1.5, 1.5] },
  "data": [
    { "fn": "x < -1 ? -1 : x < 1 ? 0 : 1", "color": "#4ec9b0", "nSamples": 1200 }
  ]
}
```

### 2.5 Logarithmic and exponential

```plot
{
  "title": "log and exp",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [-4, 4] },
  "yAxis": { "domain": [-4, 4] },
  "data": [
    { "fn": "exp(x)", "color": "#569cd6" },
    { "fn": "log(x)", "color": "#4ec9b0", "range": [0.01, 4] },
    { "fn": "x", "color": "#888888" }
  ]
}
```

### 2.6 A logarithmic x-axis

```plot
{
  "title": "Logarithmic x-axis",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [0.1, 100], "type": "log", "label": "x (log)" },
  "yAxis": { "domain": [-2, 2], "label": "y" },
  "data": [
    { "fn": "log(x)/log(10)", "color": "#569cd6" },
    { "fn": "sin(log(x))", "color": "#4ec9b0" }
  ]
}
```

---

## 3. Non-`y = f(x)` graph types

### 3.1 Parametric — a Lissajous figure

```plot
{
  "title": "Parametric",
  "grid": true,
  "height": 340,
  "xAxis": { "domain": [-1.2, 1.2] },
  "yAxis": { "domain": [-1.2, 1.2] },
  "data": [
    { "fnType": "parametric", "sampler": "builtIn", "x": "sin(3*t)", "y": "sin(4*t)",
      "range": [0, 6.283185307179586], "color": "#569cd6" }
  ]
}
```

### 3.2 Polar — a cardioid

```plot
{
  "title": "Polar",
  "grid": true,
  "height": 340,
  "xAxis": { "domain": [-2.4, 2.4] },
  "yAxis": { "domain": [-2.4, 2.4] },
  "data": [
    { "fnType": "polar", "sampler": "builtIn", "r": "1 - cos(theta)", "color": "#4ec9b0" },
    { "fnType": "polar", "sampler": "builtIn", "r": "2*cos(theta)", "color": "#d69d85" }
  ]
}
```

### 3.3 Implicit — a circle

```plot
{
  "title": "Implicit",
  "grid": true,
  "height": 340,
  "xAxis": { "domain": [-2.5, 2.5] },
  "yAxis": { "domain": [-2.5, 2.5] },
  "data": [
    { "fnType": "implicit", "fn": "x^2 + y^2 - 4", "color": "#569cd6" }
  ]
}
```

### 3.4 Scatter and points

```plot
{
  "title": "Scatter and points",
  "grid": true,
  "height": 280,
  "xAxis": { "domain": [-1, 7] },
  "yAxis": { "domain": [-2, 6] },
  "data": [
    { "graphType": "scatter", "sampler": "builtIn", "fn": "x", "color": "#569cd6",
      "nSamples": 24 },
    { "fnType": "points", "sampler": "builtIn",
      "points": [[1, 1], [2, 4], [3, 1], [4, 4], [5, 1]],
      "color": "#d69d85" }
  ]
}
```

### 3.5 A tangent line and a text annotation

```plot
{
  "title": "Derivative and annotation",
  "grid": true,
  "height": 300,
  "xAxis": { "domain": [-3, 3] },
  "yAxis": { "domain": [-1, 8] },
  "data": [
    { "fn": "x^2", "color": "#569cd6", "derivative": { "fn": "2*x", "x0": 1.5 } },
    { "fnType": "text", "location": [2.1, 1.2], "text": "tangent at x = 1.5",
      "graphType": "text" }
  ]
}
```

### 3.6 Several vector fields are not supported; vectors are individual

```plot
{
  "title": "Vectors",
  "grid": true,
  "height": 300,
  "xAxis": { "domain": [-3, 3] },
  "yAxis": { "domain": [-3, 3] },
  "data": [
    { "fnType": "vector", "sampler": "builtIn", "vector": [1, 1], "offset": [-2, -2],
      "color": "#569cd6" },
    { "fnType": "vector", "sampler": "builtIn", "vector": [2, -1], "offset": [1, 1],
      "color": "#4ec9b0" }
  ]
}
```

---

## 4. Declining to draw

Two fences that must render an error block rather than a chart, so the demo
documents what a failure looks like next to a wall of successes.

```plot
{ "data": [ { "fn": "sin(x)" }, ] }
```

```plot
{ "data": [ { "fn": "x | 1" } ] }
```
