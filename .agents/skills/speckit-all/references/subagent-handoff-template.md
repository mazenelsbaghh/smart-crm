# Subagent Handoff Template

Use these templates only when a subagent facility is available. The main agent remains responsible for user interaction, file writes, validation, and final acceptance.

## Evidence Format

Append this section to `achievements.md` during Phases 1-3:

```markdown
### Subagent Evidence / إثبات استخدام الوكلاء الفرعيين
- [x] Phase 1 specify support: <agent id or "unavailable"> → <summary>
- [x] Phase 2 clarify support: <agent id or "unavailable"> → <summary>
- [x] Phase 3 plan support: <agent id or "unavailable"> → <summary>
```

If subagents are unavailable, write `unavailable` and continue inline. Do not skip the evidence section.

## Phase 1 Specify Support

Prompt:

```text
Analyze the feature request for speckit-specify. Read AGENTS.md and project spec conventions. Return a concise specification context packet only; do not write files.

Inputs:
- Feature request: <paste $ARGUMENTS>
- Repo root: <absolute path>

Required output:
- Suggested short feature name
- Actors and user goals
- Functional requirements candidates
- Edge cases and exclusions
- Data entities, if any
- Testable success criteria
- Open risks that may require Arabic clarification
```

## Phase 2 Clarify Support

Prompt:

```text
Review the current spec.md and propose up to 5 high-impact clarification candidates. Do not ask the user. Draft every question in Arabic.

Inputs:
- spec.md path: <path>

Required output:
- Candidate questions in priority order
- Recommended option for each question with Arabic reasoning
- 2-5 Arabic option descriptions per question
- Which spec section each answer would update
- Any ambiguity that should be deferred to planning
```

## Phase 3 Plan Support

Prompt:

```text
Support speckit-plan. Read the clarified spec.md, AGENTS.md, constitution, and existing project patterns. Return a planning context packet; write files only if explicitly assigned.

Inputs:
- spec.md path: <path>
- constitution path: .specify/memory/constitution.md
- repo root: <absolute path>

Required output:
- Relevant existing files and patterns
- Technical risks and mitigations
- Data model implications
- API/UI contracts needed
- Test strategy and exact command candidates
- Required artifacts checklist: plan.md, research.md, data-model.md, contracts/, quickstart.md, AGENTS.md SPECKIT update
```

## Acceptance Rules

Reject or repair subagent output before proceeding if it:

- contradicts `spec.md`, `AGENTS.md`, or the constitution,
- asks the user directly during Phase 2,
- uses non-Arabic clarification questions,
- omits exact file paths or artifact names,
- introduces unanswered high-impact assumptions,
- skips tests for changed behavior.
