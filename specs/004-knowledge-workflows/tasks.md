# Tasks: Company Brain, Knowledge Base, Workflows & Approval System

**Input**: Design documents from `/specs/004-knowledge-workflows/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Initialize Phase 3 directories for modules: Brain, Workflows, Approvals, Integrations in `backend/src/Modules/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Enable Vector DB extension and database context support for PostgreSQL pgvector

- [x] T002 Setup database configuration to auto-create pgvector extension on startup in `backend/src/Shared/Infrastructure/AppDbContext.cs`
- [x] T003 Ensure NuGet packages for Npgsql vector mappings are fully referenced in `backend/backend.csproj`

---

## Phase 3: User Story 1 - AI Retrieval & Company Brain Sync (Priority: P1) 🎯 MVP

**Goal**: AI syncs knowledge base items and retrieves relevant chunks using semantic search query contexts.

**Independent Test**: Run `tests/phase_3/test_company_brain.py` to assert sync ingestion, vector generation, and similarity search return correct chunks.

### Tests for User Story 1

- [x] T004 [P] [US1] Create integration test `tests/phase_3/test_company_brain.py` to verify brain sync and semantic search endpoints.

### Implementation for User Story 1

- [x] T005 [P] [US1] Create `KnowledgeDocument.cs` and `KnowledgeChunk.cs` models in `backend/src/Modules/Brain/Domain/` containing embedding vectors (float[] / pgvector).
- [x] T006 [US1] Add DB context sets for `KnowledgeDocuments` and `KnowledgeChunks` in `backend/src/Shared/Infrastructure/AppDbContext.cs`.
- [x] T007 [US1] Create EF Core migration for Knowledge base tables and run `dotnet ef database update` inside the backend.
- [x] T008 [P] [US1] Implement `AICompanyBrain.cs` service under `backend/src/Modules/Brain/Services/` that generates text embeddings via `GeminiClient` and executes pgvector cosine similarity searches.
- [x] T009 [US1] Add `POST /api/projects/{projectId}/brain/sync` and `GET /api/projects/{projectId}/brain/search` API endpoints in `backend/src/Modules/Brain/API/BrainController.cs`.

---

## Phase 4: User Story 2 - Knowledge Base Management & Approval Workflow (Priority: P1)

**Goal**: Add FAQ/Document CRUD management endpoints and Draft -> Published approval workflow.

**Independent Test**: Run `tests/phase_3/test_knowledge_base.py` to verify that drafts are excluded from similarity searches, and only active documents are fetched.

### Tests for User Story 2

- [x] T010 [P] [US2] Create integration test `tests/phase_3/test_knowledge_base.py` to verify FAQ creation, status changes, and context filtering.

### Implementation for User Story 2

- [x] T011 [P] [US2] Implement `KnowledgeBaseService.cs` in `backend/src/Modules/Brain/Services/` containing status transition policies (Draft, Published, Archived).
- [x] T012 [US2] Add endpoints `PUT /api/knowledge/{id}/approve` and `PUT /api/knowledge/{id}/reject` in `backend/src/Modules/Brain/API/KnowledgeBaseController.cs`.

---

## Phase 5: User Story 3 - Workflow Trigger & Automation Engine (Priority: P1)

**Goal**: Execute sequence-based trigger actions (set tags, update CRM statuses) asynchronously via workflow engine.

**Independent Test**: Run `tests/phase_3/test_workflows.py` to assert triggers are evaluated and actions fire successfully.

### Tests for User Story 3

- [x] T013 [P] [US3] Create integration test `tests/phase_3/test_workflows.py` to verify trigger evaluation, action executions, and delay routing.

### Implementation for User Story 3

- [x] T014 [P] [US3] Create `AutomationWorkflow.cs` and `WorkflowExecutionLog.cs` models in `backend/src/Modules/Workflows/Models/`.
- [x] T015 [US3] Register workflow DB tables in application DbContext and run EF Core migrations.
- [x] T016 [P] [US3] Implement `WorkflowEngine.cs` service in `backend/src/Modules/Workflows/Services/` to parse and execute trigger conditions/actions.
- [x] T017 [US3] Add workflow CRUD and toggle endpoints in `backend/src/Modules/Workflows/API/WorkflowsController.cs`.
- [x] T018 [US3] Create `WorkflowWorker.cs` background consumer in `backend/src/Modules/Workflows/Workers/` to consume RabbitMQ trigger events.

---

## Phase 6: User Story 4 - AI Risk & Action Approval System (Priority: P1)

**Goal**: Evaluate AI actions using RiskAnalyzer; high-risk actions are paused and require supervisor verification.

**Independent Test**: Run `tests/phase_3/test_approvals.py` to verify high-risk actions are successfully intercepted and manually approved.

### Tests for User Story 4

- [x] T019 [P] [US4] Create integration test `tests/phase_3/test_approvals.py` to verify risk assessment and authorization execution.

### Implementation for User Story 4

- [x] T020 [P] [US4] Create `ApprovalRequest.cs` model in `backend/src/Modules/Approvals/Models/`.
- [x] T021 [US4] Register approvals tables in DbContext and run EF migrations.
- [x] T022 [P] [US4] Implement `RiskAnalyzer.cs` service in `backend/src/Modules/Approvals/Services/` to classify action hazards (Low/Medium/High/Critical).
- [x] T023 [US4] Add endpoints `GET /api/projects/{projectId}/approvals`, `POST /api/approvals/{id}/approve`, and `POST /api/approvals/{id}/reject` in `backend/src/Modules/Approvals/API/ApprovalsController.cs`.

---

## Phase 7: User Story 5 - Integrations Layer & Customer Memory (Priority: P2)

**Goal**: Sync data via external APIs periodically, and extract/maintain long-term customer context upon conversation close.

**Independent Test**: Run `tests/phase_3/test_integrations.py` and `tests/phase_3/test_customer_memory.py` to assert integration schedules and customer memory updates are successful.

### Tests for User Story 5

- [x] T024 [P] [US5] Create integration test `tests/phase_3/test_integrations.py` to verify pull-based sync schedules.
- [x] T025 [P] [US5] Create integration test `tests/phase_3/test_customer_memory.py` to verify memory generation post-conversation.

### Implementation for User Story 5

- [x] T026 [P] [US5] Create `ProjectIntegration.cs` in `backend/src/Modules/Integrations/Models/` and `CustomerMemory.cs` in `backend/src/Modules/Customers/Models/`.
- [x] T027 [US5] Register integration and customer memory tables in DbContext and run EF migrations.
- [x] T028 [P] [US5] Implement `ProjectIntegrationService.cs` in `backend/src/Modules/Integrations/Services/` to trigger external HTTP gets and schedule them in Hangfire.
- [x] T029 [P] [US5] Implement `CustomerMemoryService.cs` in `backend/src/Modules/Customers/Services/` to summarize conversations via Gemini.
- [x] T030 [US5] Add integration setup API endpoints in `backend/src/Modules/Integrations/API/IntegrationsController.cs`.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, Makefile targets, and documentation updates.

- [x] T031 Run all tests using `make test-all` and ensure 100% compliance across all 35 test suites.
- [x] T032 Create Phase 3 skill guide at `.agents/skills/phase-3/SKILL.md`.
- [x] T033 Update repository `Makefile` with targets `make brain-sync`, `make knowledge-search`, and `make approval-queue`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup.
- **User Story 1 (Phase 3)**: Depends on Phase 2 (pgvector setup).
- **User Story 2 (Phase 4)**: Depends on US1 (Knowledge models and base brain sync endpoints).
- **User Story 3 (Phase 5)**: Depends on Setup/Foundational.
- **User Story 4 (Phase 6)**: Depends on Setup/Foundational.
- **User Story 5 (Phase 7)**: Depends on US1, US4, and Hangfire.
- **Polish (Phase 8)**: Depends on completion of all implementation phases.
