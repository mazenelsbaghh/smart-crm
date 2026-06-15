#!/usr/bin/env python3
"""Mark a speckit-all phase as complete in achievements.md."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("phase", type=int, choices=range(1, 10), help="Phase number")
    parser.add_argument("--root", default=".", help="Repository root")
    args = parser.parse_args()

    path = Path(args.root).resolve() / "achievements.md"
    if not path.exists():
        raise SystemExit(f"Missing achievements.md at {path}")

    text = path.read_text(encoding="utf-8")
    pattern = re.compile(rf"^- \[ \] Phase {args.phase}:", re.MULTILINE)
    updated, count = pattern.subn(f"- [x] Phase {args.phase}:", text, count=1)
    if count == 0:
        checked = re.search(rf"^- \[x\] Phase {args.phase}:", text, re.MULTILINE)
        if checked:
            print(f"Phase {args.phase} already marked complete")
            return 0
        raise SystemExit(f"Could not find unchecked Phase {args.phase} in {path}")

    path.write_text(updated, encoding="utf-8")
    print(f"marked Phase {args.phase} complete in {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
