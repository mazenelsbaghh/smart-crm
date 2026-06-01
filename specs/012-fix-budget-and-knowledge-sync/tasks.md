# Tasks: CRM Customer Budget & Knowledge Sync Seeding Fixes

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Foundational Tasks

- [x] T001 Verify database configuration and that pgvector migration is up-to-date.

---

### User Story 1: Persist and Clear Customer Budget (Priority: P1)

**Goal**: Allow setting or clearing the customer's budget via the update customer API endpoint, and keep it in sync with their active deal.

**Independent Test**: Put request to `/api/customers/{id}` with `{"budget": null}` clears the customer budget and active deal amount to 0.

- [x] T002 In `backend/src/Modules/CRM/API/CRMController.cs`, update `UpdateCustomerRequest` class to use backing field and property setter logic to track `IsBudgetSet` flag.
- [x] T003 In `backend/src/Modules/CRM/API/CRMController.cs`, inside `UpdateCustomer` action, update the check for `request.Budget` to use `request.IsBudgetSet`, updating `customer.Budget` directly to `request.Budget`.
- [x] T004 In `backend/src/Modules/CRM/API/CRMController.cs`, inside `UpdateCustomer` action's active deal update block, set `activeDeal.Amount = request.Budget ?? 0` if `request.IsBudgetSet` is true.
- [x] T005 In `backend/src/Modules/CRM/API/CRMController.cs`, inside `UpdateCustomer` action's `else` block (when no pipeline stage is passed), check if `request.IsBudgetSet` is true, and if so, set `activeDeal.Amount = request.Budget ?? 0` and mark the deal as modified.

**Checkpoint**: Build the backend container and run `make test-phase-1` to verify the CRM customer updating tests still pass.

---

### User Story 2: Sync AI Brain without Erasing User Documents (Priority: P1)

**Goal**: AI sync brain does not delete the user's manual documents, and only seeds the three policy templates if the database is empty.

**Independent Test**: Click "Sync AI Brain" on a project with existing manual documents and verify they are kept and properly indexed.

- [x] T006 In `backend/src/Modules/Brain/Services/AICompanyBrain.cs`, modify `SyncBrainAsync` to check if any knowledge documents exist for the project: `var hasDocs = await _dbContext.KnowledgeDocuments.AnyAsync(d => d.ProjectId == projectId);`.
- [x] T007 In `backend/src/Modules/Brain/Services/AICompanyBrain.cs`, inside `SyncBrainAsync`, wrap the template seeding logic inside an `if (!hasDocs)` conditional block.
- [x] T008 In `backend/src/Modules/Brain/Services/AICompanyBrain.cs`, inside `SyncBrainAsync`, add an `else` block that iterates through existing `KnowledgeDocuments` and checks if they have any chunks. If they lack chunks, generate chunks and embeddings for them using `_geminiClient.GenerateEmbeddingAsync` and save.

**Checkpoint**: Run `pytest tests/phase_3/test_company_brain.py` to verify that the sync brain function correctly seeds a new project and passes tests.

---

### Phase N: Polish & Docker Rebuild

- [x] T009 Rebuild backend container: `docker compose up -d --build backend`.
- [x] T010 Verify health of all services: `make health`.
- [x] T011 Run all phase tests: `make test-phase-1` and `make test-phase-3`.
