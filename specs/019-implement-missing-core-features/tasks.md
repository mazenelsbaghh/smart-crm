# Tasks: Implement Missing Core Features

**Input**: Design documents from `/specs/019-implement-missing-core-features/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

## Spec Kit Preparation Workflow
- [x] Phase 1: Feature Specification (`speckit-specify`) completed
- [x] Phase 2: Technical Planning (`speckit-plan`) completed
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the database schema for the newly added knowledge base approval status.

- [x] T001 Create a migration in `backend/` to add `ApprovalStatus` property to `KnowledgeDocument` by running the EF migration CLI command.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Prepare core models and schemas.

- [x] T002 Add the `KnowledgeApprovalStatus` enum and the `ApprovalStatus` property (defaulting to `Approved` or `2`) in the file `backend/src/Modules/Brain/Domain/KnowledgeDocument.cs`.
- [x] T003 Ensure database seeder `backend/src/Shared/Infrastructure/DbSeeder.cs` marks seeded knowledge base items as `Approved`.

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Workflow Automation Actions (Priority: P1)

**Goal**: Implement `SendMessage` and `SendAlert` actions in `WorkflowEngine.cs`.

**Independent Test**: Trigger a tag workflow and verify that a WhatsApp message is sent or a dashboard notification alert is raised.

### Implementation for User Story 1

- [x] T004 [US1] Update `WorkflowAction` in `backend/src/Modules/Workflows/Services/WorkflowEngine.cs` to properly deserialize parameter values.
- [x] T005 [US1] Implement the `SendMessage` action type in `ExecuteActionsAsync` of `backend/src/Modules/Workflows/Services/WorkflowEngine.cs` by resolving `IEventBus` and publishing an `AIReplyGeneratedEvent` (parsing name placeholders using customer fields).
- [x] T006 [US1] Implement the `SendAlert` action type in `ExecuteActionsAsync` of `backend/src/Modules/Workflows/Services/WorkflowEngine.cs` by inserting a new `NotificationAlert` into `AppDbContext` and pushing the alert via `IHubContext<NotificationHub>`.

**Checkpoint**: User Story 1 works independently.

---

## Phase 4: User Story 2 - Knowledge Suggestion & Approval (Priority: P1)

**Goal**: Add approval lifecycle for knowledge document suggestions.

**Independent Test**: Put a knowledge item in `PendingApproval`, run a RAG search, ensure it's excluded, then approve it and ensure it's included.

### Implementation for User Story 2

- [x] T007 [P] [US2] Update `AICompanyBrain.cs` RAG search query (`backend/src/Modules/Brain/Services/AICompanyBrain.cs`) to filter database matches by `ApprovalStatus == Approved`.
- [x] T008 [US2] Create REST endpoints `/api/projects/{projectId}/brain/suggest`, `/api/projects/{projectId}/brain/{documentId}/approve`, and `/api/projects/{projectId}/brain/{documentId}/reject` in `backend/src/Modules/Brain/API/BrainController.cs` to update document approval status.
- [x] T009 [US2] Implement the frontend React component changes in `frontend/src/packages/management/KnowledgeBase.tsx` to render a "Pending Approval" tab listing suggested drafts, complete with Approve and Reject action buttons.

**Checkpoint**: User Story 2 works independently.

---

## Phase 5: User Story 3 - Advanced Assignment Routing (Priority: P2)

**Goal**: Add VIP routing, complaint routing, and idle/offline reassignment checks.

**Independent Test**: Check conversation assignment when VIP/complaint states are triggered, and verify automatic agent reassignment when a message arrives for an offline agent.

### Implementation for User Story 3

- [x] T010 [US3] Add a check in `AssignConversationAsync` in `backend/src/Modules/Conversations/Services/AssignmentEngine.cs` to assign conversations to users with the Admin/Owner role if the customer's `LeadScore` is >= 80.
- [x] T011 [US3] Add a check in `AssignConversationAsync` in `backend/src/Modules/Conversations/Services/AssignmentEngine.cs` to assign conversations with sentiment alerts or complaints to users with the `Supervisor` role.
- [x] T012 [US3] Add an automatic offline/idle agent detection and reassignment check in `backend/src/Modules/Conversations/API/WebhookController.cs` (when a new message is received), re-routing the conversation via `AssignmentEngine` if the current agent is offline in Redis or has been idle for more than 10 minutes.

**Checkpoint**: All user stories work independently.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify and clean up.

- [x] T013 Verify both `backend` and `frontend` build cleanly without compiler errors or warnings.
- [x] T014 Run unit/integration tests to verify new features.
