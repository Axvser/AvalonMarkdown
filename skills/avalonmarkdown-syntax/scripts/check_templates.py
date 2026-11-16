#!/usr/bin/env python3
"""Render every template in references/templates.md and report what breaks.

templates.md is documentation, so its examples are quoted inside four-backtick
fences — which means nothing renders them and a wrong snippet sits there looking
authoritative. This extracts each block, concatenates them, renders the result
through the real pipeline and fails if anything reports an error.

    python check_templates.py            # static only
    python check_templates.py --render   # + the real render tier (default when a build exists)

Exit codes match render_check.py: 0 clean, 1 findings, 2 usage, 3 environment.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import render_check as rc  # noqa: E402  (path set above)

SKILL_DIR = HERE.parent
TEMPLATES = SKILL_DIR / "references" / "templates.md"

# A four-backtick fence tagged `markdown`; the inner three-backtick fences that
# several templates contain are content, not terminators.
_BLOCK = re.compile(r"^````markdown\n(.*?)^````\s*$", re.M | re.S)


def extract(text: str) -> list[str]:
    return [m.group(1) for m in _BLOCK.finditer(text)]


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(prog="check_templates.py")
    ap.add_argument("--render", dest="render", action="store_true", default=None)
    ap.add_argument("--no-render", dest="render", action="store_false")
    ap.add_argument("--app", type=Path)
    ap.add_argument("--configuration", default="auto", choices=["auto", "Debug", "Release"])
    ap.add_argument("--repo", type=Path, default=rc.REPO_ROOT)
    ap.add_argument("--theme", default="dark", choices=["dark", "light"])
    ap.add_argument("--settle-ms", type=int, default=20000)
    ap.add_argument("--timeout", type=int, default=120)
    # render_check.analyse reads these off the namespace.
    ap.add_argument("--plantuml", dest="plantuml", action="store_true", default=True,
                    help="verify the PlantUML template against the server (default)")
    ap.add_argument("--no-plantuml", dest="plantuml", action="store_false")
    ap.add_argument("--plantuml-timeout", type=int, default=15)
    ap.add_argument("--plantuml-retries", type=int, default=2)
    ap.add_argument("--plantuml-server", default=rc.PLANTUML_SERVER)
    ap.add_argument("--plantuml-cache", type=Path, default=None)
    ap.add_argument("--no-plantuml-cache", dest="no_plantuml_cache", action="store_true")
    args = ap.parse_args(argv)

    # Same cache as render_check, so the two tools share one warm set of verdicts.
    if args.plantuml_cache is None:
        args.plantuml_cache = args.repo / ".render-check" / "plantuml-cache.json"
    args.plantuml_cache_enabled = not args.no_plantuml_cache
    if args.plantuml_cache_enabled:
        rc._puml_cache.update(rc._puml_cache_read(args.plantuml_cache))

    if not TEMPLATES.exists():
        print(f"error: {TEMPLATES} is missing", file=sys.stderr)
        return 2

    blocks = extract(TEMPLATES.read_text(encoding="utf-8"))
    if not blocks:
        print("error: no ```markdown blocks found — has the fence style changed?",
              file=sys.stderr)
        return 2

    # Separated by a rule so one template's trailing fence cannot merge with the
    # next one's opening line.
    doc = "\n\n---\n\n".join(blocks) + "\n"

    with tempfile.TemporaryDirectory(prefix="avalon-templates-") as tmp:
        path = Path(tmp) / "templates.md"
        path.write_text(doc, encoding="utf-8", newline="\n")

        print(f"templates: {len(blocks)} blocks, {len(doc)} bytes\n")

        res = rc.analyse(path, args)

        app = args.app or rc.find_app(args.repo, args.configuration)
        run_render = (args.render is True) or (args.render is None and app is not None
                                               and rc.app_supports_render_check(app))

        problems = [f for f in res["findings"] if f["severity"] == rc.FAIL]
        for f in problems:
            print(f"  [FAIL] line {f['line']:>4}  {f['id']}: {f['message']}")
            if f.get("sample"):
                print(f"           sample: {f['sample']}")

        if run_render:
            out_dir = args.repo / ".render-check" / f"templates-{os.getpid()}"
            report, err = rc.run_render_tier(app, path, out_dir, args.settle_ms,
                                             args.theme, args.timeout)
            if report is None:
                print(f"  render tier: SKIPPED ({err})")
            else:
                for f in report.get("findings", []):
                    if f.get("severity") == rc.FAIL:
                        problems.append(f)
                        print(f"  [FAIL] render  {f['id']}: {f['message'][:150]}")
                print(f"  render tier: status={report.get('status')} "
                      f"rendered: {report.get('counts', {})}")
        else:
            print("  render tier: SKIPPED — diagram and formula rendering NOT verified")

    if args.plantuml_cache_enabled and rc._puml_cache:
        rc._puml_cache_write(args.plantuml_cache, rc._puml_cache)

    print(f"\nRESULT: {'PASS' if not problems else f'FAIL ({len(problems)} finding(s))'}")
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
