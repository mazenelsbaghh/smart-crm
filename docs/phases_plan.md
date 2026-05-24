# Smart Customer Core — Phased Implementation Plan

**Last Updated**: 2026-05-24
**Constitution Version**: 1.0.0
**Architecture Reference**: [`smart_customer_core_final_with_shared_storage.md`](../smart_customer_core_final_with_shared_storage.md)

---

## Global Rules & Conditions

> [!IMPORTANT]
> Every phase MUST satisfy ALL of the following conditions before it is considered complete.

### R1. Docker-First Development
- Every service, database, queue, and tool MUST run inside Docker via `docker-compose.yml`.
- A root-level `Makefile` MUST exist with targets for every phase.
- Each phase adds new `make` targets and extends `docker-compose.yml` with new services.
- `make up` MUST always bring the full stack up in a working state.

### R2. Python Testing (pytest)
- Every module, API endpoint, worker, and integration MUST have comprehensive Python tests.
- Tests are written with `pytest` + `httpx` (for API) + `pytest-asyncio` (for async).
- **No Playwright**. Browser testing is NOT used. All tests are API-level, integration-level, and unit-level.
- Test directory: `tests/` at root, mirroring module structure.
- Every phase MUST include a `make test-phase-N` target.
- CI pipeline runs `make test-all` which runs ALL phase tests cumulatively.

### R3. Testable From Phase 1
- Phase 1 MUST produce a running system you can interact with via API and verify via tests.
- Each subsequent phase extends the running system — never breaks it.
- Regression tests from previous phases MUST continue passing.

### R4. Git Workflow
- Repository initialized at Phase 0 with `.gitignore`, branch protection rules documented.
- Each phase gets its own feature branch: `phase/N-short-description`.
- Merge to `main` only after all tests pass.
- Conventional commits enforced: `feat:`, `fix:`, `test:`, `docs:`, `chore:`.
- Tags: `vN.0.0` after each phase merge (e.g., `v1.0.0`, `v2.0.0`).

### R5. Makefile Targets (Cumulative)
Every phase adds targets. The Makefile MUST always contain:

```makefile
# === Core ===
make up                  # docker-compose up -d --build
make down                # docker-compose down
make restart             # down + up
make logs                # docker-compose logs -f
make ps                  # docker-compose ps
make clean               # Remove volumes, images, containers

# === Testing ===
make test-all            # Run ALL tests
make test-phase-N        # Run tests for phase N only
make test-coverage       # Run with coverage report

# === Database ===
make db-migrate          # Run EF Core migrations
make db-seed             # Seed test data
make db-reset            # Drop + recreate + migrate + seed

# === Phase-specific (added as phases progress) ===
make whatsapp-status     # Phase 1: Check Baileys session
make ai-test             # Phase 1: Test Gemini connection
make crm-report          # Phase 2: Generate CRM report
# ... etc
```

### R6. Per-Phase Skills
Each phase MUST generate a skill file at `.agents/skills/phase-N/SKILL.md` that explains:
- What was built in the phase.
- How to run, test, and interact with the phase's deliverables.
- Make targets available.
- API endpoints introduced.
- How to verify it's working.

### R7. Environment & Secrets
- All secrets in `.env` file (gitignored).
- `.env.example` committed with placeholder values.
- Docker services read from `.env`.
- No secrets ever hardcoded in code.

---

## Phase 0: Project Scaffolding & DevOps Foundation

**Branch**: `phase/0-project-scaffolding`
**Tag**: `v0.1.0`
**Goal**: Empty but fully runnable infrastructure. Zero application code, but Docker, Makefile, Git, CI, and test harness are fully operational.

### 0.1 Repository Initialization
- [ ] `git init` with `.gitignore` (C#, Node.js, Python, Docker, IDE files).
- [ ] `README.md` with project overview, architecture diagram, and getting-started instructions.
- [ ] `LICENSE` file.
- [ ] `.editorconfig` for consistent formatting.
- [ ] Conventional commit config (`.commitlintrc.yml` or equivalent).

### 0.2 Docker Compose — Infrastructure Services
- [ ] `docker-compose.yml` with:
  - `postgres` (PostgreSQL 16 + pgvector extension).
  - `redis` (Redis 7).
  - `rabbitmq` (RabbitMQ 3.13 with management plugin).
  - `elasticsearch` (Elasticsearch 8.x, single-node).
  - `minio` (S3-compatible object storage).
  - `nginx` (reverse proxy, basic config).
- [ ] `docker-compose.override.yml` for development port mappings.
- [ ] `.env.example` with all required environment variables.
- [ ] Health checks for every service in compose.

### 0.3 Makefile — Base Targets
- [ ] `make up`, `make down`, `make restart`, `make logs`, `make ps`, `make clean`.
- [ ] `make health` — curl health endpoints for all infrastructure services.
- [ ] `make env` — copy `.env.example` to `.env` if not exists.

### 0.4 Python Test Harness
- [ ] `tests/` directory with `conftest.py`.
- [ ] `requirements-test.txt`: `pytest`, `httpx`, `pytest-asyncio`, `pytest-cov`, `python-dotenv`.
- [ ] `make test-setup` — create Python venv and install test deps.
- [ ] `make test-all` — run all tests.
- [ ] `make test-phase-0` — run infrastructure health tests.
- [ ] `tests/phase_0/test_infrastructure.py`:
  - Test PostgreSQL connection and pgvector extension.
  - Test Redis ping.
  - Test RabbitMQ management API.
  - Test Elasticsearch cluster health.
  - Test MinIO bucket creation.

### 0.5 CI Pipeline
- [ ] `.github/workflows/ci.yml` (or equivalent):
  - Lint, build, test on every PR.
  - Uses `docker-compose` to bring up services.
  - Runs `make test-all`.

### 0.6 Phase Skill
- [ ] `.agents/skills/phase-0/SKILL.md` — documents infrastructure setup, make targets, and how to verify.

### Phase 0 Verification Checklist
```bash
make env           # Create .env from template
make up            # All 6 infrastructure services start
make health        # All health checks pass
make test-phase-0  # All 5 infrastructure tests pass
make down          # Clean shutdown
```

---

## Phase 1: Auth, Projects, WhatsApp Gateway & Basic Conversations

**Branch**: `phase/1-core-foundation`
**Tag**: `v1.0.0`
**Goal**: A user can log in, create a project, connect a WhatsApp number, receive messages, and see them in a basic conversation API. Gemini replies with a simple echo-style response. **This is a fully testable MVP.**

### 1.1 ASP.NET Core Backend — Project Scaffold
- [ ] `backend/` directory with ASP.NET Core Web API project.
- [ ] `Dockerfile` for backend.
- [ ] Add `backend` service to `docker-compose.yml`.
- [ ] Modular folder structure:
  ```
  backend/src/
  ├── Modules/
  │   ├── Auth/
  │   ├── Projects/
  │   ├── Users/
  │   ├── RolesPermissions/
  │   ├── WhatsApp/
  │   ├── Conversations/
  │   ├── Messages/
  │   └── AI/
  ├── Shared/
  │   ├── Domain/
  │   ├── Application/
  │   ├── Infrastructure/
  │   ├── Events/
  │   ├── Queue/
  │   ├── Security/
  │   └── Common/
  └── Program.cs
  ```
- [ ] Entity Framework Core setup with PostgreSQL.
- [ ] `make db-migrate`, `make db-seed` targets.

### 1.2 Auth Module
- [ ] JWT authentication with access + refresh tokens.
- [ ] Endpoints:
  - `POST /api/auth/register`
  - `POST /api/auth/login`
  - `POST /api/auth/refresh`
  - `POST /api/auth/logout`
- [ ] Password hashing with BCrypt.
- [ ] Role claim injection into JWT.
- [ ] Middleware for auth validation.

### 1.3 Projects Module
- [ ] CRUD endpoints for Projects.
  - `POST /api/projects`
  - `GET /api/projects`
  - `GET /api/projects/{id}`
  - `PUT /api/projects/{id}`
  - `DELETE /api/projects/{id}`
- [ ] `ProjectSettings` entity with per-project configuration.
- [ ] All queries scoped to `ProjectId`.

### 1.4 Users & Roles Module
- [ ] User CRUD (scoped to project).
- [ ] Predefined roles: Owner, Admin, Supervisor, Agent, AI Reviewer, Analyst.
- [ ] Permission matrix implemented as claims.
- [ ] `GET /api/projects/{id}/users`
- [ ] `POST /api/projects/{id}/users/invite`
- [ ] `PUT /api/users/{id}/role`

### 1.5 WhatsApp Gateway (Node.js Baileys)
- [ ] `whatsapp-gateway/` directory with Node.js project.
- [ ] `Dockerfile` for gateway.
- [ ] Add `whatsapp-gateway` service to `docker-compose.yml`.
- [ ] Features:
  - QR Code login via API.
  - Session persistence to file/Redis.
  - Auto-reconnect.
  - Message receiver → webhook to backend.
  - Message sender API.
  - Media download handler.
  - Delivery/read receipt forwarding.
- [ ] Endpoints:
  - `POST /api/whatsapp/session/start`
  - `GET /api/whatsapp/session/qr`
  - `GET /api/whatsapp/session/status`
  - `POST /api/whatsapp/send`
- [ ] Rate limiter and anti-ban delay controller.
- [ ] `make whatsapp-status` target.

### 1.6 Conversations Module
- [ ] `Conversations` entity with states (Open, Pending, Resolved, Closed).
- [ ] `Messages` entity linked to conversation.
- [ ] Webhook endpoint to receive messages from Baileys:
  - `POST /api/webhooks/whatsapp/message`
- [ ] Auto-create conversation on first message from unknown number.
- [ ] Auto-create customer record on first message.
- [ ] Endpoints:
  - `GET /api/projects/{id}/conversations`
  - `GET /api/conversations/{id}/messages`
  - `POST /api/conversations/{id}/messages` (agent reply)

### 1.7 Messages Module & Message Aggregator
- [ ] Save all incoming messages (text, image, voice, document).
- [ ] Media files saved to MinIO via the Shared Media module.
- [ ] **Message Aggregator**:
  - Redis-based aggregation window (3-10 seconds).
  - Collects consecutive messages from same sender.
  - Emits `MessageAggregated` event to RabbitMQ when window closes.

### 1.8 Basic AI Module — Gemini 3.5 Flash
- [ ] Gemini API connector (HTTP client to Google AI API).
- [ ] Context builder: takes aggregated messages + customer info.
- [ ] Sends text/image/voice directly to Gemini 3.5 Flash.
- [ ] Receives reply and intent classification.
- [ ] **AI Worker** (background service):
  - Consumes `MessageAggregated` from RabbitMQ.
  - Builds context.
  - Calls Gemini.
  - Produces `AIReplyGenerated` event.
- [ ] Reply sender:
  - Consumes `AIReplyGenerated`.
  - Sends reply via Baileys gateway.
- [ ] `make ai-test` — test Gemini connectivity.

### 1.9 Basic CRM (Customer Profiles)
- [ ] `Customers` table scoped to ProjectId.
- [ ] Auto-populated from WhatsApp contact info.
- [ ] Basic fields: Name, Phone, City, Tags, Notes, LeadScore, CreatedAt.
- [ ] `GET /api/projects/{id}/customers`
- [ ] `GET /api/customers/{id}`
- [ ] `PUT /api/customers/{id}`

### 1.10 Basic Follow-up
- [ ] `FollowUps` table with Due Date, Status (Pending/Done/Missed), linked to Customer.
- [ ] Manual follow-up creation by agent.
- [ ] `POST /api/customers/{id}/follow-ups`
- [ ] `GET /api/projects/{id}/follow-ups?status=pending`
- [ ] Background job to check overdue follow-ups and mark as Missed.

### 1.11 Phase 1 — Python Tests
- [ ] `tests/phase_1/test_auth.py` — register, login, refresh, protected endpoints.
- [ ] `tests/phase_1/test_projects.py` — CRUD + isolation.
- [ ] `tests/phase_1/test_users_roles.py` — invite, assign role, permissions.
- [ ] `tests/phase_1/test_whatsapp_gateway.py` — session lifecycle, send/receive mock.
- [ ] `tests/phase_1/test_conversations.py` — webhook ingestion, conversation creation, message listing.
- [ ] `tests/phase_1/test_message_aggregator.py` — aggregation window, event emission.
- [ ] `tests/phase_1/test_ai_gemini.py` — mock Gemini call, context building, reply generation.
- [ ] `tests/phase_1/test_crm.py` — customer auto-creation, profile CRUD.
- [ ] `tests/phase_1/test_follow_ups.py` — creation, listing, overdue detection.
- [ ] `make test-phase-1`

### 1.12 Phase 1 Skill & Documentation
- [ ] `.agents/skills/phase-1/SKILL.md`
- [ ] Update `README.md` with Phase 1 API docs.
- [ ] Update `Makefile` with all new targets.
- [ ] `make whatsapp-status`, `make ai-test`, `make db-seed`.

### Phase 1 Verification Checklist
```bash
make up                    # All services + backend + whatsapp-gateway start
make db-migrate            # Database schema created
make db-seed               # Test users, project, sample customers seeded
make test-phase-0          # Infrastructure tests still pass (regression)
make test-phase-1          # All 9 test files pass
make whatsapp-status       # Gateway responds with session info
make ai-test               # Gemini connectivity verified
# Manual: Send a WhatsApp message → see it in API → AI responds
```

---

## Phase 2: AI Intelligence, CRM Auto-Updates, Assignment & Smart Messaging

**Branch**: `phase/2-ai-intelligence`
**Tag**: `v2.0.0`
**Goal**: AI becomes smart — marketing brain, human-like messaging, CRM auto-update, assignment engine, scheduler, and notifications.

### 2.1 AI Marketing Brain
- [ ] `AIMarketingBrain` module:
  - Customer Psychology Analyzer.
  - Buyer Intent Analyzer.
  - Trust Builder logic.
  - Urgency/Curiosity engine.
  - Objection Handler templates.
  - CTA Optimizer.
  - Reply Style Selector (Fast/Casual/Sales/Support/VIP/Complaint/Follow-up).
- [ ] Gemini prompt engineering for marketing-aware responses.
- [ ] Tests: `tests/phase_2/test_ai_marketing_brain.py`

### 2.2 Smart Human-Like Messaging Engine
- [ ] Reply chunking: AI decides whether reply is 1 message or multiple.
- [ ] Smart delay engine: realistic typing delays between chunks.
- [ ] Anti-ban throttling on outgoing messages.
- [ ] `HumanMessagingEngine` service consumed by AI Worker.
- [ ] Tests: `tests/phase_2/test_human_messaging.py`

### 2.3 AI CRM Auto-Updates
- [ ] `CRMAutoUpdateEngine`:
  - AI extracts entities from messages (city, budget, interests, dates).
  - Generates CRM update proposals.
  - `CRMUpdateSuggested` event → RabbitMQ.
- [ ] `CRMWorker`:
  - Consumes update events.
  - Applies low-risk updates immediately.
  - Queues high-risk for approval.
- [ ] CRM Update History table for audit trail.
- [ ] Tests: `tests/phase_2/test_crm_auto_update.py`

### 2.4 AI Intent & Sentiment Analysis
- [ ] Enhanced AI module:
  - Intent detection (inquiry, complaint, purchase, follow-up, greeting).
  - Sentiment analysis (positive, neutral, negative, angry).
  - Entity extraction.
  - Customer classification (hot/warm/cold/lost).
  - Lead scoring model.
- [ ] Tests: `tests/phase_2/test_intent_sentiment.py`

### 2.5 Assignment Engine
- [ ] `AssignmentEngine` module:
  - Agent availability tracking (Redis-backed presence).
  - Workload analyzer.
  - Load balancer (round-robin, least-busy).
  - Priority router (VIP → supervisor, complaint → supervisor, hot lead → sales).
  - Escalation manager (auto-escalate if agent doesn't respond in SLA).
  - Reassignment on agent offline.
- [ ] `POST /api/conversations/{id}/assign`
- [ ] `GET /api/projects/{id}/agents/workload`
- [ ] Tests: `tests/phase_2/test_assignment.py`

### 2.6 Scheduler Engine (Hangfire)
- [ ] Hangfire integration for background job scheduling.
- [ ] Job types:
  - Follow-up execution.
  - Lead score recalculation.
  - Health score recalculation.
  - Customer re-classification.
  - Overdue follow-up detection.
  - WhatsApp session health check.
  - Queue health monitoring.
- [ ] Hangfire Dashboard exposed at `/hangfire` (admin-only).
- [ ] `make scheduler-status` target.
- [ ] Tests: `tests/phase_2/test_scheduler.py`

### 2.7 Notifications Engine
- [ ] `Notifications` module:
  - Realtime alerts via SignalR.
  - Follow-up alerts.
  - Complaint alerts.
  - SLA breach alerts.
  - VIP customer alerts.
  - System alerts.
- [ ] SignalR hub added to backend.
- [ ] `NotificationSettings` per user.
- [ ] Tests: `tests/phase_2/test_notifications.py`

### 2.8 Basic Reports
- [ ] Daily Operations Report (auto-generated by scheduler).
- [ ] Follow-up Report.
- [ ] AI Performance Report.
- [ ] `GET /api/projects/{id}/reports/daily`
- [ ] `GET /api/projects/{id}/reports/follow-ups`
- [ ] `GET /api/projects/{id}/reports/ai`
- [ ] Tests: `tests/phase_2/test_reports.py`

### 2.9 Phase 2 — Tests, Skill & Makefile
- [ ] All test files for Phase 2 (8 test files).
- [ ] `make test-phase-2`
- [ ] `.agents/skills/phase-2/SKILL.md`
- [ ] `make scheduler-status`, `make assignment-report`.

### Phase 2 Verification Checklist
```bash
make up
make test-phase-0          # Regression
make test-phase-1          # Regression
make test-phase-2          # All 8 new test files pass
make scheduler-status      # Hangfire responds
# Manual: Send WhatsApp message → AI replies in human-like chunks
# Manual: Verify CRM auto-updated after customer mentions a city
```

---

## Phase 3: Company Brain, Knowledge Base, Workflows & Approval System

**Branch**: `phase/3-knowledge-workflows`
**Tag**: `v3.0.0`
**Goal**: AI gets project-specific intelligence via Knowledge Base and Company Brain. Workflows automate business logic. Approval system protects critical actions.

### 3.1 AI Company Brain
- [ ] `AICompanyBrain` module:
  - Knowledge sync from project APIs.
  - Company memory storage (pgvector embeddings).
  - Semantic graph for entity relationships.
  - AI retrieval layer (RAG pipeline).
- [ ] Integration with Gemini context building.
- [ ] `POST /api/projects/{id}/brain/sync`
- [ ] `GET /api/projects/{id}/brain/search?q=...`
- [ ] Tests: `tests/phase_3/test_company_brain.py`

### 3.2 Knowledge Base Engine
- [ ] `KnowledgeBase` module:
  - Documents, FAQs, Services, Pricing Rules, Policies.
  - Objection handling templates.
  - Approved reply templates.
- [ ] Embedding generation pipeline (pgvector).
- [ ] Semantic search endpoint.
- [ ] Knowledge suggestion from conversations (AI-generated).
- [ ] Knowledge Approval workflow (pending → admin review → published).
- [ ] Knowledge versioning.
- [ ] CRUD endpoints:
  - `POST /api/projects/{id}/knowledge`
  - `GET /api/projects/{id}/knowledge`
  - `GET /api/projects/{id}/knowledge/search?q=...`
  - `PUT /api/knowledge/{id}/approve`
  - `PUT /api/knowledge/{id}/reject`
- [ ] Tests: `tests/phase_3/test_knowledge_base.py`

### 3.3 Workflow Engine
- [ ] `Workflows` module:
  - AI Workflow Builder (AI suggests workflows from patterns).
  - Trigger engine (event-based triggers).
  - Condition engine (if/else logic).
  - Action executor (CRM update, send message, create follow-up, assign, tag).
  - Delay engine.
  - Workflow versioning.
  - Workflow safety (approval required for bulk actions).
- [ ] CRUD endpoints:
  - `POST /api/projects/{id}/workflows`
  - `GET /api/projects/{id}/workflows`
  - `PUT /api/workflows/{id}`
  - `POST /api/workflows/{id}/activate`
  - `POST /api/workflows/{id}/deactivate`
- [ ] `WorkflowWorker` background service consuming trigger events.
- [ ] Tests: `tests/phase_3/test_workflows.py`

### 3.4 AI Action & Approval System
- [ ] `Approvals` module:
  - Risk Analyzer service.
  - Risk levels: Low (auto-execute), Medium (execute + audit), High (admin approval), Critical (block + notify).
  - Approval queue.
  - Admin approval UI endpoints.
- [ ] Endpoints:
  - `GET /api/projects/{id}/approvals?status=pending`
  - `POST /api/approvals/{id}/approve`
  - `POST /api/approvals/{id}/reject`
  - `POST /api/approvals/{id}/edit`
- [ ] All AI actions routed through Risk Analyzer.
- [ ] Tests: `tests/phase_3/test_approvals.py`

### 3.5 Integration Layer
- [ ] `Integrations` module:
  - Project API Connector (configurable per project).
  - Auth token manager.
  - Sync services: Customers, Services, Pricing, Orders.
  - Webhook dispatcher.
  - Sync scheduler (periodic sync via Hangfire).
- [ ] `POST /api/projects/{id}/integrations`
- [ ] `POST /api/projects/{id}/integrations/{id}/sync`
- [ ] Tests: `tests/phase_3/test_integrations.py`

### 3.6 Customer Memory
- [ ] `CustomerMemory` module:
  - Preferences, Important Facts, Objections, Purchase Intent.
  - Previous Interests, Follow-up History.
  - Conversation summaries.
  - Long-term summary (AI-generated).
- [ ] AI automatically updates memory after each conversation.
- [ ] Memory included in Gemini context.
- [ ] Tests: `tests/phase_3/test_customer_memory.py`

### 3.7 Phase 3 — Tests, Skill & Makefile
- [ ] All test files for Phase 3 (6 test files).
- [ ] `make test-phase-3`
- [ ] `.agents/skills/phase-3/SKILL.md`
- [ ] `make brain-sync`, `make knowledge-search`, `make approval-queue`.

### Phase 3 Verification Checklist
```bash
make up
make test-phase-0          # Regression
make test-phase-1          # Regression
make test-phase-2          # Regression
make test-phase-3          # All 6 new test files pass
make brain-sync            # Company brain syncs from project API
make knowledge-search      # Semantic search returns results
make approval-queue        # Approval queue is empty/shows pending items
```

---

## Phase 4: Campaigns, Advanced Analytics & Reporting

**Branch**: `phase/4-campaigns-analytics`
**Tag**: `v4.0.0`
**Goal**: Marketing campaigns via WhatsApp with anti-ban, full analytics engine, and executive-level reporting.

### 4.1 Campaign Engine
- [ ] `Campaigns` module:
  - Audience builder (filter by tags, segments, status, score).
  - Campaign scheduler (immediate, scheduled, recurring).
  - AI message generator (Gemini generates campaign copy).
  - Anti-ban sender (throttled sending with random delays).
  - Delivery tracker.
  - Response tracker.
  - A/B testing (2 variants, random split, measure conversion).
- [ ] Endpoints:
  - `POST /api/projects/{id}/campaigns`
  - `GET /api/projects/{id}/campaigns`
  - `GET /api/campaigns/{id}`
  - `POST /api/campaigns/{id}/schedule`
  - `POST /api/campaigns/{id}/pause`
  - `GET /api/campaigns/{id}/results`
- [ ] `CampaignWorker` background service.
- [ ] Tests: `tests/phase_4/test_campaigns.py`

### 4.2 Advanced Analytics Engine
- [ ] `Analytics` module:
  - Customer analytics (acquisition, retention, churn risk).
  - Sales analytics (funnel, conversion, revenue).
  - Complaint analytics (volume, categories, resolution time).
  - Team analytics (response time, resolution rate, workload).
  - AI analytics (accuracy, handoff rate, cost per conversation).
  - Campaign analytics (delivery, open, response, conversion).
  - Follow-up analytics (completion rate, effectiveness).
  - Predictive analytics (churn prediction, upsell opportunity).
- [ ] Analytics snapshots stored for historical trending.
- [ ] `GET /api/projects/{id}/analytics/{type}`
- [ ] Tests: `tests/phase_4/test_analytics.py`

### 4.3 Advanced Reports System
- [ ] Report types:
  - Daily Operations Report.
  - Weekly Summary Report.
  - Complaint Report.
  - Lost Customers Report.
  - Follow-up Report.
  - AI Performance Report.
  - Campaign Report.
  - Executive Insights Report.
- [ ] Auto-generated by scheduler (daily/weekly).
- [ ] `GET /api/projects/{id}/reports`
- [ ] `GET /api/reports/{id}`
- [ ] `POST /api/projects/{id}/reports/generate`
- [ ] Tests: `tests/phase_4/test_advanced_reports.py`

### 4.4 Customer Segmentation & Pipeline
- [ ] `CRM` module extensions:
  - Customer segmentation (dynamic segments based on tags, scores, activity).
  - Pipeline management (stages: New → Contacted → Qualified → Proposal → Negotiation → Won/Lost).
  - Opportunity/Deal management.
  - Relationship graph.
- [ ] `GET /api/projects/{id}/segments`
- [ ] `GET /api/projects/{id}/pipelines`
- [ ] Tests: `tests/phase_4/test_crm_advanced.py`

### 4.5 Elasticsearch Integration
- [ ] Index conversations, messages, customers, notes into Elasticsearch.
- [ ] Full-text search endpoints:
  - `GET /api/projects/{id}/search?q=...&type=conversations`
  - `GET /api/projects/{id}/search?q=...&type=customers`
- [ ] Background indexer worker.
- [ ] Tests: `tests/phase_4/test_search.py`

### 4.6 Phase 4 — Tests, Skill & Makefile
- [ ] All test files for Phase 4 (5 test files).
- [ ] `make test-phase-4`
- [ ] `.agents/skills/phase-4/SKILL.md`
- [ ] `make campaign-status`, `make analytics-dashboard`, `make search-reindex`.

### Phase 4 Verification Checklist
```bash
make up
make test-phase-0 test-phase-1 test-phase-2 test-phase-3   # Regression
make test-phase-4          # All 5 new test files pass
make campaign-status       # Campaign engine responds
make search-reindex        # Elasticsearch indices created
```

---

## Phase 5: Shared Assets, Media Engine & Audit Trail

**Branch**: `phase/5-media-audit`
**Tag**: `v5.0.0`
**Goal**: Centralized asset management, media transformation pipeline, and comprehensive audit logging.

### 5.1 Shared Asset Management System
- [ ] `Media` module:
  - Asset Registry (central database for all files).
  - Asset metadata (dimensions, duration, MIME type, hash).
  - File references (no duplication — store once, reference everywhere).
  - Secure URL generation (signed URLs with expiry).
  - Asset versioning.
  - AI media tagging (Gemini classifies uploaded media).
- [ ] MinIO integration for actual file storage.
- [ ] Endpoints:
  - `POST /api/assets/upload`
  - `GET /api/assets/{id}`
  - `GET /api/assets/{id}/download`
  - `GET /api/assets/{id}/thumbnail`
  - `DELETE /api/assets/{id}`
- [ ] Tests: `tests/phase_5/test_assets.py`

### 5.2 Media Transformation Engine
- [ ] Thumbnail generator.
- [ ] Image resize/compression.
- [ ] Format conversion.
- [ ] WhatsApp optimization (compress for WhatsApp limits).
- [ ] `MediaWorker` background service.
- [ ] Tests: `tests/phase_5/test_media_transform.py`

### 5.3 Audit & System Logging
- [ ] `Audit` module:
  - All API requests logged with user, action, timestamp, IP.
  - All AI decisions logged with input/output.
  - All CRM changes logged with before/after values.
  - All approval actions logged.
- [ ] Serilog structured logging.
- [ ] Audit search via Elasticsearch.
- [ ] `GET /api/projects/{id}/audit?action=...&user=...&from=...&to=...`
- [ ] Tests: `tests/phase_5/test_audit.py`

### 5.4 System Health & Monitoring
- [ ] `SystemHealth` module:
  - API health checks (`/health`).
  - Worker health checks.
  - RabbitMQ queue depth monitoring.
  - Redis connection monitoring.
  - PostgreSQL connection pool monitoring.
  - WhatsApp session health.
  - Gemini API latency monitoring.
- [ ] `GET /api/system/health`
- [ ] `GET /api/system/metrics`
- [ ] Alerting on critical failures (via notification engine).
- [ ] Tests: `tests/phase_5/test_system_health.py`

### 5.5 Phase 5 — Tests, Skill & Makefile
- [ ] All test files for Phase 5 (4 test files).
- [ ] `make test-phase-5`
- [ ] `.agents/skills/phase-5/SKILL.md`
- [ ] `make asset-stats`, `make audit-report`, `make system-health`.

### Phase 5 Verification Checklist
```bash
make up
make test-phase-0 test-phase-1 test-phase-2 test-phase-3 test-phase-4  # Regression
make test-phase-5          # All 4 new test files pass
make system-health         # All health checks green
make asset-stats           # Asset registry responds
make audit-report          # Audit trail queryable
```

---

## Phase 6: Frontend Dashboard, Realtime & Production Hardening

**Branch**: `phase/6-frontend-production`
**Tag**: `v6.0.0`
**Goal**: Production-ready web dashboard with realtime inbox, full CRM UI, and deployment automation.

### 6.1 Frontend Scaffold (React/Next.js)
- [ ] `frontend/` directory with Next.js project.
- [ ] `Dockerfile` for frontend.
- [ ] Add `frontend` service to `docker-compose.yml`.
- [ ] Design system: consistent colors, typography, spacing.
- [ ] Auth flow: login, register, refresh token handling.
- [ ] Layout: sidebar navigation, header, main content.

### 6.2 Dashboard Page
- [ ] KPI cards (new customers, open conversations, pending follow-ups, active campaigns).
- [ ] Activity feed (recent events).
- [ ] Quick actions (new conversation, new follow-up, new campaign).

### 6.3 Inbox Page (Realtime)
- [ ] 3-panel layout: Conversation List | Chat Window | Customer Panel.
- [ ] SignalR integration for live message updates.
- [ ] Filters: status, priority, assigned agent.
- [ ] Agent presence indicators.
- [ ] Message composer with media upload.
- [ ] AI suggestion display.

### 6.4 CRM Pages
- [ ] Customer list with search and filters.
- [ ] Customer profile page (timeline, tags, scores, follow-ups, notes).
- [ ] Pipeline board (Kanban-style drag-and-drop).

### 6.5 Management Pages
- [ ] Follow-ups list.
- [ ] Campaigns list & create/edit.
- [ ] Reports viewer.
- [ ] Workflows list & builder.
- [ ] Knowledge Base manager.
- [ ] Approval queue.
- [ ] Settings (project, WhatsApp, users, roles).

### 6.6 Production Hardening
- [ ] Nginx SSL termination (Let's Encrypt).
- [ ] Rate limiting on API.
- [ ] CORS configuration.
- [ ] Compression (gzip/brotli).
- [ ] Database connection pooling optimized.
- [ ] Redis connection pooling.
- [ ] Error handling middleware (structured error responses).
- [ ] Request/response logging.

### 6.7 Deployment Automation
- [ ] `deploy/` directory with:
  - `docker-compose.production.yml`
  - `setup-server.sh` (installs Docker, clones repo, configures firewall).
  - `backup.sh` (PostgreSQL dump, Redis snapshot, MinIO sync).
  - `restore.sh` (reverse of backup).
- [ ] `make deploy` — production build + up.
- [ ] `make backup` — full backup.
- [ ] `make restore` — full restore.

### 6.8 Phase 6 — Tests, Skill & Makefile
- [ ] `tests/phase_6/test_frontend_api.py` — test frontend API calls.
- [ ] `tests/phase_6/test_signalr.py` — test realtime connections.
- [ ] `tests/phase_6/test_production.py` — test rate limiting, CORS, SSL.
- [ ] `tests/phase_6/test_deployment.py` — test backup/restore scripts.
- [ ] `make test-phase-6`
- [ ] `.agents/skills/phase-6/SKILL.md`

### Phase 6 Verification Checklist
```bash
make up
make test-all              # ALL phase tests pass (0 through 6)
make deploy                # Production build succeeds
make backup                # Backup completes
make system-health         # All systems green
make test-coverage         # Coverage report generated
```

---

## Summary — All Phases at a Glance

| Phase | Name | Services Added | Test Files | Makefile Targets Added | Tag |
|-------|------|---------------|------------|----------------------|-----|
| 0 | Project Scaffolding | postgres, redis, rabbitmq, elasticsearch, minio, nginx | 1 | 8 | v0.1.0 |
| 1 | Core Foundation | backend, whatsapp-gateway | 9 | 6 | v1.0.0 |
| 2 | AI Intelligence | (internal modules) | 8 | 3 | v2.0.0 |
| 3 | Knowledge & Workflows | (internal modules + workers) | 6 | 4 | v3.0.0 |
| 4 | Campaigns & Analytics | (internal modules + workers) | 5 | 4 | v4.0.0 |
| 5 | Media & Audit | (internal modules + workers) | 4 | 4 | v5.0.0 |
| 6 | Frontend & Production | frontend | 4 | 4 | v6.0.0 |
| **Total** | | **~10 Docker services** | **37 test files** | **~33 targets** | |

---

## Git Workflow Summary

```text
main ─────────────────────────────────────────────────────────────►
  │           │           │           │           │           │
  └─ phase/0 ─┘           │           │           │           │
              └─ phase/1 ─┘           │           │           │
                          └─ phase/2 ─┘           │           │
                                      └─ phase/3 ─┘           │
                                                  └─ phase/4 ─┘
                                                              └─ phase/5 ─► phase/6 ─► main
```

Each phase branch merges to `main` with a version tag. All previous tests must pass before merge.
