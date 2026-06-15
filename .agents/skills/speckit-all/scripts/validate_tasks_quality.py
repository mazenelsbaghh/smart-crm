#!/usr/bin/env python3
"""Validate that tasks.md is concrete enough for weaker implementers."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


VAGUE_PHRASES = [
    "update the ui",
    "improve ui",
    "add tests",
    "handle errors",
    "update service",
    "fix bugs",
    "clean up",
    "refactor code",
    "make it work",
    "implement feature",
    "adjust logic",
]

PATH_RE = re.compile(r"`?((?:backend|frontend|src|tests|specs|\\.agents|\\.specify)/[^`\\s]+)`?")
CHECKBOX_RE = re.compile(r"^- \[[ xX]\] .+", re.MULTILINE)
COMMAND_RE = re.compile(r"`[^`]*(?:test|pytest|playwright|dotnet|npm|pnpm|yarn|curl)[^`]*`", re.IGNORECASE)


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tasks", required=True, help="Path to tasks.md")
    args = parser.parse_args()

    path = Path(args.tasks).resolve()
    if not path.is_file():
        fail(f"Missing tasks file: {path}")

    text = path.read_text(encoding="utf-8")
    lower = text.lower()

    for phrase in VAGUE_PHRASES:
        if phrase in lower:
            fail(f"tasks.md contains vague phrase: {phrase!r}")

    tasks = CHECKBOX_RE.findall(text)
    if len(tasks) < 5:
        fail("tasks.md has too few checklist tasks")

    path_count = len(PATH_RE.findall(text))
    if path_count < 3:
        fail("tasks.md must include exact file paths for implementation tasks")

    if not COMMAND_RE.search(text):
        fail("tasks.md must include exact verification/test commands")

    required_terms = [
        "clean-code-guard",
        "test-guard",
        "feature tests",
    ]
    for term in required_terms:
        if term not in lower:
            fail(f"tasks.md missing required tail task term: {term}")

    if "expected" not in lower and "result" not in lower and "passes" not in lower:
        fail("tasks.md must describe expected observable outcomes")

    print(f"tasks quality validation passed for {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
