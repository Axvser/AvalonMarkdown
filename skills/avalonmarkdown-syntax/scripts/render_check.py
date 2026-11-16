#!/usr/bin/env python3
"""Validate an AvalonMarkdown document: will it actually render?

Two tiers, because they answer different questions.

  Static tier (always runs, stdlib only, instant, offline)
      Re-derives the renderer's own dispatch rules from first principles: fenced
      blocks and their info strings, the ```plot JSON contract and its expression
      whitelist, the $$ block pairing, and the inline-math delimiter rules.
      It catches structural mistakes and -- importantly -- two failures the
      render tier provably cannot see: a bare $$ mid-line silently deletes two
      characters, and an unterminated $$ block swallows the rest of the document.

  Render tier (opt-in, needs a build and a desktop session)
      Renders the file through the real WebView pipeline and inspects the DOM,
      because that is the only way to know whether KaTeX, mermaid and
      function-plot actually produced output. Most failures in this renderer are
      SILENT -- KaTeX renders its own error span without throwing or logging,
      and a PlantUML server failure has no detection at all -- so a validator
      that only reads console output would pass a broken document.

Exit codes: 0 pass, 1 findings, 2 usage/config error, 3 environment problem.

    python render_check.py doc.md              # static, plus render if a build exists
    python render_check.py --no-render doc.md  # static only, fast
    python render_check.py --self-test         # assert every check fires
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

HERE = Path(__file__).resolve().parent
SKILL_DIR = HERE.parent
REPO_ROOT = SKILL_DIR.parent.parent

# The info strings renderer.js intercepts in `md.renderer.rules.fence`.
# Everything else falls through to markdown-it's built-in fence renderer, which
# routes through the highlight.js wrapper -- that is not an error, so an
# unrecognised info string is only reported when it is a NEAR MISS of one of
# these (a typo silently degrades a diagram into a plain code block).
#
# `chat` is special: it dispatches on the bare word and on `chat[...]` (the
# brackets carry a free-form label plus options), so it is checked separately
# rather than by the exact-equality path below.
SPECIAL_FENCES = ("mermaid", "plantuml", "puml", "plot", "video", "chat")

# renderer.js parseChatInfo(): chat, chat[label], chat[label][option,...].
# A label cannot contain "]" -- the renderer rejects it with a visible error.
CHAT_INFO_RE = re.compile(r"^chat((?:\[[^\]]*\])*)$")
CHAT_OPTIONS = frozenset({
    "open", "expanded", "collapsed",
    "spin", "live", "active",       # status halo animation on (the default)
    "static", "idle",               # freeze the halo for a finished step
    "thinking", "think",            # lightbulb icon
    "tool", "tools", "tool-call", "toolcall",   # wrench icon
    "done", "complete", "success",  # check icon + frozen halo by default
    "error", "fail", "failed",      # cross icon + frozen halo by default
    "chat", "message",              # chat-bubble icon
})

# renderer.js:414 -- /^[0-9A-Za-z_+\-*/^().,\s%?:<>=!]*$/
# Rejecting quotes, backticks, semicolons, braces, brackets and backslash keeps
# the evaluator's new Function() path from being reachable.
PLOT_FN_WHITELIST = re.compile(r"[0-9A-Za-z_+\-*/^().,\s%?:<>=!]*")

PLOT_EXPR_KEYS = ("fn", "x", "y", "r")

# Real highlight.js languages that sit within edit distance 2 of a special fence
# keyword. Without these the near-miss check fires on legitimately-highlighted
# code; the list is short on purpose and safe to extend.
NEAR_MISS_EXCEPTIONS = frozenset({"pot", "dot", "pml", "plotly", "videos"})

# renderer.js:62-65 noise filter, mirrored so the render tier's console slice
# agrees with what the preview itself considers an error.
PLANTUML_SERVER = "https://www.plantuml.com/plantuml/svg/"

# plantuml.com fronts its service with a bot filter: a default urllib
# User-Agent is answered with HTTP 403 "error code: 1010". The preview loads the
# diagram through an <img> in a browser engine, so the checker has to ask with
# the same kind of User-Agent or it will report an environment failure as a
# diagram failure.
_BROWSER_UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 Edg/152.0.0.0"
)

FAIL, WARN, ENV = "fail", "warn", "environment"


@dataclass
class Finding:
    id: str
    severity: str
    message: str
    line: int = 0
    col: int = 0
    sample: str = ""

    def where(self) -> str:
        return f"line {self.line}" + (f":{self.col}" if self.col else "")


@dataclass
class Fence:
    info: str
    body: str
    open_line: int      # 1-based line of the opening marker
    body_line: int      # 1-based line of the first body line
    closed: bool
    ordinal: int = 0    # 1-based, among fences of the same info string


# --------------------------------------------------------------------------
# text helpers
# --------------------------------------------------------------------------

_WORD_RE = re.compile(r"[A-Za-z0-9_]")


def _is_word(ch: str) -> bool:
    """mirror of renderer.js `isWord` = /^[\\w\\d]$/u -- ASCII only.

    Because \\w already covers digits, this is exactly [A-Za-z0-9_]. CJK is
    deliberately NOT a word character, which is why 中$x$ opens fine while
    1$x$ does not.
    """
    return bool(_WORD_RE.fullmatch(ch))


def _is_ws(ch: str) -> bool:
    """mirror of renderer.js `isWS` = /^\\s$/u."""
    return ch.isspace()


def levenshtein(a: str, b: str) -> int:
    if a == b:
        return 0
    if not a:
        return len(b)
    if not b:
        return len(a)
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


# --------------------------------------------------------------------------
# static tier -- fences
# --------------------------------------------------------------------------

_OPENER_RE = re.compile(r"^( {0,3})(`{3,}|~{3,})(.*)$")


def scan_fences(text: str) -> tuple[list[Fence], list[Finding]]:
    """Line-scanner mirroring markdown-it's fence rule.

    Deliberately lenient about indentation: a fence nested in a list or
    blockquote is kept, at the cost of not modelling every exotic case. False
    negatives are cheap here; false positives are not.
    """
    lines = text.split("\n")
    fences: list[Fence] = []
    findings: list[Finding] = []
    counters: dict[str, int] = {}

    i = 0
    while i < len(lines):
        m = _OPENER_RE.match(lines[i])
        if not m:
            i += 1
            continue
        marker = m.group(2)
        char = marker[0]
        info = m.group(3).strip()
        # A backtick fence may not carry a backtick in its info string --
        # markdown-it refuses the fence entirely in that case.
        if char == "`" and "`" in m.group(3):
            i += 1
            continue

        close_re = re.compile(rf"^ {{0,3}}{re.escape(char)}{{{len(marker)},}}\s*$")
        j = i + 1
        while j < len(lines) and not close_re.match(lines[j]):
            j += 1
        closed = j < len(lines)

        body = "\n".join(lines[i + 1:j]) if closed else "\n".join(lines[i + 1:])
        info_key = info.lower()
        ordinal = counters.get(info_key, 0) + 1
        counters[info_key] = ordinal

        fences.append(Fence(info=info_key, body=body, open_line=i + 1,
                            body_line=i + 2, closed=closed, ordinal=ordinal))

        if not closed:
            findings.append(Finding(
                id="unclosed-fence", severity=FAIL, line=i + 1,
                message=f"fence opened with {marker!r} is never closed; "
                        "everything after it becomes code",
                sample=info))

        i = j + 1 if closed else len(lines)

    return fences, findings


def is_chat_info(info: str) -> bool:
    """True for the info strings renderer.js routes to renderChatFence()."""
    return info == "chat" or info.startswith("chat[")


def check_fence_info(info: str) -> list[Finding]:
    """Only near misses are findings -- an unknown info string is a normal code block."""
    if not info or info in SPECIAL_FENCES or info in NEAR_MISS_EXCEPTIONS:
        return []
    if is_chat_info(info):
        # Structural validation lives in check_chat(), which has the line number.
        return []
    if " " in info or "\t" in info:
        # ```mermaid {theme=dark} does NOT dispatch in this renderer: the info
        # string is compared with exact equality after trim+lowercase.
        head = info.split()[0]
        if head in SPECIAL_FENCES:
            return [Finding(
                id="info-not-exact", severity=FAIL,
                message=f"info string {info!r} does not dispatch: the renderer compares "
                        f"it exactly, so this renders as a code block. Use {head!r} alone.",
                sample=info)]
        return []
    for kw in SPECIAL_FENCES:
        if abs(len(info) - len(kw)) <= 2 and info[:1] == kw[:1] and levenshtein(info, kw) <= 2:
            return [Finding(
                id="info-near-miss", severity=FAIL,
                message=f"info string {info!r} looks like a typo of {kw!r}; it will "
                        f"silently render as a code block instead of a diagram",
                sample=info)]
    return []


# --------------------------------------------------------------------------
# static tier -- plot
# --------------------------------------------------------------------------


def _reject_constant(name: str):
    raise ValueError(f"{name} is not valid JSON")


def check_plot(fence: Fence) -> list[Finding]:
    """Replicates hydratePlots() in renderer.js:564-608."""
    out: list[Finding] = []
    raw = fence.body

    try:
        # Python accepts NaN/Infinity/-Infinity by default; JSON.parse does not.
        parsed = json.loads(raw, parse_constant=_reject_constant)
    except ValueError as e:
        offset = getattr(e, "lineno", None)
        line = fence.body_line + (offset - 1) if offset else fence.open_line
        out.append(Finding(
            id="plot-invalid-json", severity=FAIL, line=line,
            message=f"plot body is not valid JSON ({e}); the renderer shows an inline "
                    "error block and logs nothing",
            sample=raw.strip()[:200]))
        return out

    if not isinstance(parsed, dict):
        out.append(Finding(
            id="plot-not-object", severity=FAIL, line=fence.body_line,
            message="the plot body must be a single JSON object, e.g. "
                    '{ "data": [{ "fn": "x^2" }] }',
            sample=raw.strip()[:200]))
        return out

    data = parsed.get("data")
    if data is None:
        out.append(Finding(
            id="plot-no-data", severity=WARN, line=fence.body_line,
            message="plot has no \"data\"; it will draw bare axes"))
        return out

    if not isinstance(data, list):
        # plotExpressionError() returns null for a non-array, so the whitelist is
        # silently skipped -- and buildPlotOptions skips recolouring. The failure
        # surfaces later as an opaque library throw.
        out.append(Finding(
            id="plot-data-not-array", severity=FAIL, line=fence.body_line,
            message=f"\"data\" must be an array, got {type(data).__name__}; the expression "
                    "whitelist is silently skipped and the library will throw",
            sample=raw.strip()[:200]))
        return out

    for idx, item in enumerate(data):
        if not isinstance(item, dict):
            continue
        for key in PLOT_EXPR_KEYS:
            val = item.get(key)
            if isinstance(val, str) and not PLOT_FN_WHITELIST.fullmatch(val):
                bad = sorted({c for c in val if not PLOT_FN_WHITELIST.fullmatch(c)})
                out.append(Finding(
                    id="plot-expression-rejected", severity=FAIL,
                    line=fence.body_line,
                    message=f"expression in data[{idx}].{key} contains {''.join(bad)!r}, "
                            "which the whitelist rejects; the renderer shows an error block",
                    sample=f"{val!r}"))

    # Not checked on purpose: a duplicate "id" (buildPlotOptions deletes it), and
    # width/height/title/color, none of which can fail.
    return out


# --------------------------------------------------------------------------
# static tier -- chat
# --------------------------------------------------------------------------


def _split_chat_options(group: str) -> list[str]:
    """Split an option group on commas outside quotes.

    A custom ``svg="M12 2,3 4"`` path contains commas, so a plain split would
    produce false unknown-option findings. Quotes are kept so the value can be
    unwrapped by the caller.
    """
    out: list[str] = []
    cur = ""
    quote = ""
    for ch in group:
        if quote:
            cur += ch
            if ch == quote:
                quote = ""
        elif ch in "\"'":
            quote = ch
            cur += ch
        elif ch == ",":
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return out


def check_chat(fence: Fence) -> list[Finding]:
    """Validate ```chat[label][option,...]``` without relying on the render tier.

    The renderer turns a malformed info string or an unknown option into a visible
    `.chat-error`, so unlike a typo'd ``mermaid`` this is not silent. The static
    check still repeats it here because it can name the line and the allowed
    options, and because the render tier is optional.
    """
    out: list[Finding] = []
    info = fence.info          # scan_fences() lower-cases it
    match = CHAT_INFO_RE.fullmatch(info)
    if not match:
        return [Finding(
            id="chat-info-malformed", severity=FAIL, line=fence.open_line,
            message="chat fence info must be `chat`, `chat[label]` or "
                    "`chat[label][option,...]`; this form renders a visible error block",
            sample=fence.info)]

    groups = re.findall(r"\[([^\]]*)\]", info)
    for group in groups[1:]:
        for raw_option in _split_chat_options(group):
            option = raw_option.strip()
            if not option:
                continue
            key, sep, value = option.partition("=")
            if sep and key.strip().lower() == "svg":
                if not value.strip().strip("\"'"):
                    out.append(Finding(
                        id="chat-svg-empty", severity=FAIL, line=fence.open_line,
                        message="svg= needs path data or an SVG string; an empty value "
                                "renders a visible error block",
                        sample=fence.info))
                continue
            if option not in CHAT_OPTIONS:
                out.append(Finding(
                    id="chat-unknown-option", severity=FAIL, line=fence.open_line,
                    message=f"unknown chat option {option!r}; recognised options are "
                            "open, expanded, collapsed, spin, static, thinking, tool, "
                            "done, error, chat and svg=...",
                    sample=fence.info))
    return out


# --------------------------------------------------------------------------
# static tier -- math
# --------------------------------------------------------------------------


def _blank_code(text: str, fences: list[Fence]) -> list[str]:
    """Line array with fenced blocks and inline code spans blanked out.

    Math inside a fence or a code span is not math, and scanning it would produce
    false positives. Line count is preserved so reported line numbers stay true.
    """
    lines = text.split("\n")
    for f in fences:
        end = f.open_line + (f.body.count("\n") + 1) + (1 if f.closed else 0)
        for ln in range(f.open_line - 1, min(end, len(lines))):
            lines[ln] = ""

    # Blank inline code spans (`` `x` ``, `` ``x`` ``) outside of fences.
    def blank_span(m: re.Match) -> str:
        return " " * len(m.group(0))

    return [re.sub(r"(?<!`)`{1,3}[^`\n]*`{1,3}(?!`)", blank_span, ln) for ln in lines]


def _paragraphs(lines: list[str]) -> list[tuple[int, str]]:
    """Group non-blank lines into paragraphs -> (1-based first line, joined text)."""
    out: list[tuple[int, str]] = []
    buf: list[str] = []
    start = 0
    for n, ln in enumerate(lines, 1):
        if ln.strip():
            if not buf:
                start = n
            buf.append(ln)
        elif buf:
            out.append((start, "\n".join(buf)))
            buf = []
    if buf:
        out.append((start, "\n".join(buf)))
    return out


def check_math(text: str, fences: list[Fence]) -> list[Finding]:
    """Ports the math_inline / math_block rulers (renderer.js:778-853)."""
    out: list[Finding] = []
    lines = _blank_code(text, fences)

    # ---- $$ blocks -------------------------------------------------------
    # The block ruler sets state.line = next + 1 whether or not it found a
    # closer, so an unterminated block consumes the whole rest of the document.
    consumed: set[int] = set()
    n = 0
    while n < len(lines):
        stripped = lines[n].lstrip()
        if stripped.startswith("$$") and len(lines[n]) - len(stripped) <= 3:
            close = None
            for m in range(n + 1, len(lines)):
                if lines[m].strip().endswith("$$"):
                    close = m
                    break
            if close is None:
                out.append(Finding(
                    id="math-block-unterminated", severity=FAIL, line=n + 1,
                    message=f"$$ block is never closed, so the remaining "
                            f"{len(lines) - n} lines are all swallowed into it as math; "
                            "the document loses its bottom half and nothing is logged"))
                consumed.update(range(n, len(lines)))
                break
            consumed.update(range(n, close + 1))
            n = close + 1
        else:
            n += 1

    # ---- inline $ --------------------------------------------------------
    # Lines already claimed by a $$ block are not prose; re-scanning them here
    # would read the block's own delimiters as an empty inline pair.
    for n in consumed:
        lines[n] = ""

    for first_line, para in _paragraphs(lines):
        src = para
        i = 0
        line_offset = 0
        while i < len(src):
            ch = src[i]
            if ch == "\n":
                line_offset += 1
                i += 1
                continue
            if ch != "$":
                i += 1
                continue

            prev = src[i - 1] if i > 0 else None
            nxt = src[i + 1] if i + 1 < len(src) else None
            can_open = prev != "$" and prev != "\\" and (
                prev is None or _is_ws(prev) or not _is_word(prev))

            if not can_open:
                i += 1
                continue

            start = i + 1
            match = src.find("$", start)
            while match != -1:
                p = match - 1
                while p >= 0 and src[p] == "\\":
                    p -= 1
                if (match - p) % 2 == 1:
                    break
                match = src.find("$", match + 1)

            if match == -1:
                out.append(Finding(
                    id="math-inline-unclosed", severity=FAIL,
                    line=first_line + line_offset,
                    message="$ opens inline math that never closes; it renders as a "
                            "literal dollar sign"))
                i = start
                continue

            if match - start == 0:
                # renderer.js:809-812 -- this branch appends an EMPTY string and
                # advances past both characters, so the two dollars vanish with
                # no artifact anywhere. Only the static tier can see this.
                out.append(Finding(
                    id="math-inline-empty-dollar", severity=FAIL,
                    line=first_line + line_offset,
                    message="bare $$ mid-line: the renderer silently deletes both "
                            "characters, leaving a double space and no other trace"))
                i = start + 1
                continue

            nxt2 = src[match + 1] if match + 1 < len(src) else None
            can_close = nxt2 != "$" and (
                nxt2 is None or _is_ws(nxt2) or not _is_word(nxt2))
            if not can_close:
                i = start
                continue

            i = match + 1

    return out


# --------------------------------------------------------------------------
# plantuml server check
# --------------------------------------------------------------------------


def _puml_verdict(status: int, text: str) -> tuple[str, str]:
    """Map one server answer to (verdict, detail).

    Order matters: the HTTP status is checked BEFORE the body markers, because the
    400 response for a syntax error also contains "Welcome to PlantUML".
    """
    if status != 200:
        return "rejected", " ".join(text.split())[:200]
    if 'data-diagram-type="' in text:
        return "accepted", ""
    if "Welcome to PlantUML" in text:
        return "empty", ""
    if "Syntax Error" in text:
        return "syntax", " ".join(text.split())[:200]
    return "unknown", " ".join(text.split())[:200]


def _puml_findings(verdict: str, detail: str) -> list[Finding]:
    if verdict == "accepted":
        return []
    if verdict == "empty":
        return [Finding(
            id="plantuml-empty", severity=FAIL,
            message="PlantUML returned its welcome placeholder: the diagram is empty or "
                    "degenerate (typically a bare @startuml/@enduml pair)")]
    if verdict == "syntax":
        return [Finding(
            id="plantuml-syntax", severity=FAIL,
            message="PlantUML reported a syntax error inside a 200 response",
            sample=detail)]
    if verdict == "rejected":
        return [Finding(
            id="plantuml-rejected", severity=FAIL,
            message="PlantUML rejected the diagram; the preview will show a blank box "
                    "with no error",
            sample=detail)]
    return [Finding(
        id="plantuml-unknown-shape", severity=WARN,
        message="PlantUML returned HTTP 200 with an unrecognised body shape",
        sample=detail)]


def _puml_key(body: str) -> str:
    return hashlib.sha256(body.encode("utf-8")).hexdigest()[:32]


def _puml_cache_read(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


def _puml_cache_write(path: Path, data: dict) -> None:
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(data, indent=1, sort_keys=True), encoding="utf-8")
    except OSError:
        pass   # a cache that cannot be written is not a failure


def check_plantuml(body: str, args, cache: dict) -> tuple[list[Finding], str]:
    """Ask the server the same question the preview's <img> asks.

    This is the only way to validate PlantUML: a server-side failure leaves a broken
    <img> with no event and no console message, and the browser cannot tell "diagram
    rejected" from "network down". Reading the response body here can.

    Three things make it survive the host being flaky, because it is a third party
    and `--self-test` checks the SAME diagrams on every run:

    * **retry** — a refused connection or a timeout is retried with backoff; a real
      HTTP answer is never retried, it IS the answer;
    * **cache** — a verdict for a given source is stable (syntax does not change), so
      one successful run makes every later run offline and deterministic;
    * **--plantuml-server** — an escape hatch when the host is blocked outright.

    Only definitive answers are cached. "Unreachable" never is, or an outage would
    outlive itself.
    """
    from plantuml_encode import diagram_url

    key = _puml_key(body)
    if args.plantuml_cache_enabled:
        hit = cache.get(key)
        if hit:
            return _puml_findings(hit.get("verdict", "unknown"),
                                  hit.get("detail", "")), "ok"

    url = diagram_url(body, args.plantuml_server)
    headers = {"User-Agent": _BROWSER_UA,
               "Accept": "image/svg+xml,image/*,*/*;q=0.8"}

    status = None
    text = ""
    last_err: Exception | None = None
    for attempt in range(max(0, args.plantuml_retries) + 1):
        if attempt:
            time.sleep(min(2 ** attempt, 8))
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=args.plantuml_timeout) as r:
                status, text = r.status, r.read().decode("utf-8", "replace")
            break
        except urllib.error.HTTPError as e:
            # A response, even a 400 — that is the answer, do not retry it.
            status, text = e.code, e.read().decode("utf-8", "replace")
            break
        except Exception as e:   # DNS, TLS, timeout, connection reset
            last_err = e

    if status is None:
        return ([Finding(
            id="plantuml-unreachable", severity=ENV,
            message=f"could not reach {args.plantuml_server} after "
                    f"{max(0, args.plantuml_retries) + 1} attempt(s) "
                    f"({last_err.__class__.__name__ if last_err else 'unknown'}); "
                    "diagram NOT validated -- this is an environment problem, not a "
                    "document problem")], "unreachable")

    verdict, detail = _puml_verdict(status, text)
    if args.plantuml_cache_enabled:
        cache[key] = {"verdict": verdict, "detail": detail,
                      "at": datetime.date.today().isoformat()}
    return _puml_findings(verdict, detail), "ok"


# --------------------------------------------------------------------------
# render tier
# --------------------------------------------------------------------------


def find_app(repo: Path, configuration: str) -> Path | None:
    for cfg in ([configuration] if configuration != "auto" else ["Debug", "Release"]):
        for tfm in ("net10.0", "net8.0"):
            p = repo / "AvalonMarkdown.Test.Desktop" / "bin" / cfg / tfm / "AvalonMarkdown.Test.Desktop.exe"
            if p.exists():
                return p
    return None


_RENDER_CHECK_MARKER = b"AVALON_RENDER_CHECK"


def app_supports_render_check(app: Path) -> bool:
    """Does this build actually know the mode exists?

    Without this the failure mode is nasty: a build predating the render-check
    driver simply ignores the environment variable, launches the normal GUI,
    and never exits -- so the CLI burns its whole timeout and then reports a
    timeout instead of the truth. Cheap byte-scan, same trick used to catch a
    stale embedded asset.
    """
    for dll in app.parent.glob("AvalonMarkdown*.dll"):
        try:
            if _RENDER_CHECK_MARKER in dll.read_bytes():
                return True
        except OSError:
            continue
    return False


def run_render_tier(app: Path, md: Path, out_dir: Path, settle_ms: int,
                    theme: str, timeout: int) -> tuple[dict | None, str]:
    """Launch the harness in render-check mode and read back its JSON report.

    The child environment MUST be scrubbed of the other two drivers' variables:
    the harness refuses to start when more than one driver is enabled, and an
    inherited AVALON_CAPTURE_ERRORS would turn every run into a config error.
    """
    if not app_supports_render_check(app):
        return None, (f"{app.name} does not contain the render-check driver -- the build "
                      "predates it. Rebuild, or pass --no-render to skip this tier.")

    out_dir.mkdir(parents=True, exist_ok=True)
    report_path = out_dir / "render-check.json"
    log_path = out_dir / "render-check.log"

    env = dict(os.environ)
    for var in ("AVALON_CAPTURE_ERRORS", "AVALON_THEME_AUDIT"):
        env.pop(var, None)
    env.update({
        "AVALON_RENDER_CHECK": "1",
        "AVALON_RENDER_CHECK_FILE": str(md.resolve()),
        "AVALON_RENDER_CHECK_OUT": str(report_path.resolve()),
        "AVALON_RENDER_CHECK_LOG": str(log_path.resolve()),
        "AVALON_RENDER_CHECK_SETTLE_MS": str(settle_ms),
        "AVALON_RENDER_CHECK_THEME": theme,
    })

    try:
        proc = subprocess.run([str(app)], env=env, capture_output=True,
                              text=True, timeout=timeout)
        code = proc.returncode
    except subprocess.TimeoutExpired:
        return None, (f"harness did not finish within {timeout}s; its own watchdog "
                      "should have fired first -- treat as an environment failure")

    if not report_path.exists():
        detail = (proc.stderr or proc.stdout or "").strip()[-400:]
        return None, (f"harness exited {code} without writing a report "
                      f"(exit 2 = driver conflict or bad file path){': ' + detail if detail else ''}")

    try:
        report = json.loads(report_path.read_text(encoding="utf-8"))
    except ValueError as e:
        return None, f"harness wrote an unreadable report: {e}"

    if not str(report.get("schema", "")).startswith("avalonmarkdown.render-check/"):
        return None, ("harness report has an unexpected schema -- is the build current?")

    return report, ""


# --------------------------------------------------------------------------
# driver
# --------------------------------------------------------------------------


def analyse(path: Path, args) -> dict:
    raw = path.read_bytes()
    text = raw.decode("utf-8", errors="replace")
    findings: list[Finding] = []
    unvalidated: list[str] = []

    fences, fence_findings = scan_fences(text)
    findings += fence_findings

    counts = {"mermaid": 0, "plantuml": 0, "plot": 0, "video": 0, "chat": 0, "code": 0}
    # Set when the PlantUML host could not be reached, so callers can tell "the
    # diagram is fine" from "we could not ask". plantuml.com is a third party and
    # does go away.
    plantuml_unreachable = False
    for f in fences:
        info = f.info
        if info in ("plantuml", "puml"):
            counts["plantuml"] += 1
        elif is_chat_info(info):
            counts["chat"] += 1
        elif info in counts:
            counts[info] += 1
        else:
            counts["code"] += 1

        for fi in check_fence_info(info):
            findings.append(Finding(id=fi.id, severity=fi.severity,
                                    message=fi.message, line=f.open_line, sample=fi.sample))

        if info == "plot":
            findings += check_plot(f)
        elif info in ("plantuml", "puml"):
            if not f.body.strip().startswith("@start"):
                findings.append(Finding(
                    id="puml-missing-start", severity=FAIL, line=f.body_line,
                    message="PlantUML body does not start with @startuml; the server will "
                            "reject it"))
            elif "@end" not in f.body:
                findings.append(Finding(
                    id="puml-missing-end", severity=FAIL, line=f.body_line,
                    message="PlantUML body has no @enduml"))
            if args.plantuml:
                pf, pmode = check_plantuml(f.body, args, _puml_cache)
                if pmode == "unreachable":
                    plantuml_unreachable = True
                for fi in pf:
                    fi.line = fi.line or f.body_line
                    findings.append(fi)
            else:
                unvalidated.append(f"plantuml fence #{f.ordinal}: server check disabled")
        elif info == "mermaid":
            if not f.body.strip():
                findings.append(Finding(
                    id="mermaid-empty", severity=FAIL, line=f.body_line,
                    message="mermaid fence is empty"))
            else:
                unvalidated.append(
                    f"mermaid fence #{f.ordinal}: diagram semantics only checked by the "
                    "render tier")
        elif info == "video":
            if not f.body.strip():
                findings.append(Finding(
                    id="video-empty", severity=FAIL, line=f.body_line,
                    message="video fence is empty; it renders <source src=\"\"> silently"))
        elif is_chat_info(info):
            findings += check_chat(f)

    findings += check_math(text, fences)

    if counts["mermaid"] == 0:
        unvalidated.append("mermaid: no fences -- nothing validated")
    if counts["plantuml"] == 0:
        unvalidated.append("plantuml: no fences -- nothing validated")
    if counts["plot"] == 0:
        unvalidated.append("plot: no fences -- nothing validated")

    return {
        "file": str(path),
        "sha256": hashlib.sha256(raw).hexdigest(),
        "bytes": len(raw),
        "counts": counts,
        "findings": [f.__dict__ for f in findings],
        "unvalidated": unvalidated,
        "plantuml_unreachable": plantuml_unreachable,
        "render": None,
        "render_error": "",
    }


_puml_cache: dict[str, tuple] = {}


def main(argv: list[str]) -> int:
    # A Windows console often reports cp936/GBK. A sample from the DOM can contain
    # characters the console cannot encode (the "⚠" in a .chat-error message, for
    # example), and printing one would otherwise crash the report itself.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="backslashreplace")
        except (AttributeError, ValueError):
            pass

    ap = argparse.ArgumentParser(
        prog="render_check.py",
        description="Validate an AvalonMarkdown document.",
        epilog="Exit codes: 0 pass, 1 findings, 2 usage error, 3 environment problem.")
    ap.add_argument("files", nargs="*", type=Path)
    ap.add_argument("--render", dest="render", action="store_true", default=None,
                    help="force the render tier (fails if no build exists)")
    ap.add_argument("--no-render", dest="render", action="store_false",
                    help="static tier only: fast, offline, but does NOT prove a "
                         "diagram or formula renders")
    ap.add_argument("--app", type=Path, help="explicit path to AvalonMarkdown.Test.Desktop.exe")
    ap.add_argument("--configuration", default="auto", choices=["auto", "Debug", "Release"])
    ap.add_argument("--repo", type=Path, default=REPO_ROOT)
    ap.add_argument("--settle-ms", type=int, default=20000)
    ap.add_argument("--timeout", type=int, default=120,
                    help="harness process timeout; MUST exceed the harness watchdog")
    ap.add_argument("--theme", default="dark", choices=["dark", "light"])
    ap.add_argument("--repeat", type=int, default=1, help="render each file N times (flake triage)")
    ap.add_argument("--plantuml", dest="plantuml", action="store_true", default=True,
                    help="verify PlantUML by asking the server (default)")
    ap.add_argument("--no-plantuml", dest="plantuml", action="store_false")
    ap.add_argument("--plantuml-timeout", type=int, default=15)
    ap.add_argument("--plantuml-retries", type=int, default=2,
                    help="retries on a refused/timed-out request; a real HTTP answer "
                         "is never retried (default 2)")
    ap.add_argument("--plantuml-server", default=PLANTUML_SERVER,
                    help=f"override the render host (default {PLANTUML_SERVER})")
    ap.add_argument("--plantuml-cache", type=Path, default=None,
                    help="where to remember verdicts (default <repo>/.render-check/"
                         "plantuml-cache.json); a diagram's verdict is stable, so a "
                         "warm cache makes repeat runs offline")
    ap.add_argument("--no-plantuml-cache", dest="no_plantuml_cache", action="store_true")
    ap.add_argument("--json", action="store_true", help="emit the machine report on stdout")
    ap.add_argument("--json-out", type=Path)
    ap.add_argument("--quiet", action="store_true")
    ap.add_argument("--self-test", action="store_true",
                    help="assert every check fires against fixtures/")
    args = ap.parse_args(argv)

    if args.plantuml_cache is None:
        args.plantuml_cache = args.repo / ".render-check" / "plantuml-cache.json"
    args.plantuml_cache_enabled = not args.no_plantuml_cache
    if args.plantuml_cache_enabled:
        _puml_cache.update(_puml_cache_read(args.plantuml_cache))

    try:
        return _run(args, ap)
    finally:
        # Written even on the failure paths: a verdict already obtained is worth
        # keeping, and an outage must not discard the warm entries.
        if args.plantuml_cache_enabled and _puml_cache:
            _puml_cache_write(args.plantuml_cache, _puml_cache)


def _run(args, ap) -> int:
    if args.self_test:
        return self_test(args)

    if not args.files:
        ap.error("no input files")

    app = args.app or find_app(args.repo, args.configuration)
    run_render = (args.render is True) or (args.render is None and app is not None)

    if args.render is True and app is None:
        print(f"error: --render requested but no build found under {args.repo}", file=sys.stderr)
        return 3

    results = []
    worst = 0
    for md in args.files:
        if not md.exists():
            print(f"error: {md} does not exist", file=sys.stderr)
            return 2
        res = analyse(md, args)
        if run_render:
            for attempt in range(max(1, args.repeat)):
                out_dir = args.repo / ".render-check" / f"{md.stem}-{args.theme}-{os.getpid()}-{attempt}"
                report, err = run_render_tier(app, md, out_dir, args.settle_ms,
                                              args.theme, args.timeout)
                if report is None:
                    res["render_error"] = err
                    break
                res["render"] = report
        results.append(res)

    if not args.quiet:
        for res in results:
            print_report(res, run_render)
    if args.json or args.json_out:
        doc = {"schema": "avalonmarkdown.render-check-cli/1", "files": results,
               "render_available": app is not None, "render_ran": run_render}
        blob = json.dumps(doc, indent=2, ensure_ascii=False)
        if args.json:
            print(blob)
        if args.json_out:
            args.json_out.write_text(blob, encoding="utf-8")

    for res in results:
        if res["render_error"]:
            worst = max(worst, 3)
            continue
        fails = [f for f in res["findings"] if f["severity"] == FAIL]
        envs = [f for f in res["findings"] if f["severity"] == ENV]
        r = res["render"]
        if r:
            fails += [f for f in r.get("findings", []) if f.get("severity") == FAIL]
            envs += [f for f in r.get("findings", []) if f.get("severity") == ENV]
            if r.get("status") == "environment":
                envs.append(r)
        if fails:
            worst = max(worst, 1)
        elif envs:
            worst = max(worst, 3)
    return worst


def print_report(res: dict, render_requested: bool) -> None:
    print(f"\n=== {res['file']} ===")
    c = res["counts"]
    print(f"  blocks: {c['mermaid']} mermaid, {c['plantuml']} plantuml, "
          f"{c['plot']} plot, {c['chat']} chat, {c['code']} code")

    hard = [f for f in res["findings"] if f["severity"] == FAIL]
    soft = [f for f in res["findings"] if f["severity"] != FAIL]
    for f in hard + soft:
        tag = {"fail": "FAIL", "warn": "warn", "environment": "env "}[f["severity"]]
        loc = f"line {f['line']}" if f["line"] else "-"
        print(f"  [{tag}] {loc:>10}  {f['id']}: {f['message']}")
        if f.get("sample"):
            print(f"           sample: {f['sample']}")

    r = res["render"]
    if res["render_error"]:
        print(f"  render tier: FAILED - {res['render_error']}")
    elif r:
        print(f"  render tier: status={r.get('status')} "
              f"findings={len(r.get('findings', []))}")
        for f in r.get("findings", []):
            tag = {"fail": "FAIL", "warn": "warn", "environment": "env "}.get(
                f.get("severity"), "?   ")
            print(f"  [{tag}] {f.get('id')}: {f.get('message')}")
            if f.get("sample"):
                print(f"           sample: {str(f['sample'])[:160]}")
    elif not render_requested:
        print("  render tier: SKIPPED (--no-render)")
        print("  WARNING: diagrams and formulas were NOT checked for renderability.")
        print("           The static tier proves structure, not that anything draws.")
    else:
        print("  render tier: SKIPPED (no build found)")
        print("  WARNING: diagrams and formulas were NOT checked for renderability.")
        print("           Run `dotnet build` first, then re-run without --no-render.")

    if res["unvalidated"]:
        print("  not validated:")
        for u in res["unvalidated"]:
            print(f"    - {u}")

    ok = not hard and not (r and [f for f in r.get("findings", []) if f.get("severity") == FAIL])
    suffix = "" if r else " (static only)"
    print(f"  RESULT: {'PASS' if ok else 'FAIL'}{suffix}")


# --------------------------------------------------------------------------
# self-test
# --------------------------------------------------------------------------


# Every check this tool claims to perform, pinned to the fixture that must set it
# off. A validator with no known-bad fixture is not trustworthy: without this the
# tool could be silently checking nothing and still reporting a clean pass.
SELF_TEST_EXPECTATIONS = {
    # static ids, render ids, plantuml-server ids
    "broken.md": {
        "static": ["plot-invalid-json", "plot-expression-rejected", "info-near-miss",
                   "chat-info-malformed", "chat-unknown-option", "chat-svg-empty"],
        "render": ["katex-error", "mermaid-render-error", "plot-error", "chat-error"],
        "plantuml": ["plantuml-rejected"],
        # The deliberately-broken plot has no artefact, so its two export attempts
        # (svg + png) are skipped. The deliberately-broken PlantUML image DOES load
        # (a rejected diagram comes back as a renderable error picture), so its two
        # exports get far enough to fail fetching the SVG -- which is correct: the
        # export refuses rather than emitting an error picture as if it were the
        # diagram. Which of the two happens depends on plantuml.com, so the pinned
        # invariant is "must not succeed", not "must fail".
        # Two plot fences, each attempted in two formats.
        "exports_skip": {"plot": 4},
        "exports_not_succeed": {"puml": 2},
    },
    "broken-swallow-math.md": {"static": ["math-block-unterminated"]},
    "broken-swallow-fence.md": {"static": ["unclosed-fence"]},
    "broken-static.md": {"static": ["math-inline-empty-dollar", "math-inline-unclosed"]},
}


BLACKISH = {"black", "#000", "#000000", "rgb(0, 0, 0)", "rgb(0,0,0)"}


def export_problems(report: dict, forbid_skips: bool = False,
                    expect_skip_by_kind: dict | None = None,
                    expect_not_succeed_by_kind: dict | None = None
                    ) -> tuple[list[str], list[str]]:
    """Check the export audit a render-tier report carries.

    The export path's nastiest failure is invisible: a canvas that drew nothing,
    or one past the engine's area ceiling, returns a valid PNG data URL and throws
    nothing. Hence the ink measurement and the PNG magic-byte check.

    NOTE on the PlantUML assertions: a *broken* PlantUML diagram is not
    deterministic. If its image fails to load the export is skipped; if the image
    loads (plantuml.com answers a syntax error with a renderable error picture)
    the export gets far enough to fail fetching the SVG. Which one happens depends
    on the third-party service, so the invariant that actually holds is "must not
    succeed" rather than "must fail".

    Returns (problems, notes). Notes record assertions relaxed because the
    environment was degraded — a green run must not hide what went unverified.
    """
    ex = report.get("exports")
    if not ex:
        return ["the report carries no exports section"], []
    if ex.get("timedOut"):
        return ["the export audit timed out"], []

    # plantuml.com is a third-party host; if it is unreachable, PlantUML exports
    # cannot be attempted and that is not a defect in the code under test.
    degraded = report.get("status") == "environment"

    problems: list[str] = []
    notes: list[str] = []
    ok_count = 0
    skip_by_kind: dict[str, int] = {}
    fail_by_kind: dict[str, int] = {}
    failures: list[tuple[str, str, str]] = []

    for r in ex.get("results", []):
        label = f"{r.get('kind')}/{r.get('format')}"
        kind = r.get("kind", "?")
        if r.get("skipped"):
            skip_by_kind[kind] = skip_by_kind.get(kind, 0) + 1
            continue
        if not r.get("ok"):
            fail_by_kind[kind] = fail_by_kind.get(kind, 0) + 1
            failures.append((label, kind, r.get("error")))
            continue
        ok_count += 1
        if r.get("format") == "png":
            if not r.get("pngMagic"):
                problems.append(f"{label}: payload is not a PNG")
            ink = r.get("ink")
            if isinstance(ink, (int, float)) and ink <= 0:
                problems.append(f"{label}: the raster is blank (ink={ink})")
        else:
            if not r.get("svgDoc"):
                problems.append(f"{label}: no <svg> in the output")
            if not r.get("svgProlog"):
                problems.append(f"{label}: missing the XML prolog")
            # The plot's colours come from author CSS. If the computed-style pass
            # did not run they silently revert to the library's hard-coded black,
            # which is invisible against the dark theme.
            for s in r.get("strokeSamples") or []:
                if str(s).lower() in BLACKISH:
                    problems.append(f"{label}: a stroke came out black — the "
                                    "computed-style pass did not run")

    if expect_skip_by_kind:
        for kind, want in expect_skip_by_kind.items():
            got = skip_by_kind.get(kind, 0)
            if got != want:
                problems.append(f"expected {want} skipped {kind} export(s), got {got}")

    expected_unsuccessful = expect_not_succeed_by_kind or {}
    for label, kind, error in failures:
        # A kind pinned as "must not succeed" is allowed to fail -- that IS the
        # expected outcome for a deliberately-broken member.
        if kind in expected_unsuccessful:
            continue
        if kind == "puml":
            # Every PlantUML export fetches from a third-party host, which
            # intermittently drops requests. A failure there says the host did not
            # answer, not that this code is wrong -- so it is reported as
            # unverified. The path still gets a real regression check against the
            # broken fixture, where the outcome is pinned and deterministic.
            notes.append(f"{label}: {error} (third-party fetch - NOT verified)")
            continue
        problems.append(f"{label}: {error}")

    if expect_not_succeed_by_kind:
        for kind, want in expect_not_succeed_by_kind.items():
            got = skip_by_kind.get(kind, 0) + fail_by_kind.get(kind, 0)
            if got != want:
                problems.append(f"expected {want} {kind} export(s) to not succeed, got {got}")

    if forbid_skips and skip_by_kind:
        total = sum(skip_by_kind.values())
        if degraded:
            notes.append(f"{total} export(s) skipped while the environment was degraded "
                         "— NOT verified")
        else:
            problems.append(f"{total} export(s) skipped unexpectedly: {skip_by_kind}")

    # The context-menu wiring, driven by a synthetic event. If the handler does not
    # claim the event the platform's own menu appears on top of ours; if the menu
    # never opens, right-click does nothing at all.
    menu = ex.get("menu")
    if not menu:
        # Guarded rather than skipped: a missing probe result would otherwise let
        # the whole menu check pass by not running.
        problems.append("the export audit carries no context-menu probe result")
    else:
        if not menu.get("ok"):
            problems.append(f"context-menu probe failed: {menu.get('reason')}")
        else:
            if not menu.get("defaultPrevented"):
                problems.append("the contextmenu handler did not call preventDefault, "
                                "so the platform menu will appear over ours")
            if not menu.get("menuShown"):
                problems.append("no .md-ctx-menu element appeared for a synthetic "
                                "contextmenu on an exportable member")
            if len(menu.get("items") or []) != 2:
                problems.append(f"expected 2 menu items, got {menu.get('items')}")

    # A run where nothing exported would otherwise pass vacuously.
    if ok_count == 0:
        problems.append("no export succeeded at all")
    return problems, notes


def self_test(args) -> int:
    fixtures = SKILL_DIR / "fixtures"
    app = args.app or find_app(args.repo, args.configuration)
    render_available = app is not None and app_supports_render_check(app)
    can_plantuml = args.plantuml

    print("self-test: asserting every documented check fires\n")
    print(f"  render tier: {'available' if render_available else 'UNAVAILABLE'}"
          f"   plantuml server: {'on' if can_plantuml else 'off'}\n")

    failed = 0

    notes_seen: list[str] = []

    def check(label: str, missing: list[str], detail: str,
              notes: list[str] | None = None) -> None:
        nonlocal failed
        ok = not missing
        print(f"  [{'ok  ' if ok else 'MISS'}] {label}{detail}"
              + (f"   missing={missing}" if missing else ""))
        if not ok:
            failed += 1
        # A relaxed assertion is printed even on success: a green self-test must
        # not hide that part of the surface went unverified.
        for n in (notes or []):
            notes_seen.append(n)
            print(f"         note: {n}")

    for name, want in SELF_TEST_EXPECTATIONS.items():
        path = fixtures / name
        if not path.exists():
            print(f"  MISSING fixture {path}")
            failed += 1
            continue

        res = analyse(path, args)
        static_got = {f["id"] for f in res["findings"] if f["severity"] == FAIL}
        check(f"{name} (static)", [i for i in want.get("static", []) if i not in static_got],
              f": {len(static_got)} static findings")

        if want.get("plantuml"):
            if not can_plantuml:
                print(f"  [skip] {name} (plantuml server) -- disabled")
            else:
                missing_puml = [i for i in want["plantuml"] if i not in static_got]
                if missing_puml and res.get("plantuml_unreachable"):
                    # The host is a third party and does go away. An unanswered
                    # question is not a failed check -- but it must be loud, because
                    # a silent skip would hide a real regression.
                    # Name the server that was actually used: it can be overridden,
                    # and a message pointing at the wrong host sends people the wrong
                    # way.
                    note = f"{name}: {args.plantuml_server} unreachable"
                    notes_seen.append(note)
                    print(f"  [note] {name} (plantuml server): {args.plantuml_server} "
                          "unreachable -- NOT verified")
                else:
                    check(f"{name} (plantuml server)", missing_puml, "")

        if want.get("render"):
            if not render_available:
                print(f"  [skip] {name} (render) -- no render-check build")
                continue
            out_dir = args.repo / ".render-check" / f"selftest-{Path(name).stem}-{os.getpid()}"
            report, err = run_render_tier(app, path, out_dir, args.settle_ms,
                                          args.theme, args.timeout)
            if report is None:
                check(f"{name} (render)", ["<harness failed>"], f": {err}")
                continue
            got = {f["id"] for f in report.get("findings", [])
                   if f.get("severity") == FAIL}
            check(f"{name} (render)",
                  [i for i in want["render"] if i not in got],
                  f": status={report.get('status')}")
            ex_problems, ex_notes = export_problems(
                report,
                expect_skip_by_kind=want.get("exports_skip"),
                expect_not_succeed_by_kind=want.get("exports_not_succeed"))
            check(f"{name} (exports)", ex_problems, "", ex_notes)

    # The positive fixture must be clean in EVERY tier that ran. A false positive
    # here is as damaging as a false negative -- it trains people to ignore the tool.
    good = fixtures / "good.md"
    if not good.exists():
        print("  MISSING fixtures/good.md")
        failed += 1
    else:
        res = analyse(good, args)
        bad = [f["id"] for f in res["findings"] if f["severity"] == FAIL]
        detail = f": {len(bad)} static false positive(s)"
        notes: list[str] = []
        if render_available:
            out_dir = args.repo / ".render-check" / f"selftest-good-{os.getpid()}"
            report, err = run_render_tier(app, good, out_dir, args.settle_ms,
                                          args.theme, args.timeout)
            if report is None:
                bad.append(f"<harness failed: {err}>")
            else:
                bad += [f["id"] for f in report.get("findings", [])
                        if f.get("severity") == FAIL]
                detail = f": status={report.get('status')}"
                # Every member in the positive fixture must export in both formats;
                # nothing here is broken, so nothing may be skipped.
                p2, notes = export_problems(report, forbid_skips=True)
                bad += p2
        check("good.md (no false positives)", bad, detail, notes)

    verdict = "PASS" if not failed else f"FAIL ({failed} problem(s))"
    if notes_seen:
        # Never let a green line stand in for a complete one.
        verdict += (f" - WITH {len(notes_seen)} UNVERIFIED ASSERTION(S); "
                    "the environment was degraded:")
        for n in notes_seen:
            verdict += f"\n    - {n}"
    print(f"\nself-test: {verdict}")
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
