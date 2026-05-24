---
name: "speckit-all"
description: "Execute the complete Spec-Driven Development workflow from specification, planning, task breakdown for cheaper LLMs, implementation, to deep architectural review."
compatibility: "Requires spec-kit project structure with .specify/ directory"
metadata:
  author: "community"
  source: "speckit-all/SKILL.md"
---

## User Input

```text
$ARGUMENTS
```

You **MUST** process the user's input as the feature description to begin the end-to-end workflow.

---

## SDD Automation Workflow

When this skill is triggered, you must execute the following 6 phases sequentially, carrying over all context between them. Do not skip any phase unless explicitly instructed.

### MANDATORY PROGRESS TRACKING (achievements.md)
At the very beginning of the run (before starting Phase 1), you MUST create a progress tracking file named `achievements.md` in the root directory of the workspace. This file must list all the workflow phases as tasks.

**CRITICAL PROGRESS REQUIREMENTS**:
1. You MUST update `achievements.md` immediately after completing each phase, marking that specific phase as completed (`[x]`). Do not wait until the end of the entire run to update it; it must be edited dynamically after every single phase.
2. In the final phase, you MUST verify that all phases (including the core phases and any additional phases/tasks listed in `achievements.md`) have been successfully completed and are marked as completed (`[x]`) in the `achievements.md` file.

The initial structure of `achievements.md` MUST be:
```
# Project Achievements & SDD Phase Progress / الإنجازات وتقدم المراحل

- [ ] Phase 1: Feature Specification (`speckit-specify`)
- [ ] Phase 2: Technical Planning (`speckit-plan`)
- [ ] Phase 3: Detailed Task Breakdown (`speckit-tasks`)
- [ ] Phase 4: Implementation (`speckit-implement`)
- [ ] Phase 5: Deep Architectural, Code & UI/UX Critique
- [ ] Phase 6: Final Verification & Summary Report
```

### Phase 1: Feature Specification (`speckit-specify`)
1. Create/initialize the `achievements.md` file in the root of the project with all phases unchecked (`[ ]`).
2. Extract the feature description from `$ARGUMENTS`.
3. Execute the **`speckit-specify`** skill to draft the feature specifications.
4. Resolve requirements and edge cases using reasonable defaults to keep the specification concrete and clear.
5. Create the specification directory under `specs/` and save `spec.md` and the validation checklist.
6. **Progress Update**: Mark Phase 1 as completed (`[x]`) in `achievements.md` before proceeding.

### Phase 2: Technical Planning (`speckit-plan`)
1. Take the generated `spec.md` and analyze the technical needs of the project.
2. Execute the **`speckit-plan`** skill to create a robust, secure, and DRY technical implementation plan (`plan.md`).
3. Ensure the plan conforms to the project's `.specify/memory/constitution.md` principles.
4. **Mandatory Design reference**: You MUST use and apply the guidelines of **`ui-ux-pro-max-skill`** and **`impeccable`** to ensure high-fidelity styling, UX heuristics, and pixel-perfect layout planning.
5. **Progress Update**: Mark Phase 2 as completed (`[x]`) in `achievements.md` before proceeding.

### Phase 3: Detailed Task Breakdown for Cheaper LLMs (`speckit-tasks`)
1. Execute the **`speckit-tasks`** skill to generate the `tasks.md` file.
2. **CRITICAL REQUIREMENT**: You must generate the task breakdown with the target prompt:
   > "create the tasks file so that a cheaper llm model can implement without problems"
3. **SPEC KIT COMPLETION LOG**: In the generated `tasks.md` file, you MUST prepend a "Spec Kit Preparation Workflow" section showing that Phase 1 (Feature Specification), Phase 2 (Technical Planning), and Phase 3 (Detailed Task Breakdown) are completed and checked off (`[x]`). This prevents downstream agents and tools from attempting to re-execute planning or specification steps.
4. Verify that the task checklist conforms to the checklist formatting rules.
5. To fulfill this, your task breakdown must:
   - Be extremely atomic: Break down complex operations into single-file, single-function, or single-rule edits.
   - Be self-contained: Each task must specify the exact file path, the required modification, and the expected code structure.
   - Leave zero ambiguity: Do not write vague tasks like "update the UI" or "add logging". Instead, write "In file `src/index.js`, add a try-catch block to the login function and log the error message using `console.error`."
   - Explicitly define checkpoints and testing commands for every user story.
6. **Progress Update**: Mark Phase 3 as completed (`[x]`) in `achievements.md` before proceeding.

### Phase 4: Implementation (`speckit-implement`)
1. Execute the **`speckit-implement`** skill.
2. Systematically execute the tasks defined in `tasks.md` sequentially.
3. Verify that each task compiles/runs correctly as you proceed.
4. **RECORD & RESOLVE WARNINGS**: If you encounter any compiler warnings, build issues, lint warnings, or test failures during this phase:
   - You MUST immediately record them in a new `### Warnings and Issues / تحذيرات ومشاكل` section at the end of the `achievements.md` file as unchecked checkboxes (`- [ ]`).
   - You MUST also add them to the end of the `tasks.md` checklist as new unchecked items.
   - You MUST systematically fix every warning/issue, checking them off (`[x]`) in both `achievements.md` and `tasks.md` only when they are fully resolved and verified.
5. **Progress Update**: Mark Phase 4 as completed (`[x]`) in `achievements.md` before proceeding.

### Phase 5: Deep Architectural, Code & UI/UX Critique
1. Once implementation is completed and verified, you must trigger a deep review and critique phase with the prompt:
   > "The cheaper llm model implemented the, tasks. We need a deep review of the implementation"
2. Conduct a thorough and deep critique (انتقاد ديب) of the codebase:
   - **Backend**: Audit for unhandled exceptions, correct error response codes, security vulnerabilities (validation, sanitization), correct database transactions, and proper layering.
   - **Frontend**: Check for state management correctness, proper async handler error catches, rendering efficiency, and optimal component structures.
   - **UI & UX**: Verify pixel-perfect layouts, responsive design consistency, correct styling tokens, smooth transitions/hover states, accessibility (contrast, labels), and prevention of layout shifts. **You MUST run and apply the specific critique rules from `ui-ux-pro-max-skill` and `impeccable` to audit and align the UI/UX implementation.**
   - Check all modified and created files against the specification (`spec.md`) and plan (`plan.md`).
   - Audit the code for DRY principles (ensure no duplicated logic) and security requirements (verify inputs are sanitized, secrets are not exposed).
3. **RECORD & RESOLVE CRITIQUE FINDINGS**: If any issues, errors, warnings (in compilation, tests, console outputs, or code audits), bugs, or UX/UI design flaws are found during critique:
   - You MUST immediately record them in a new `### Critique & Architectural Issues / مشاكل الانتقاد والبنية` section at the end of the `achievements.md` file as unchecked checkboxes (`- [ ]`).
   - You MUST also add them to the end of the `tasks.md` checklist as new unchecked items.
   - You MUST systematically correct the code, resolve all warnings, and fix the problems across Frontend, Backend, UI, and UX layers.
   - Mark the resolved items as completed (`[x]`) in both `achievements.md` and `tasks.md` only after they are fully resolved and verified.
4. **Progress Update**: Mark Phase 5 as completed (`[x]`) in `achievements.md` before proceeding, and ensure that all recorded warnings, errors, and critique issues from both Phase 4 and Phase 5 have been fully resolved and checked off.

### Phase 6: Final Verification & Summary Report
Before finalizing the run and claiming completion, you MUST perform a final project-wide sweep:

1. **FINAL WHOLE-PROJECT CRITIQUE & BUILD VERIFICATION**:
   - Conduct one final, comprehensive critique of the entire project to ensure absolutely no code smell, styling flaw, regression, or architectural gap exists.
   - Run a full build/compile check of both Frontend and Backend projects. You MUST resolve and eliminate any build warnings or compilation warnings (يحذف ويحل أي warning لأي build في المشروع) to guarantee a clean, warning-free build.
   - Record any final issues/warnings identified during this sweep and their resolutions in the `achievements.md` file under a final section.

2. **Compile and Output the Markdown Report**:
   Once the project is 100% clean and warning-free, compile a report containing:
   - **Summary of Feature**: Short description and specs location.
   - **Implementation Log**: List of files created/modified.
   - **Review Findings**: Quality assessment, plus a list of issues/bugs identified and how they were resolved.
   - **Final Status**: Verification results (tests passed, runtime checks) and overall readiness of the feature.

3. **Progress Update**:
   - Mark the final phase as completed (`[x]`) in `achievements.md`.
   - Verify that all phases (including the core phases and any additional phase checkboxes added dynamically, or by custom workflows, or listed in the file) and all recorded warning/critique checklist items are fully checked off and marked as completed (`[x]`) across the entire file before ending your execution.
