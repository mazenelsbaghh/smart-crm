# Tasks: Core Foundation

**Input**: Design documents from `/specs/002-core-foundation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Pinned Python tests are included and MUST be written and pass to satisfy acceptance criteria.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding, Docker configuration updates, and directory structure setup.

- [ ] T001 Create C# ASP.NET Core Project structure under `backend/`
  - Action: Create `backend/backend.csproj` containing target framework `.net8.0` and references to:
    - `Microsoft.EntityFrameworkCore`
    - `Npgsql.EntityFrameworkCore.PostgreSQL`
    - `BCrypt.Net-Next`
    - `System.IdentityModel.Tokens.Jwt`
    - `RabbitMQ.Client`
    - `StackExchange.Redis`
  - File: `backend/backend.csproj`
  - Verify: Project builds successfully (`dotnet build`).

- [ ] T002 Create backend Dockerfile under `backend/Dockerfile`
  - Action: Create `backend/Dockerfile` using multi-stage build (`mcr.microsoft.com/dotnet/sdk:8.0` for build and `mcr.microsoft.com/dotnet/aspnet:8.0` for runtime). Expose port 5000.
  - File: `backend/Dockerfile`
  - Verify: Container builds successfully locally.

- [ ] T003 Create Node.js WhatsApp Gateway structure under `whatsapp-gateway/`
  - Action: Create `whatsapp-gateway/package.json` with dependencies:
    - `@whiskeysockets/baileys`
    - `express`
    - `redis`
    - `axios`
  - File: `whatsapp-gateway/package.json`
  - Verify: Run `npm install` inside the folder.

- [ ] T004 Create WhatsApp Gateway Dockerfile under `whatsapp-gateway/Dockerfile`
  - Action: Create `whatsapp-gateway/Dockerfile` using `node:20-alpine`, copy source files, expose port 3000, and define start command.
  - File: `whatsapp-gateway/Dockerfile`
  - Verify: Container builds successfully.

- [ ] T005 [P] Update root `docker-compose.yml` to include new services
  - Action: Add `backend` (depends on postgres, redis, rabbitmq) and `whatsapp-gateway` (depends on redis) services to `docker-compose.yml`. Make sure Nginx depends on `backend`.
  - File: `docker-compose.yml`
  - Verify: `docker compose config` runs and validates.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Set up EF Core database contexts, event bus, base class entities, and tenant context provider.

- [ ] T006 Create DB Context and Base Entities under `backend/src/Shared/Infrastructure/`
  - Action: Create `AppDbContext.cs` inheriting from `DbContext`. Add base abstract class `Entity` with `Id` (UUID) and `Auditable` fields. Add `OnModelCreating` to apply a global query filter on all tenant entities: `EF.Property<Guid>(e, "ProjectId") == _tenantContext.ProjectId`.
  - File: `backend/src/Shared/Infrastructure/AppDbContext.cs`
  - Verify: Compile successfully.

- [ ] T007 [P] Create RabbitMQ Event Bus under `backend/src/Shared/Queue/`
  - Action: Create `IEventBus.cs` defining `Publish<T>(T @event)` and `Subscribe<T, THandler>()`. Implement using `RabbitMQ.Client` in `RabbitMQEventBus.cs`.
  - File: `backend/src/Shared/Queue/RabbitMQEventBus.cs`
  - Verify: Compile successfully.

- [ ] T008 [P] Setup Tenant Context Provider under `backend/src/Shared/Security/`
  - Action: Create `ITenantContext.cs` exposing `Guid ProjectId`. Implement `TenantContext.cs` that extracts `X-Project-Id` header or JWT tenant claim from HttpContext.
  - File: `backend/src/Shared/Security/TenantContext.cs`
  - Verify: Compile successfully.

---

## Phase 3: User Story 1 - User Authentication & Authorization (Priority: P1)

**Goal**: Setup registration, login, refresh token, and logout endpoints.

**Independent Test**: Run `pytest tests/phase_1/test_auth.py` verifying registration and token cycle.

### Tests for User Story 1
- [ ] T009 [P] [US1] Create integration tests for Auth in `tests/phase_1/test_auth.py`
  - Action: Implement HTTP client tests posting to `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, and `/api/auth/logout`.
  - File: `tests/phase_1/test_auth.py`

### Implementation for User Story 1
- [ ] T010 [P] [US1] Create User & RefreshToken models
  - Action: Create `User.cs` and `RefreshToken.cs` in `backend/src/Modules/Auth/Domain/`. User has Email, PasswordHash, Role, ProjectId.
  - File: `backend/src/Modules/Auth/Domain/User.cs`

- [ ] T011 [P] [US1] Implement password hashing helper
  - Action: Create `BCryptPasswordHasher.cs` using `BCrypt.Net.BCrypt.HashPassword` and `Verify`.
  - File: `backend/src/Shared/Security/BCryptPasswordHasher.cs`

- [ ] T012 [US1] Implement JWT service
  - Action: Create `JwtService.cs` generating JWT with claims (UserId, Role, ProjectId) using Symmetric Security Key.
  - File: `backend/src/Shared/Security/JwtService.cs`

- [ ] T013 [US1] Implement AuthController and Endpoints
  - Action: Implement `AuthController.cs` with routes `/register`, `/login`, `/refresh`, `/logout`. Map endpoints to database.
  - File: `backend/src/Modules/Auth/API/AuthController.cs`
  - Verify: Start backend and run `test_auth.py` — it must pass.

---

## Phase 4: User Story 2 - Project Management & Isolation (Priority: P1)

**Goal**: Create projects and settings, and enforce Project ID filtering.

**Independent Test**: Run `pytest tests/phase_1/test_projects.py` verifying CRUD and isolation.

### Tests for User Story 2
- [ ] T014 [P] [US2] Create integration tests for Project Isolation in `tests/phase_1/test_projects.py`
  - Action: Test User A cannot access User B's project or setting records.
  - File: `tests/phase_1/test_projects.py`

### Implementation for User Story 2
- [ ] T015 [P] [US2] Create Project & ProjectSettings entities
  - Action: Create `Project.cs` and `ProjectSettings.cs` in `backend/src/Modules/Projects/Domain/`.
  - File: `backend/src/Modules/Projects/Domain/Project.cs`

- [ ] T016 [US2] Implement ProjectController
  - Action: Expose `POST /api/projects`, `GET /api/projects`, and `PUT /api/projects/{id}/settings`.
  - File: `backend/src/Modules/Projects/API/ProjectController.cs`
  - Verify: Run `test_projects.py` — it must pass.

---

## Phase 5: User Story 3 - WhatsApp Gateway Connection (Priority: P1)

**Goal**: Initialize Baileys sessions, retrieve QR codes, and monitor statuses.

**Independent Test**: Run `pytest tests/phase_1/test_whatsapp_gateway.py` checking connection states.

### Tests for User Story 3
- [ ] T017 [P] [US3] Create connection test in `tests/phase_1/test_whatsapp_gateway.py`
  - Action: Assert starting session, fetching QR, and mock connections.
  - File: `tests/phase_1/test_whatsapp_gateway.py`

### Implementation for User Story 3
- [ ] T018 [US3] Implement express server endpoints in `whatsapp-gateway/src/index.js`
  - Action: Setup Express REST API routes for session start, QR fetch, and status check.
  - File: `whatsapp-gateway/src/index.js`

- [ ] T019 [US3] Implement Baileys authentication flow in `whatsapp-gateway/src/baileys-manager.js`
  - Action: Handle WebSocket connection, QR code generation events, and state persistence.
  - File: `whatsapp-gateway/src/baileys-manager.js`
  - Verify: Run `test_whatsapp_gateway.py` — it must pass.

---

## Phase 6: User Story 4 - Real-time Message Ingestion & Aggregation (Priority: P1)

**Goal**: Webhook ingest, create customer/conversation, and aggregate messages using Redis.

**Independent Test**: Run `pytest tests/phase_1/test_conversations.py` and `test_message_aggregator.py`.

### Tests for User Story 4
- [ ] T020 [P] [US4] Create webhook and aggregator tests
  - Action: Write `tests/phase_1/test_conversations.py` and `tests/phase_1/test_message_aggregator.py`.
  - File: `tests/phase_1/test_conversations.py`

### Implementation for User Story 4
- [ ] T021 [P] [US4] Create Customer, Conversation, and Message entities
  - Action: Add CRM / Conversation models in backend domain.
  - File: `backend/src/Modules/Conversations/Domain/Conversation.cs`

- [ ] T022 [US4] Implement Webhook receiver
  - Action: Expose `POST /api/webhooks/whatsapp/message`. On message, save record, resolve conversation, and forward content.
  - File: `backend/src/Modules/Conversations/API/WebhookController.cs`

- [ ] T023 [US4] Implement Redis Aggregator logic
  - Action: Create `MessageAggregator.cs` that holds messages in Redis, monitors the 5-second silence window, and publishes a event when completed.
  - File: `backend/src/Modules/Conversations/Services/MessageAggregator.cs`
  - Verify: Run tests — must pass.

---

## Phase 7: User Story 5 - AI Auto-Response via Gemini (Priority: P1)

**Goal**: Process aggregated messages, fetch Gemini response, and send back to WhatsApp.

**Independent Test**: Run `pytest tests/phase_1/test_ai_gemini.py` verifying mock Gemini response sends back.

### Tests for User Story 5
- [ ] T024 [P] [US5] Create AI integration tests in `tests/phase_1/test_ai_gemini.py`
  - Action: Mock Gemini API call and assert response logic.
  - File: `tests/phase_1/test_ai_gemini.py`

### Implementation for User Story 5
- [ ] T025 [P] [US5] Implement Gemini API Connector
  - Action: Create `GeminiClient.cs` using HTTP requests to Google AI Endpoint.
  - File: `backend/src/Modules/AI/Services/GeminiClient.cs`

- [ ] T026 [US5] Implement AI background worker
  - Action: Create `AIReplyWorker.cs` consuming RabbitMQ `MessageAggregated` event, fetching response, and raising `AIReplyGenerated`.
  - File: `backend/src/Modules/AI/Workers/AIReplyWorker.cs`

- [ ] T027 [US5] Implement Send Message executor
  - Action: Consume `AIReplyGenerated` and forward to gateway send API.
  - File: `backend/src/Modules/WhatsApp/Workers/ReplySender.cs`
  - Verify: Run `test_ai_gemini.py` — it must pass.

---

## Phase 8: User Story 6 - Customer Profile & Follow-Up Management (Priority: P2)

**Goal**: Customer details updates and follow-ups scheduling.

**Independent Test**: Run `pytest tests/phase_1/test_crm.py` and `test_follow_ups.py`.

### Tests for User Story 8
- [ ] T028 [P] [US6] Create CRM and Follow-up tests in `tests/phase_1/test_crm.py` and `tests/phase_1/test_follow_ups.py`
  - Action: Test metadata updates, follow-up scheduling, and missed flags.
  - File: `tests/phase_1/test_crm.py`

### Implementation for User Story 8
- [ ] T029 [P] [US6] Create FollowUp model
  - Action: Define fields for FollowUp.
  - File: `backend/src/Modules/CRM/Domain/FollowUp.cs`

- [ ] T030 [US6] Implement CRMController & FollowUp Controller
  - Action: Expose REST endpoints for customer listing, metadata update, and follow-up CRUD.
  - File: `backend/src/Modules/CRM/API/CRMController.cs`

- [ ] T031 [US6] Setup Hangfire scheduler check
  - Action: Implement periodic background service to mark overdue follow-ups as "Missed".
  - File: `backend/src/Modules/CRM/Services/FollowUpScheduler.cs`
  - Verify: Run tests — must pass.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Clean up code, finalize migrations, and update docs.

- [ ] T032 Create migration scripts and DbSeed setup
  - Action: Add EF migrations (`make db-migrate`) and `make db-seed` task.
  - File: `backend/src/Shared/Infrastructure/DbSeeder.cs`

- [ ] T033 [P] Document Phase 1 Skill file under `.agents/skills/phase-1/SKILL.md`
  - Action: Write detailed description of endpoint routes, setup instruction, and commands.
  - File: `.agents/skills/phase-1/SKILL.md`

- [ ] T034 Run full verification checklist
  - Action: Execute `make test-all` and verify all tests pass.
  - File: `README.md`

---

## Dependencies & Execution Order

```text
Phase 1 (Setup)
  └──► Phase 2 (Foundational DB & Event Bus)
        ├──► Phase 3 (US1: Auth API)
        │     └──► Phase 4 (US2: Projects Isolation) 
        └──► Phase 5 (US3: WhatsApp Gateway)
              └──► Phase 6 (US4: Ingest & Aggregator)
                    └──► Phase 7 (US5: Gemini AI replies)
                          └──► Phase 8 (US6: CRM & Followups)
                                └──► Phase 9 (Polish)
```
