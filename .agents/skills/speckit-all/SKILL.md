---
name: speckit-all
description: Execute the complete Spec-Driven Development workflow from feature specification, mandatory Arabic speckit-clarify, mandatory standalone speckit-plan-driven deep technical planning, strict cheaper-LLM task breakdown, implementation, deep review, clean-code-guard, test-guard, feature test verification, and final reporting. Use when the user invokes $speckit-all or asks for an end-to-end Spec Kit feature workflow; Phase 3 planning must spend the deepest research effort and must be performed by reading and executing speckit-plan, never by inline planning inside speckit-all; always finish by running clean-code-guard before test-guard, then feature tests, before the final report.
---

## User Input

```text
$ARGUMENTS
```

You **MUST** process the user's input as the feature description to begin the end-to-end workflow.

## SDD Automation Workflow

When this skill is triggered, execute the following 9 phases sequentially. Do not skip any phase unless the user explicitly instructs you to stop.

### Bundled Automation Scripts

Use these scripts from this skill folder to make the workflow deterministic:

- `scripts/init_achievements.py --root <repo-root>`: create the required `achievements.md` tracker at the start of Phase 1.
- `scripts/mark_phase.py <1-9> --root <repo-root>`: mark each phase complete immediately after it finishes.
- `scripts/extract_test_commands.py --spec-dir <specs/feature-dir>`: list likely test commands from `tasks.md`, `quickstart.md`, and `plan.md` before Phase 9 execution.
- `scripts/validate_spec_plan_quality.py --spec-dir <specs/feature-dir>`: reject unresolved or vague spec/plan artifacts before task generation.
- `scripts/validate_tasks_quality.py --tasks <specs/feature-dir/tasks.md>`: reject vague task files before implementation.
- `scripts/validate_run.py --root <repo-root> --spec-dir <specs/feature-dir>`: verify final artifacts, progress checklists, clarification/planning artifacts, AGENTS.md plan reference, feature-test evidence, and quality-gate ordering before the final report.

Read `references/subagent-handoff-template.md` before Phases 1-3 when subagents are available.
Read `references/feature-test-matrix.md` during Phase 9 before building the final feature test matrix.

### Mandatory Order

1. `speckit-specify`
2. `speckit-clarify` immediately after specify and before plan
3. `speckit-plan`
4. `speckit-tasks`
5. `speckit-implement`
6. Deep critique and fixes
7. `clean-code-guard`
8. `test-guard`
9. Final feature tests, build verification, and report

`clean-code-guard` MUST run before `test-guard`. Feature tests MUST run after implementation, critique fixes, clean-code-guard, and test-guard. The final report MUST NOT be written until all phases and all dynamic issue checkboxes are checked.

### Mandatory Speckit-Plan Delegation

Phase 3 is a strict handoff to `speckit-plan`, not a planning shortcut inside this skill.

- Before Phase 3 work begins, read the active `speckit-plan/SKILL.md` from disk completely and follow its workflow exactly.
- Do not write, patch, summarize, or "fill in" `plan.md`, `research.md`, `data-model.md`, `contracts/`, or `quickstart.md` from `speckit-all` unless `speckit-plan` explicitly requires that exact file write as part of its own workflow.
- Treat Phase 3 as the highest-effort phase before implementation. Spend more context gathering, repository inspection, dependency checking, contract tracing, and risk analysis here than in any other pre-implementation phase.
- If the plan appears obvious, still perform the `speckit-plan` setup, load context, research, design, contract, quickstart, agent-context update, and constitution checks. Obvious plans still require evidence.
- If `speckit-plan` cannot run, stop Phase 3, record the blocker in `achievements.md`, and do not continue to `speckit-tasks`.
- Record the executed `speckit-plan` handoff, generated artifact paths, research evidence, and any unresolved blockers in `achievements.md` under `### Phase 3 Speckit-Plan Evidence / إثبات التخطيط`.

### Mandatory Progress Tracking

At the beginning of Phase 1, run:

```bash
python .agents/skills/speckit-all/scripts/init_achievements.py --root .
```

After each phase, run:

```bash
python .agents/skills/speckit-all/scripts/mark_phase.py <phase-number> --root .
```

The initial `achievements.md` structure MUST be:

```markdown
# Project Achievements & SDD Phase Progress / الإنجازات وتقدم المراحل

- [ ] Phase 1: Feature Specification (`speckit-specify`)
- [ ] Phase 2: Arabic Clarification (`speckit-clarify`)
- [ ] Phase 3: Technical Planning (`speckit-plan`)
- [ ] Phase 4: Detailed Task Breakdown (`speckit-tasks`)
- [ ] Phase 5: Implementation (`speckit-implement`)
- [ ] Phase 6: Deep Architectural, Code & UI/UX Critique
- [ ] Phase 7: Clean Code Guard (`clean-code-guard`)
- [ ] Phase 8: Test Guard (`test-guard`)
- [ ] Phase 9: Feature Tests, Final Verification & Summary Report
```

### Strictness For Weaker Implementers

Assume the implementation model is much weaker than you. Every spec, plan, task, and review output MUST remove ambiguity.

- Prefer exact file paths, exact functions/components, exact request/response contracts, exact edge cases, and exact commands.
- Do not write vague tasks such as "improve UI", "add tests", "handle errors", or "update service".
- Every implementation task must be small enough for a weaker model to complete without inference across unrelated files.
- Every user story must have explicit verification commands and expected observable outcomes.
- For frontend work, split work into feature packages/modules, feature components, and shared reusable components. Avoid giant components and duplicated inline styling.
- For backend work, specify validation, authorization, database transaction/concurrency behavior, error responses, and migration expectations.
- For tests, define happy path, permission/access negative path, validation failure path, persistence/state transition path, and regression path when applicable.

### Subagent Usage For Phases 1-3

If a subagent facility is available, use it in Phases 1, 2, and 3 for context gathering, ambiguity discovery, and planning support. If unavailable, continue inline and record that explicitly.

- The main agent MUST own user interaction, Arabic clarification questions, file writes, phase marking, validation, and final acceptance.
- Subagents MUST NOT ask the user questions directly.
- Subagents MUST NOT mark phases complete.
- Subagents MUST NOT overwrite `spec.md`, `plan.md`, `tasks.md`, or `achievements.md` unless the main agent explicitly assigns that exact file ownership.
- Use `references/subagent-handoff-template.md` for the handoff prompts and acceptance checks.
- Record results in `achievements.md` under `### Subagent Evidence / إثبات استخدام الوكلاء الفرعيين`.
- If subagent output is vague, contradicts the spec/constitution, omits artifact paths, asks non-Arabic clarification questions, or leaves high-impact assumptions unresolved, reject or repair it before proceeding.

### Phase 1: Feature Specification (`speckit-specify`)

1. Create `achievements.md` by running `python .agents/skills/speckit-all/scripts/init_achievements.py --root .`.
2. If subagents are available, run a Phase 1 specify-support subagent using `references/subagent-handoff-template.md`; otherwise record `Phase 1 specify support: unavailable`.
3. Execute **`speckit-specify`** using `$ARGUMENTS` and the accepted subagent context packet as the feature description context.
4. Ensure `spec.md` and `checklists/requirements.md` are created under one `specs/<feature>/` directory.
5. Do not proceed if the feature description is empty or the spec directory cannot be identified.
6. Mark Phase 1 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 1 --root .`.

### Phase 2: Arabic Clarification (`speckit-clarify`)

1. Execute **`speckit-clarify`** immediately after Phase 1 and before planning.
2. All clarification questions shown to the user MUST be in Arabic. Option labels may be `A/B/C`, but question text, recommendation reasoning, option descriptions, and answer instructions must be Arabic.
3. If subagents are available, run a Phase 2 clarify-support subagent using `references/subagent-handoff-template.md`; otherwise record `Phase 2 clarify support: unavailable`.
4. Ask up to 5 targeted questions exactly as `speckit-clarify` allows, one question at a time. The main agent asks the user; the subagent only drafts candidates.
5. If no critical ambiguities exist, record that result and continue.
6. Save every accepted answer back into `spec.md` through the `speckit-clarify` workflow.
7. Do not run `speckit-plan` while clarification questions are unanswered.
8. Mark Phase 2 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 2 --root .`.

### Phase 3: Technical Planning (`speckit-plan`)

1. Read the active **`speckit-plan/SKILL.md`** from disk completely. Use that skill as the owner of Phase 3. If the file cannot be read, stop and record the blocker.
2. Take the clarified `spec.md` and pass it as the planning input/context for **`speckit-plan`**.
3. If subagents are available, run a Phase 3 plan-support subagent using `references/subagent-handoff-template.md`; otherwise record `Phase 3 plan support: unavailable`.
4. Use the longest and most careful research budget in this phase. Inspect the repository structure, existing implementations, related tests, API/service contracts, database schema/migrations, UI patterns, deployment/runtime constraints, and known risks before finalizing technical decisions.
5. **MANDATORY SKILL HANDOFF**: Execute standalone **`speckit-plan`** and follow its `SKILL.md` workflow exactly to create `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`, and the AGENTS.md plan reference update.
6. `speckit-all` MUST NOT perform Phase 3 planning inline, recreate the plan workflow itself, or write `plan.md` without applying `speckit-plan`.
7. `research.md` MUST include concrete decisions with rationale and alternatives for every unknown, dependency, integration point, external API, data model change, authorization rule, UI architecture choice, test strategy, and deployment/migration risk relevant to the feature.
8. `plan.md` MUST include exact implementation scope, exact files/modules likely to change, contracts, data persistence implications, failure modes, test commands, and rollout/verification notes. Do not accept generic statements.
9. Ensure the plan conforms to `.specify/memory/constitution.md`.
10. If UI is touched, pass `ui-ux-pro-max-skill` and `impeccable` expectations into the `speckit-plan` planning context.
11. Before leaving Phase 3, verify that the `speckit-plan` output generated or updated all required artifacts and that `AGENTS.md` contains the current plan reference between the Spec Kit markers.
12. Run `python .agents/skills/speckit-all/scripts/validate_spec_plan_quality.py --spec-dir <specs/feature-dir>` and fix every failure before task generation.
13. Mark Phase 3 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 3 --root .`.

### Phase 4: Detailed Task Breakdown (`speckit-tasks`)

1. Execute **`speckit-tasks`** with this target prompt:
   > create the tasks file so that a cheaper llm model can implement without problems
2. Prepend a "Spec Kit Preparation Workflow" section to `tasks.md` with Phase 1, Phase 2, Phase 3, and Phase 4 checked.
3. Tasks MUST be atomic, ordered, self-contained, and include exact file paths and expected code structure.
4. Tasks MUST include explicit tests for every user story and every changed critical workflow.
5. Add final task checklist items requiring this exact order: deep critique fixes, `clean-code-guard`, `test-guard`, feature tests, final build verification.
6. Run `python .agents/skills/speckit-all/scripts/validate_tasks_quality.py --tasks <specs/feature-dir/tasks.md>` and fix every failure before implementation.
7. Mark Phase 4 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 4 --root .`.

### Phase 5: Implementation (`speckit-implement`)

1. Execute **`speckit-implement`** and complete `tasks.md` sequentially.
2. Verify each meaningful group of tasks as you proceed, not only at the end.
3. If any compiler warning, build issue, lint warning, test failure, runtime error, or unclear task is encountered, add it as an unchecked item to both `achievements.md` and `tasks.md`, then fix and check it off only after verification.
4. Mark Phase 5 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 5 --root .`.

### Phase 6: Deep Architectural, Code & UI/UX Critique

1. Trigger the deep review prompt:
   > The cheaper llm model implemented the tasks. We need a deep review of the implementation.
2. Audit backend correctness, authorization, validation, error responses, transaction/concurrency behavior, data migrations, and layering.
3. Audit frontend modularity, component size, state ownership, async error handling, rendering efficiency, accessibility, responsive layout, styling tokens, and UI regressions.
4. Compare changed files against `spec.md`, `plan.md`, and `tasks.md`.
5. Record every finding in both `achievements.md` and `tasks.md`; fix, verify, and check off every item.
6. Mark Phase 6 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 6 --root .`.

### Phase 7: Clean Code Guard (`clean-code-guard`)

1. Execute **`clean-code-guard`** in guard-pass mode against every changed production-code file.
2. Exclude test-only files; they are reviewed by `test-guard`.
3. Resolve and check off every clean-code finding in both `achievements.md` and `tasks.md`.
4. Mark Phase 7 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 7 --root .`.

### Phase 8: Test Guard (`test-guard`)

1. Execute **`test-guard`** against every changed test file.
2. If no test files changed, record: `No changed test files; test-guard reviewed the diff and found no test-code surface to audit.`
3. Resolve and check off every test-guard finding in both `achievements.md` and `tasks.md`.
4. Mark Phase 8 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 8 --root .`.

### Phase 9: Feature Tests, Final Verification & Summary Report

1. Read `references/feature-test-matrix.md`.
2. Run `python .agents/skills/speckit-all/scripts/extract_test_commands.py --spec-dir <specs/feature-dir>` and use the output as the starting test command list.
3. Build a feature test matrix from `spec.md`, `plan.md`, `tasks.md`, `quickstart.md`, and the reference file. It MUST cover:
   - every user story,
   - primary happy paths,
   - permission/access failures,
   - validation failures,
   - edge cases,
   - state transitions and persistence,
   - purchase/payment/entitlement logic when relevant,
   - homework/exam/submission flows when relevant,
   - UI smoke or E2E paths when frontend behavior changed,
   - regression paths for any bug fix.
4. Run the most appropriate tests for the feature: unit, integration/API, E2E/browser, and smoke/runtime checks as applicable. Do not rely only on build or lint when behavior changed.
5. Record the exact test commands and results in `achievements.md` under `### Feature Test Evidence / إثبات اختبارات الفيتشر`.
6. Run full backend and frontend build/compile checks. Resolve build errors and warnings where they are introduced by this feature.
7. Mark Phase 9 complete with `python .agents/skills/speckit-all/scripts/mark_phase.py 9 --root .`.
8. Run `python .agents/skills/speckit-all/scripts/validate_run.py --root . --spec-dir <specs/feature-dir>` and resolve every failure before reporting completion.
9. Final report MUST include summary, spec path, implementation files, clean-code-guard result, test-guard result, feature test matrix, commands run, failures fixed, and final readiness.
