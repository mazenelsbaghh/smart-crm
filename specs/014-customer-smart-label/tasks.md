# Tasks: Customer Smart Label

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Foundational Tasks

- [x] T001 [P] Modify `backend/src/Modules/Conversations/Domain/Customer.cs` to add the `Label` string property: `public string? Label { get; set; }`
- [x] T002 Generate database migration in the backend folder by executing `dotnet ef migrations add AddCustomerLabel --project src/backend.csproj --startup-project src/backend.csproj` and apply it using EF Core migrations.

---

### User Story 1: Automated Label Prediction (Priority: P1)

**Goal**: Update the AI brain engine and workers to predict, map, and persist the customer label on every message.

- [x] T003 [US1] In `backend/src/Modules/AI/Services/AIMarketingBrain.cs`, add the `Label` string property to the `MarketingAnalysisResult` class.
- [x] T004 [US1] In `backend/src/Modules/AI/Services/AIMarketingBrain.cs`, update the Gemini system prompt to ask for a short Arabic label (max 3 words) under the JSON field `"label"`, and update parsing to deserialize it.
- [x] T005 [US1] In `backend/src/Shared/Events/CRMUpdateSuggestedEvent.cs`, add the `Label` string property to `CRMUpdateSuggestedEvent` class.
- [x] T006 [US1] In `backend/src/Modules/AI/Workers/AIReplyWorker.cs`, update the mapping logic to populate `crmSuggestion.Label` from `analysisResult.Label`.
- [x] T007 [US1] In `backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs`, update `ProcessSuggestionAsync` to save the predicted label to `customer.Label` (or fallback to `"استفسار عام"` if null/empty). Ensure changes are saved in the database, and publish a SignalR alert/broadcast using `_hubContext` to update connected browsers with the new customer state.

---

### User Story 2: Real-time Chat Label Display (Priority: P1)

**Goal**: Add the label to the frontend type definitions, display it next to the customer's name in the inbox sidebar and chat header, and listen to real-time updates.

- [x] T008 [US2] In frontend type definitions `frontend/src/services/crm.ts` and `frontend/src/types/chat.ts`, add the `label?: string;` property to the `Customer` interfaces.
- [x] T009 [US2] In `frontend/src/packages/inbox/Inbox.tsx`, render a label badge (e.g. `<span className={styles.convLabelBadge}>{c.customer.label}</span>`) next to the customer's name in the conversation cards in the sidebar and next to the active customer's name in the chat header.
- [x] T010 [US2] In `frontend/src/packages/inbox/Inbox.tsx`, update the SignalR event handlers or state updates to capture real-time updates of customer records and merge them into local `conversations` list and `activeConv` states.

---

### User Story 3: CRM Board & List Integration (Priority: P2)

**Goal**: Render the smart label badge inside the CRM customer table and Pipeline board deal cards.

- [x] T011 [US3] In `frontend/src/packages/crm/CustomerList.tsx`, render the customer's smart label badge in the customer name cell below the name next to their phone number.
- [x] T012 [US3] In `frontend/src/packages/crm/PipelineBoard.tsx`, render the customer's smart label badge inside the deal card below the customer's name.
- [x] T013 [US3] In `frontend/src/components/shared/CustomerDetail.tsx`, display the smart label inside the profile information layout.

---

### Phase N: Rebuild & Verify

- [x] T014 Rebuild frontend and backend containers: `docker compose up -d --build backend frontend`.
- [x] T015 Verify health: `make health`.
- [x] T016 Run integration tests: `make test-phase-1` and `make test-phase-3`.
