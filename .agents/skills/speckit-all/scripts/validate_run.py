#!/usr/bin/env python3
"""Validate a speckit-all workflow run before final reporting."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


REQUIRED_PHASES = [
    "Phase 1: Feature Specification",
    "Phase 2: Arabic Clarification",
    "Phase 3: Technical Planning",
    "Phase 4: Detailed Task Breakdown",
    "Phase 5: Implementation",
    "Phase 6: Deep Architectural, Code & UI/UX Critique",
    "Phase 7: Clean Code Guard",
    "Phase 8: Test Guard",
    "Phase 9: Feature Tests, Final Verification & Summary Report",
]


def fail(message: str) -> None:
    raise SystemExit(f"FAIL: {message}")


def find_spec_dir(root: Path, explicit: str | None) -> Path:
    if explicit:
        spec_dir = Path(explicit)
        return spec_dir if spec_dir.is_absolute() else root / spec_dir

    candidates = [p for p in (root / "specs").glob("*/plan.md") if p.is_file()]
    if not candidates:
        fail("Could not infer spec directory; pass --spec-dir")
    return max(candidates, key=lambda p: p.stat().st_mtime).parent


def require_file(path: Path) -> str:
    if not path.is_file():
        fail(f"Missing required file: {path}")
    return path.read_text(encoding="utf-8")


def checked_line(text: str, label: str) -> bool:
    escaped = re.escape(label)
    return bool(re.search(rf"^- \[x\] {escaped}", text, re.MULTILINE))


def validate_achievements(root: Path) -> None:
    text = require_file(root / "achievements.md")
    for phase in REQUIRED_PHASES:
        if not checked_line(text, phase):
            fail(f"achievements.md does not mark complete: {phase}")

    unchecked = re.findall(r"^- \[ \] .+", text, flags=re.MULTILINE)
    if unchecked:
        fail("achievements.md still has unchecked items:\n" + "\n".join(unchecked))

    phase7 = text.find("Phase 7: Clean Code Guard")
    phase8 = text.find("Phase 8: Test Guard")
    phase9 = text.find("Phase 9: Feature Tests")
    if phase7 == -1 or phase8 == -1 or phase7 > phase8:
        fail("achievements.md must list Clean Code Guard before Test Guard")
    if phase9 == -1 or phase8 > phase9:
        fail("achievements.md must list Feature Tests after Test Guard")
    if "Feature Test Evidence" not in text and "إثبات اختبارات الفيتشر" not in text:
        fail("achievements.md is missing Feature Test Evidence")
    if "Subagent Evidence" not in text and "إثبات استخدام الوكلاء الفرعيين" not in text:
        fail("achievements.md is missing Subagent Evidence")
    subagent_start = max(text.find("Subagent Evidence"), text.find("إثبات استخدام الوكلاء الفرعيين"))
    if subagent_start != -1:
        subagent_evidence = text[subagent_start:]
        for phase in ["Phase 1", "Phase 2", "Phase 3"]:
            if phase not in subagent_evidence:
                fail(f"Subagent Evidence must mention {phase}")
    evidence_start = max(text.find("Feature Test Evidence"), text.find("إثبات اختبارات الفيتشر"))
    if evidence_start != -1:
        evidence = text[evidence_start:]
        if not re.search(r"^- \[x\] .+", evidence, flags=re.MULTILINE):
            fail("Feature Test Evidence must include checked evidence items")
        if re.search(r"^- \[x\] .*not run", evidence, flags=re.IGNORECASE | re.MULTILINE):
            fail("Feature Test Evidence cannot mark 'not run' items as complete")


def validate_tasks(tasks_text: str) -> None:
    if "Spec Kit Preparation Workflow" not in tasks_text:
        fail("tasks.md is missing the Spec Kit Preparation Workflow section")

    for label in [
        "Phase 1: Feature Specification",
        "Phase 2: Arabic Clarification",
        "Phase 3: Technical Planning",
        "Phase 4: Detailed Task Breakdown",
    ]:
        if not checked_line(tasks_text, label):
            fail(f"tasks.md preparation log does not mark complete: {label}")

    clean_idx = tasks_text.find("clean-code-guard")
    test_idx = tasks_text.find("test-guard")
    feature_tests_idx = tasks_text.lower().find("feature tests")
    if clean_idx == -1:
        fail("tasks.md is missing a clean-code-guard tail task")
    if test_idx == -1:
        fail("tasks.md is missing a test-guard tail task")
    if clean_idx > test_idx:
        fail("tasks.md must list clean-code-guard before test-guard")
    if feature_tests_idx == -1:
        fail("tasks.md is missing a feature-test/E2E tail task")
    if test_idx > feature_tests_idx:
        fail("tasks.md must list feature tests after test-guard")


def validate_plan_artifacts(root: Path, spec_dir: Path, require_contracts: bool) -> None:
    spec_text = require_file(spec_dir / "spec.md")
    for name in ["plan.md", "tasks.md", "research.md", "data-model.md", "quickstart.md"]:
        require_file(spec_dir / name)

    if "NEEDS CLARIFICATION" in spec_text:
        fail("spec.md still contains NEEDS CLARIFICATION")

    plan_text = require_file(spec_dir / "plan.md")
    if "NEEDS CLARIFICATION" in plan_text:
        fail("plan.md still contains NEEDS CLARIFICATION")

    spec_plan_validator = Path(__file__).with_name("validate_spec_plan_quality.py")
    spec_plan_result = subprocess.run(
        [sys.executable, str(spec_plan_validator), "--spec-dir", str(spec_dir)],
        text=True,
        capture_output=True,
        check=False,
    )
    if spec_plan_result.returncode != 0:
        fail(
            spec_plan_result.stdout.strip()
            or spec_plan_result.stderr.strip()
            or "spec/plan quality validation failed"
        )

    tasks_text = require_file(spec_dir / "tasks.md")
    validate_tasks(tasks_text)
    validator = Path(__file__).with_name("validate_tasks_quality.py")
    result = subprocess.run(
        [sys.executable, str(validator), "--tasks", str(spec_dir / "tasks.md")],
        text=True,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        fail(result.stdout.strip() or result.stderr.strip() or "tasks quality validation failed")

    contracts_dir = spec_dir / "contracts"
    if require_contracts and not contracts_dir.is_dir():
        fail(f"Missing required contracts directory: {contracts_dir}")

    agents = root / "AGENTS.md"
    if agents.is_file():
        agents_text = agents.read_text(encoding="utf-8")
        rel_plan = (spec_dir / "plan.md").relative_to(root).as_posix()
        marker = re.search(
            r"<!-- SPECKIT START -->(.*?)<!-- SPECKIT END -->",
            agents_text,
            flags=re.DOTALL,
        )
        if not marker:
            fail("AGENTS.md is missing SPECKIT markers")
        if rel_plan not in marker.group(1):
            fail(f"AGENTS.md SPECKIT block does not reference {rel_plan}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".", help="Repository root")
    parser.add_argument("--spec-dir", help="Feature spec directory")
    parser.add_argument(
        "--require-contracts",
        action="store_true",
        help="Require specs/<feature>/contracts to exist",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    spec_dir = find_spec_dir(root, args.spec_dir).resolve()
    if not spec_dir.is_dir():
        fail(f"Spec directory does not exist: {spec_dir}")

    validate_achievements(root)
    validate_plan_artifacts(root, spec_dir, args.require_contracts)
    print(f"speckit-all validation passed for {spec_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
