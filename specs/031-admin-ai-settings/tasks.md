# Tasks: Admin AI Behavior Settings

**Input**: Design documents from `/specs/031-admin-ai-settings/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/api.md`, `quickstart.md`

**Tests**: Required for settings persistence/validation, prompt behavior, channel fallback, and reaction enforcement.

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the storage and shared behavior settings types used by all stories.

- [x] T001 Add `AiBehaviorSettingsJson` property to `backend/src/Modules/Projects/Domain/ProjectSettings.cs`.
- [x] T002 Add EF migration `backend/Migrations/20260627000000_AddAiBehaviorSettingsToProjectSettings.cs` with `AiBehaviorSettingsJson` text column on `ProjectSettings`.
- [x] T003 Update `backend/Migrations/AppDbContextModelSnapshot.cs` to include `ProjectSettings.AiBehaviorSettingsJson`.
- [x] T004 [P] Create `backend/src/Modules/AI/Services/AIBehaviorSettings.cs` with settings records/classes from `data-model.md`.
- [x] T005 Create `backend/src/Modules/AI/Services/AIBehaviorSettingsService.cs` with default settings, JSON deserialize/serialize, channel merge, template rendering, placeholder validation, and reaction validation methods.
- [x] T006 Register `IAIBehaviorSettingsService` in `backend/Program.cs`.

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Expose and validate settings before any runtime AI path consumes them.

- [x] T007 [P] Add backend tests in `tests/phase_1/test_admin_ai_settings.py` for saving and reloading `settings.aiBehavior` through `GET /api/projects/{id}` and `PUT /api/projects/{id}/settings`.
- [x] T008 [P] Add backend tests in `tests/phase_1/test_admin_ai_settings.py` for rejecting unsupported placeholders, templates over 1000 characters, invalid channel keys, and invalid reactions.
- [x] T009 [P] Add backend tests in `tests/phase_1/test_admin_ai_settings.py` for two-project isolation of staff names and fallback messages.
- [x] T010 Update `backend/src/Modules/Projects/API/ProjectController.cs` response DTO to return `settings.aiBehavior` resolved from `AiBehaviorSettingsJson`.
- [x] T011 Update `backend/src/Modules/Projects/API/ProjectController.cs` request DTO to accept `aiBehavior` and persist serialized JSON after validation.
- [x] T012 Update `backend/src/Modules/Projects/API/ProjectController.cs` to keep `SystemPrompt` as secondary advanced instructions and not as a full protected prompt replacement.

## Phase 3: User Story 1 - Configure AI identity and prompt behavior (Priority: P1) 🎯 MVP

**Goal**: Admin changes identity, signature, tone/audience, channel additions, and reaction policy from settings; AI prompt uses those settings without breaking protected JSON output.

**Independent Test**: Save custom staff names/signature/tone/reaction policy, send a mock AI message, and verify prompt output behavior and reaction suppression.

### Tests for User Story 1

- [ ] T013 [P] [US1] Add AI prompt regression test in `tests/phase_1/test_admin_ai_settings.py` proving hostile `systemPrompt` text cannot remove required JSON/CRM behavior.
- [ ] T014 [P] [US1] Add reaction-disabled test in `tests/phase_1/test_admin_ai_settings.py` proving suggested AI reactions are not sent or persisted when disabled.
- [ ] T015 [P] [US1] Add channel additional-instructions test in `tests/phase_1/test_admin_ai_settings.py` proving Messenger-specific instructions do not affect WhatsApp.

### Implementation for User Story 1

- [x] T016 [US1] Refactor `backend/src/Modules/AI/Services/AIMarketingBrain.cs` so protected `SystemPromptTemplate` is always used and advanced instructions are appended below structured settings.
- [x] T017 [US1] Update `backend/src/Modules/AI/Services/AIMarketingBrain.cs` to accept resolved identity, signature, tone, reaction, and channel instructions from `AIBehaviorSettingsService`.
- [x] T018 [US1] Update `backend/src/Modules/AI/Workers/AIReplyWorker.cs` to resolve AI behavior settings once per message and pass resolved settings into static prompt and dynamic prompt generation.
- [x] T019 [US1] Update `backend/src/Modules/AI/Workers/AIReplyWorker.cs` pricing guard and auto-reaction logic to call reaction policy before assigning, saving, or sending reactions.
- [x] T020 [US1] Ensure Gemini context cache hash in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` includes resolved AI behavior content.

## Phase 4: User Story 2 - Configure channel-specific messages and fallbacks (Priority: P2)

**Goal**: Admin-configured fallback and transition templates are used per channel.

**Independent Test**: Configure distinct fallback templates, force transition success/failure and Facebook public fallback paths, and verify the customer-facing text.

### Tests for User Story 2

- [ ] T021 [P] [US2] Add Messenger-to-WhatsApp transition success template test in `tests/phase_1/test_admin_ai_settings.py`.
- [ ] T022 [P] [US2] Add Messenger-to-WhatsApp transition failure template test in `tests/phase_1/test_admin_ai_settings.py`.
- [ ] T023 [P] [US2] Add Facebook public comment fallback and reaction policy test in `tests/phase_1/test_admin_ai_settings.py`.
- [ ] T024 [P] [US2] Add invalid AI output fallback test in `tests/phase_1/test_admin_ai_settings.py`.

### Implementation for User Story 2

- [x] T025 [US2] Replace hardcoded Messenger-to-WhatsApp success/failure strings in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` with rendered fallback templates.
- [x] T026 [US2] Replace hardcoded follow-up default text in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` with rendered `FollowUpDefault`.
- [x] T027 [US2] Replace invalid AI output and AI error fallback strings in `backend/src/Modules/AI/Services/AIMarketingBrain.cs` with resolved fallback settings.
- [x] T028 [US2] Replace hardcoded Facebook public comment fallback and forced LOVE reaction in `backend/src/Modules/Facebook/Workers/FacebookReplySender.cs`.
- [x] T029 [US2] Apply reaction policy in `backend/src/Modules/Conversations/API/ConversationController.cs` for `comment-reply` and `messages/{messageId}/react`.

## Phase 5: User Story 3 - Preserve protected AI invariants and defaults (Priority: P3)

**Goal**: Existing projects continue working, protected rules cannot be overridden, and the admin UI exposes structured inputs.

**Independent Test**: Existing settings calls still pass, invalid advanced prompt cannot break parsing, and frontend build succeeds with structured settings UI.

### Tests for User Story 3

- [ ] T030 [P] [US3] Update existing settings tests in `tests/phase_1/test_projects.py` only if response assertions need `aiBehavior` defaults.
- [ ] T031 [P] [US3] Add default-backward-compatibility test in `tests/phase_1/test_admin_ai_settings.py`.
- [x] T032 [P] [US3] Add frontend type/build coverage by ensuring `frontend/src/packages/settings/Settings.tsx` compiles with `settings.aiBehavior`.

### Implementation for User Story 3

- [x] T033 [US3] Update `frontend/src/packages/settings/Settings.tsx` types to include `aiBehavior` and all structured sections.
- [x] T034 [US3] Update `frontend/src/packages/settings/Settings.tsx` state initialization and API save payload for identity/signature/tone/reactions/fallbacks/channels/advanced instructions.
- [x] T035 [US3] Update `frontend/src/packages/settings/Settings.tsx` UI with organized sections and explicit inputs for identity/signature, tone/audience, channels, reactions, fallback messages, and Advanced instructions.
- [x] T036 [US3] Update `frontend/src/packages/settings/settings.module.css` only if existing classes cannot support stable responsive controls.
- [ ] T037 [US3] Update `docs/backend_plan.md` or `docs/frontend_plan.md` only if the implemented behavior needs operator-facing documentation.

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Required verification and quality gates.

- [x] T038 Run deep critique prompt and record/fix findings in `achievements.md` and this `tasks.md`.
- [x] T039 Run `clean-code-guard` on changed production files and fix every finding.
- [x] T040 Run `test-guard` on changed test files and fix every finding.
- [ ] T041 Run feature tests with `pytest tests/phase_1/test_admin_ai_settings.py` and confirm all admin AI settings scenarios pass.
- [ ] T042 Run `pytest tests/phase_1/test_ai_gemini.py::test_ai_gemini_reaction_and_no_buttons`.
- [ ] T043 Run `pytest tests/phase_1/test_messenger_whatsapp_followup.py::test_messenger_to_whatsapp_transition`.
- [x] T044 Run `dotnet build backend/backend.csproj`.
- [x] T045 Run `cd frontend && npm run build`.
- [x] T046 Run `python3 .agents/skills/speckit-all/scripts/validate_run.py --root . --spec-dir specs/031-admin-ai-settings`.

## Dependencies & Execution Order

- Phase 1 must complete before Phase 2.
- Phase 2 must complete before any user story implementation.
- US1 is MVP and must complete before US2 runtime fallbacks because prompt/reaction resolver is shared.
- US2 depends on resolver APIs from US1.
- US3 frontend work can proceed after Phase 2 API contract exists, but final build depends on backend DTO shape.
- Phase 6 runs after all selected user stories.

## Parallel Opportunities

- T004 can run while migration tasks T001-T003 are prepared.
- T007-T009 can be drafted in parallel because they touch the same new test file but independent test functions; final merge must be sequential.
- T013-T015 and T021-T024 are independent test functions in the same test file; final merge must be sequential.
- T033-T036 frontend work can proceed after backend DTO names are fixed.

## Implementation Strategy

1. Complete storage, settings DTO, and validation first.
2. Implement US1 prompt and reaction policy as MVP.
3. Implement US2 fallback/transition text replacement.
4. Implement US3 UI and defaults compatibility.
5. Run review gates, targeted tests, backend build, frontend build, and final validation in order.

## Expected Observable Outcomes

- Expected result: project settings API saves and returns `settings.aiBehavior` per project.
- Expected result: invalid placeholders, invalid reactions, invalid channel keys, and over-limit templates return `400`.
- Expected result: AI replies use configured identity/signature/tone/fallbacks while protected JSON/CRM schema still works.
- Expected result: disabled or disallowed reactions are not saved or sent on WhatsApp or Facebook paths.
- Expected result: admin settings UI builds and exposes organized inputs for all configured behavior sections.
