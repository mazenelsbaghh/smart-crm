#!/usr/bin/env python3
"""Create the speckit-all achievements tracker."""

from __future__ import annotations

import argparse
from pathlib import Path


TEMPLATE = """# Project Achievements & SDD Phase Progress / الإنجازات وتقدم المراحل

- [ ] Phase 1: Feature Specification (`speckit-specify`)
- [ ] Phase 2: Arabic Clarification (`speckit-clarify`)
- [ ] Phase 3: Technical Planning (`speckit-plan`)
- [ ] Phase 4: Detailed Task Breakdown (`speckit-tasks`)
- [ ] Phase 5: Implementation (`speckit-implement`)
- [ ] Phase 6: Deep Architectural, Code & UI/UX Critique
- [ ] Phase 7: Clean Code Guard (`clean-code-guard`)
- [ ] Phase 8: Test Guard (`test-guard`)
- [ ] Phase 9: Feature Tests, Final Verification & Summary Report
"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".", help="Repository root")
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite an existing achievements.md file",
    )
    args = parser.parse_args()

    path = Path(args.root).resolve() / "achievements.md"
    if path.exists() and not args.force:
        print(f"achievements.md already exists: {path}")
        return 0

    path.write_text(TEMPLATE, encoding="utf-8")
    print(f"created {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
