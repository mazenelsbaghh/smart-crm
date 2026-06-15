#!/usr/bin/env python3
"""Extract likely test commands from spec-kit artifacts."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


COMMAND_PATTERNS = [
    re.compile(r"`([^`]*(?:test|pytest|playwright|dotnet|npm run|pnpm|yarn)[^`]*)`"),
    re.compile(r"^\s{0,4}((?:npm|pnpm|yarn|dotnet|pytest|python(?:3)? -m pytest|npx playwright)[^\n]+)$", re.MULTILINE),
]


def commands_from_text(text: str) -> list[str]:
    found: list[str] = []
    for pattern in COMMAND_PATTERNS:
        for match in pattern.findall(text):
            command = match.strip()
            if command and command not in found:
                found.append(command)
    return found


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spec-dir", required=True, help="Feature spec directory")
    args = parser.parse_args()

    spec_dir = Path(args.spec_dir).resolve()
    files = [spec_dir / "tasks.md", spec_dir / "quickstart.md", spec_dir / "plan.md"]
    commands: list[str] = []
    for path in files:
        if not path.is_file():
            continue
        for command in commands_from_text(path.read_text(encoding="utf-8")):
            if command not in commands:
                commands.append(command)

    if not commands:
        print("No explicit test commands found in tasks.md, quickstart.md, or plan.md.")
        return 0

    for command in commands:
        print(command)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
