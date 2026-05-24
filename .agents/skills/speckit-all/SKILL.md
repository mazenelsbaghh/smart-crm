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

### Phase 1: Feature Specification (`speckit-specify`)
1. Extract the feature description from `$ARGUMENTS`.
2. Execute the **`speckit-specify`** skill to draft the feature specifications.
3. Resolve requirements and edge cases using reasonable defaults to keep the specification concrete and clear.
4. Create the specification directory under `specs/` and save `spec.md` and the validation checklist.

### Phase 2: Technical Planning (`speckit-plan`)
1. Take the generated `spec.md` and analyze the technical needs of the project.
2. Execute the **`speckit-plan`** skill to create a robust, secure, and DRY technical implementation plan (`plan.md`).
3. Ensure the plan conforms to the project's `.specify/memory/constitution.md` principles.

### Phase 3: Detailed Task Breakdown for Cheaper LLMs (`speckit-tasks`)
1. Execute the **`speckit-tasks`** skill to generate the `tasks.md` file.
2. **CRITICAL REQUIREMENT**: You must generate the task breakdown with the target prompt:
   > "Create the tasks file so that a cheaper LLM model can implement it without problems."
3. To fulfill this, your task breakdown must:
   - Be extremely atomic: Break down complex operations into single-file, single-function, or single-rule edits.
   - Be self-contained: Each task must specify the exact file path, the required modification, and the expected code structure.
   - Leave zero ambiguity: Do not write vague tasks like "update the UI" or "add logging". Instead, write "In file `src/index.js`, add a try-catch block to the login function and log the error message using `console.error`."
   - Explicitly define checkpoints and testing commands for every user story.

### Phase 4: Implementation (`speckit-implement`)
1. Execute the **`speckit-implement`** skill.
2. Systematically execute the tasks defined in `tasks.md` sequentially.
3. Verify that each task compiles/runs correctly as you proceed.

### Phase 5: Deep Architectural & Code Review
1. Once implementation is completed, you must trigger a deep review phase with the prompt:
   > "The cheaper llm model implemented the tasks. We need a deep review of the implementation"
2. Conduct a thorough audit of the code:
   - Check all modified and created files against the specification (`spec.md`) and plan (`plan.md`).
   - Look for common coding bugs, syntax issues, unhandled exceptions, or missing edge cases.
   - Audit the code for DRY principles (ensure no duplicated logic) and security requirements (verify inputs are sanitized, secrets are not exposed).
   - Ensure styling and layout comply with the UI/UX design rules.
3. **If any issues, errors, or discrepancies are found**:
   - Automatically correct the code and fix the problems.
   - Run tests or verify the fixes locally.

### Phase 6: Final Verification & Summary Report
Compile and output a markdown report summarizing the entire run:
1. **Summary of Feature**: Short description and specs location.
2. **Implementation Log**: List of files created/modified.
3. **Review Findings**:
   - Quality assessment of the implementation.
   - List of issues/bugs identified and how they were resolved.
4. **Final Status**: Verification results (tests passed, runtime checks) and overall readiness of the feature.
