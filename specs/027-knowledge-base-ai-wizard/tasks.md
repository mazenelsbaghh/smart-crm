# Tasks: Knowledge Base AI Wizard

**Input**: Design documents from `/specs/027-knowledge-base-ai-wizard/`

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel
- **[Story]**: US1 (Navbar & Entry), US2 (Arabic Stepper Clarification), US3 (Q&A Generation & Chunking)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure.

- [ ] T001 Initialize the task file checklist.
- [ ] T002 Verify git branch is set to `027-knowledge-base-ai-wizard`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure setup.

- [ ] T003 Compile existing code to verify build passes using `dotnet build` in `backend/` and `npm run build` in `frontend/`.
- [ ] T004 Confirm Gemini configuration variables are loaded and verify the configuration output.
- [ ] T005 Verify the existing pgvector migration is present in the database schemas.

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Navbar & Entry Page Setup (Priority: P1)

**Goal**: Add Sidebar navigation to Knowledge Base page and prepare the page view.

**Expected Result**: Clicking the sidebar link successfully routes to `/management/knowledge` and displays the page title and description.

### Implementation

- [ ] T006 [P] [US1] Add `BookOpen` navigation item in `frontend/src/packages/inbox/shared/ThinSidebar.tsx` referencing route `/management/knowledge`.
- [ ] T007 [US1] Add "معالج الذكاء الاصطناعي" button in `frontend/src/packages/management/KnowledgeBase.tsx` next to the manual text upload button.
- [ ] T008 [US1] Define state in `frontend/src/packages/management/KnowledgeBase.tsx` to handle when the AI Wizard is active.
- [ ] T009 [US1] Run a build check on the frontend using `npm run build` inside `frontend/` to verify there are no routing or compilation errors.

**Checkpoint**: User Story 1 complete. The Knowledge Base sidebar navigation and wizard launch button are fully operational.

---

## Phase 4: User Story 2 - Arabic AI Clarification Wizard (Priority: P1)

**Goal**: Request clarifying questions from Gemini API and display them in a step-by-step interactive stepper.

**Expected Result**: Clicking the AI Wizard button, pasting text, and successfully navigating through generated clarifying questions.

### Implementation

- [ ] T010 [P] [US2] Create endpoint `POST api/projects/{projectId}/knowledge/wizard/analyze` in `backend/src/Modules/Brain/API/KnowledgeBaseController.cs`.
- [ ] T011 [US2] Implement the Gemini prompt in `backend/src/Modules/Brain/API/KnowledgeBaseController.cs` to return a JSON array of Arabic questions, each with exactly 3 suggested answers.
- [ ] T012 [US2] Add the Stepper UI modal/panel in `frontend/src/packages/management/KnowledgeBase.tsx` to render the questions.
- [ ] T013 [US2] Bind the options select and custom answer text inputs for each step in the wizard in `frontend/src/packages/management/KnowledgeBase.tsx`.
- [ ] T014 [US2] Implement transition animations for step-by-step progress using CSS transitions.
- [ ] T015 [US2] Integrate error messages in the wizard UI for failed API calls or empty inputs. Verification is done by running `dotnet build` to ensure the new API builds successfully.

**Checkpoint**: User Story 2 complete. The clarifying interview works.

---

## Phase 5: User Story 3 - Q&A Generation & Boundary Chunking (Priority: P1)

**Goal**: Send raw text and answers to the backend, receive Q&A pairs, review them, save them, and chunk them boundary-safely in the database.

**Expected Result**: Finish the wizard, review the generated Q&As, click Save, and check that pgvector chunks split clean at Q&A boundaries.

### Implementation

- [ ] T016 [P] [US3] Create endpoint `POST api/projects/{projectId}/knowledge/wizard/generate` in `backend/src/Modules/Brain/API/KnowledgeBaseController.cs`.
- [ ] T017 [US3] Implement the Gemini prompt in `backend/src/Modules/Brain/API/KnowledgeBaseController.cs` to compile raw text + answers into structured Q&A pairs.
- [ ] T018 [US3] Render the generated Q&A list in a review step in `frontend/src/packages/management/KnowledgeBase.tsx` allowing add, edit, and delete operations.
- [ ] T019 [US3] Send finalized Q&As to backend save endpoint, formatting them as a structured string (`س:` and `ج:`).
- [ ] T020 [US3] Update `GenerateChunksAndEmbeddingsAsync` in `backend/src/Modules/Brain/Services/KnowledgeBaseService.cs` to detect Q&A prefix patterns and chunk boundary-safely.
- [ ] T021 [US3] Verify that the pgvector embedding is generated for each chunk.
- [ ] T022 [US3] Check that database queries match whole Q&A chunks instead of breaking mid-sentence. Verify using `dotnet test` if tests are present.

**Checkpoint**: User Story 3 complete. Full Q&A wizard flow is operational.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Expected Result**: The UI transitions look smooth and localization is correct.

- [ ] T023 Update developer documentation or quickstart links in `specs/027-knowledge-base-ai-wizard/quickstart.md`.
- [ ] T024 Add smooth micro-interactions (e.g. loading spin state, transition effects).
- [ ] T025 Confirm localization strings in Arabic are correct.

---

## Phase 7: SDD Guards & Verification

**Expected Result**: All guards run successfully and code validation passes.

- [ ] T026 Compare code changes against spec, plan, and requirements. Resolve any gaps.
- [ ] T027 Run `clean-code-guard` against changed production files.
- [ ] T028 Run `test-guard` against changed test files (if any).
- [ ] T029 **Feature Tests, Final Verification & Summary Report**: Run compile checks using `dotnet build` and `npm run build` and write `walkthrough.md`.
