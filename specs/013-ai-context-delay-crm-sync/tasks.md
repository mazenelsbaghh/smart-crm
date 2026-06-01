# Tasks: AI Context, Delay Tuning & Auto CRM Deal Sync

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Foundational Tasks

- [x] T001 Verify existing models: `CustomerMemory.cs` and `Message.cs` are fully accessible in backend project namespaces.

---

### User Story 1: Contextualized AI Auto-Reply (Priority: P1)

**Goal**: Fetch and pass recent chat history and customer memory into the Gemini auto-reply prompt.

- [x] T002 In `backend/src/Modules/AI/Services/AIMarketingBrain.cs`, update `IAIMarketingBrain` interface to add `chatHistory` and `customerMemory` parameters to `AnalyzeAndGenerateReplyAsync`.
- [x] T003 In `backend/src/Modules/AI/Services/AIMarketingBrain.cs`, update the implementation of `AnalyzeAndGenerateReplyAsync` to accept the new parameters and inject them into `systemPrompt` if not null/empty.
- [x] T004 In `backend/src/Modules/AI/Workers/AIReplyWorker.cs`, import the conversations namespaces. Move customer lookup to before prompt generation.
- [x] T005 In `backend/src/Modules/AI/Workers/AIReplyWorker.cs`, retrieve the customer's active open conversation, load its last 15 messages, format them chronologically as `"Customer: ..."` or `"Agent/AI: ..."` to build `chatHistory`.
- [x] T006 In `backend/src/Modules/AI/Workers/AIReplyWorker.cs`, retrieve the customer's memory summary, facts, and objections to build `customerMemory`.
- [x] T007 In `backend/src/Modules/AI/Workers/AIReplyWorker.cs`, call `AnalyzeAndGenerateReplyAsync` passing `chatHistory` and `customerMemory`.

---

### User Story 2: Automated CRM Budget & Deal Sync (Priority: P1)

**Goal**: Automatically sync the customer's active deal amount when their budget is updated via AI CRM auto-updates or supervisor approvals.

- [x] T008 In `backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs`, inside `ProcessSuggestionAsync`'s high confidence budget block, update the customer's active open deal Amount to match the new budget.
- [x] T009 In `backend/src/Modules/Approvals/API/ApprovalsController.cs`, inside `ExecuteActionInternalAsync`'s CRMUpdate budget block, update the customer's active open deal Amount to match the new budget.

---

### User Story 3: Natural Aggregation & Typing Delays (Priority: P2)

**Goal**: Set message aggregation delay to a random 30-50 seconds and typing delay to a clamped 5-9 seconds.

- [x] T010 In `backend/src/Modules/Conversations/Services/MessageAggregator.cs`, update the Task delay from `5000` to a randomized value between `30000` and `50000` milliseconds.
- [x] T011 In `backend/src/Modules/WhatsApp/Services/HumanMessagingEngine.cs`, update `CalculateTypingDelay` to clamp the return value between `5000` and `9000` milliseconds.

---

### Phase N: Rebuild & Verify

- [x] T012 Rebuild backend: `docker compose up -d --build backend`.
- [x] T013 Verify health: `make health`.
- [x] T014 Run tests: `make test-phase-1` and `make test-phase-3`.
