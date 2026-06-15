#!/usr/bin/env python3
"""Validate spec.md and plan.md before task generation/implementation."""

from __future__ import annotations

import argparse
import re
from pathlib import Path


FORBIDDEN_MARKERS = [
    "NEEDS CLARIFICATION",
    "[TODO",
    "TODO:",
    "TBD",
    "to be decided",
    "robust",
    "intuitive",
    "user-friendly",
]

SPEC_REQUIRED = [
    "User Scenarios",
    "Functional Requirements",
    "Success Criteria",
]

PLAN_REQUIRED = [
    "Technical Context",
    "Constitution Check",
    "Phase 0",
    "Phase 1",
]

MEASURABLE_RE = re.compile(
    r"\b(\d+%|\d+\s*(?:seconds?|minutes?|ms|users?|requests?|items?))\b",
    re.IGNORECASE,
)


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def require_markers(text: str, markers: list[str], file_name: str) -> None:
    for marker in markers:
        if marker not in text:
            fail(f"{file_name} missing required section/content: {marker}")


def reject_forbidden(text: str, file_name: str) -> None:
    lower = text.lower()
    for marker in FORBIDDEN_MARKERS:
        if marker.lower() in lower:
            fail(f"{file_name} contains unresolved or vague marker: {marker}")


def validate_spec(spec: Path) -> None:
    text = spec.read_text(encoding="utf-8")
    require_markers(text, SPEC_REQUIRED, "spec.md")
    reject_forbidden(text, "spec.md")
    if "- [ ]" in text:
        fail("spec.md must not contain unchecked checklist items")
    if not re.search(r"\bFR-\d+\b", text):
        fail("spec.md must contain numbered functional requirements such as FR-001")
    if not MEASURABLE_RE.search(text):
        fail("spec.md success criteria must include measurable outcomes")


def validate_plan(plan: Path) -> None:
    text = plan.read_text(encoding="utf-8")
    require_markers(text, PLAN_REQUIRED, "plan.md")
    reject_forbidden(text, "plan.md")
    if "research.md" not in text and "Research" not in text:
        fail("plan.md must reference research decisions")
    if "data-model.md" not in text and "Data Model" not in text:
        fail("plan.md must reference data model output")
    if "quickstart.md" not in text and "Quickstart" not in text:
        fail("plan.md must reference quickstart/verification output")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spec-dir", required=True, help="Feature spec directory")
    args = parser.parse_args()

    spec_dir = Path(args.spec_dir).resolve()
    spec = spec_dir / "spec.md"
    plan = spec_dir / "plan.md"
    if not spec.is_file():
        fail(f"Missing spec.md: {spec}")
    if not plan.is_file():
        fail(f"Missing plan.md: {plan}")

    validate_spec(spec)
    validate_plan(plan)
    print(f"spec/plan quality validation passed for {spec_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
