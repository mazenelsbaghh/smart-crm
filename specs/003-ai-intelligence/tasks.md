# Tasks: AI Intelligence & CRM Foundation

**Input**: Design documents from `/specs/003-ai-intelligence/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Infrastructure dependencies installation.

- [x] T001 Install Hangfire NuGet dependencies `Hangfire.AspNetCore` and `Hangfire.PostgreSql` in `backend/backend.csproj`
- [x] T002 Configure SignalR in ASP.NET Core project setup in `backend/Program.cs`
- [x] T003 Setup PostgreSQL Hangfire storage connection in `backend/Program.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database migrations and base abstractions setup.

- [x] T004 Create database migration scripts for new tables `CRMUpdateProposals` and `NotificationAlerts` in `backend/Migrations/`
- [x] T005 [P] Create `CRMUpdateProposal.cs` model inheriting from `ITenantEntity` in `backend/src/Modules/CRM/Domain/CRMUpdateProposal.cs`
- [x] T006 [P] Create `NotificationAlert.cs` model inheriting from `ITenantEntity` in `backend/src/Modules/Conversations/Domain/NotificationAlert.cs`
- [x] T007 Register the new entities in the DB context in `backend/src/Shared/Infrastructure/AppDbContext.cs`

---

## Phase 3: User Story 1 - AI Marketing Brain (Priority: P1)

**Goal**: Implement prompt templates and marketing analysis.

**Independent Test**: Send different message scenarios and check output reply style.

- [x] T008 [P] [US1] Create integration test `test_ai_marketing_brain.py` in `tests/phase_2/test_ai_marketing_brain.py`
- [x] T009 [US1] Implement prompt templates for marketing analysis in `backend/src/Modules/AI/Services/AIMarketingBrain.cs`
- [x] T010 [US1] Update `AIReplyWorker.cs` to consume marketing analysis context in `backend/src/Modules/AI/Workers/AIReplyWorker.cs`

---

## Phase 4: User Story 2 - Smart Human-Like Messaging (Priority: P1)

**Goal**: Split reply and typing simulation.

**Independent Test**: Verify chunking delay in the gateway webhook logs.

- [x] T011 [P] [US2] Create integration test `test_human_messaging.py` in `tests/phase_2/test_human_messaging.py`
- [x] T012 [US2] Implement sentence splitting utility in `backend/src/Modules/WhatsApp/Services/HumanMessagingEngine.cs`
- [x] T013 [US2] Integrate typing delay logic and anti-ban checks in `backend/src/Modules/WhatsApp/Workers/ReplySender.cs`

---

## Phase 5: User Story 3 - AI CRM Auto-Updates & Entity Extraction (Priority: P1)

**Goal**: AI-suggested metadata updates.

**Independent Test**: Verify new attributes populated after keyword trigger.

- [x] T014 [P] [US3] Create integration test `test_crm_auto_update.py` in `tests/phase_2/test_crm_auto_update.py`
- [x] T015 [US3] Implement `CRMAutoUpdateEngine.cs` to handle entity extraction prompts in `backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs`
- [x] T016 [US3] Create background consumer `CRMWorker.cs` to apply low-risk updates or request approval in `backend/src/Modules/CRM/Workers/CRMWorker.cs`

---

## Phase 6: User Story 4 - AI Intent & Sentiment Analysis (Priority: P1)

**Goal**: Intent classification and sentiment analysis.

**Independent Test**: Verify negative sentiment tags conversation for priority.

- [x] T017 [P] [US4] Create integration test `test_intent_sentiment.py` in `tests/phase_2/test_intent_sentiment.py`
- [x] T018 [US4] Update prompt template to request intent and sentiment analysis in `backend/src/Modules/AI/Services/AIMarketingBrain.cs`

---

## Phase 7: User Story 5 - Assignment Engine (Priority: P1)

**Goal**: Conversation workload routing.

**Independent Test**: Verify conversation assigns to agent with least workload.

- [x] T019 [P] [US5] Create integration test `test_assignment.py` in `tests/phase_2/test_assignment.py`
- [x] T020 [US5] Implement presence check and load tracker in Redis in `backend/src/Modules/Conversations/Services/AssignmentEngine.cs`
- [x] T021 [US5] Implement `POST /api/conversations/{id}/assign` endpoint in `backend/src/Modules/Conversations/API/ConversationController.cs`

---

## Phase 8: User Story 6 - Scheduler Engine (Priority: P1)

**Goal**: Hangfire background scheduler.

**Independent Test**: Verify cron runs health check.

- [x] T022 [P] [US6] Create integration test `test_scheduler.py` in `tests/phase_2/test_scheduler.py`
- [x] T023 [US6] Configure Hangfire Server and Dashboard middlewares in `backend/Program.cs`
- [x] T024 [US6] Register recurring cron jobs (Follow-up check, Lead score update) in `backend/src/Modules/CRM/Services/FollowUpScheduler.cs`

---

## Phase 9: User Story 7 - SignalR Notifications Hub (Priority: P1)

**Goal**: Real-time push notifications.

**Independent Test**: Verify client receives WS payload.

- [x] T025 [P] [US7] Create integration test `test_notifications.py` in `tests/phase_2/test_notifications.py`
- [x] T026 [US7] Create `NotificationHub.cs` under `backend/src/Modules/Conversations/Hubs/NotificationHub.cs`
- [x] T027 [US7] Setup JWT Token interceptor for Hub connections in `backend/Program.cs`

---

## Phase 10: User Story 8 - Basic Reports (Priority: P2)

**Goal**: Implement reports endpoints.

**Independent Test**: Verify JSON response schema.

- [x] T028 [P] [US8] Create integration test `test_reports.py` in `tests/phase_2/test_reports.py`
- [x] T029 [US8] Implement reports controller with daily, follow-up, and AI stats in `backend/src/Modules/CRM/API/ReportsController.cs`

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Run validations and update skills.

- [x] T030 Perform full database migration check (`make db-migrate`)
- [x] T031 Document Phase 2 Skill details in `.agents/skills/phase-2/SKILL.md`
- [x] T032 Verify all 28 tests pass (`make test-all`)
