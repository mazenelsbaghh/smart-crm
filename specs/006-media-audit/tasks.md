# Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

# Tasks: Shared Assets, Media Engine & Audit Trail

**Input**: Design documents from `/specs/006-media-audit/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and environment configurations

- [x] T001 Configure backend dependencies in backend/backend.csproj by adding AWSSDK.S3, SixLabors.ImageSharp, Serilog.AspNetCore, and Serilog.Sinks.File packages
- [x] T002 [P] Define MinIO environment variables in .env.example and update docker-compose.yml to expose them to the backend container

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database tables and logging infrastructure

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 Create models Asset and AssetVariant in backend/src/Modules/Media/Domain/Asset.cs
- [x] T004 Create AuditLog model in backend/src/Modules/Audit/Domain/AuditLog.cs
- [x] T005 Register DbSet for Asset, AssetVariant, and AuditLog in backend/src/Shared/Infrastructure/AppDbContext.cs
- [x] T006 Generate and apply Entity Framework Core migrations for the new entities (e.g. AddMediaAndAudit migrations)
- [x] T007 Configure Serilog structured JSON logging in backend/Program.cs

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Central Asset Upload and Retrieval (Priority: P1) 🎯 MVP

**Goal**: Support asset upload to MinIO, deduplication via SHA-256 hashes, and signed download URLs.

**Independent Test**: Upload file via API, verify storage in MinIO, retrieve via signed URL, check registry entry, re-upload same file and verify file reference is reused (no double storage).

### Tests for User Story 1

- [x] T008 [P] [US1] Create integration test tests/phase_5/test_assets.py verifying upload, signed URL download, and file deduplication

### Implementation for User Story 1

- [x] T009 [US1] Implement MinIoStorageService in backend/src/Modules/Media/Services/MinIoStorageService.cs using AWSSDK.S3
- [x] T010 [US1] Implement AssetService in backend/src/Modules/Media/Services/AssetService.cs for cataloging, hashing, and signed URL generation
- [x] T011 [US1] Implement AssetsController in backend/src/Modules/Media/API/AssetsController.cs for HTTP endpoints
- [x] T012 [US1] Register asset services in backend/Program.cs

**Checkpoint**: User Story 1 is fully functional and testable independently

---

## Phase 4: User Story 2 - Automated Media Transformation and WhatsApp Optimization (Priority: P2)

**Goal**: Compressing and resizing uploaded images/videos to optimize for WhatsApp web limits in the background.

**Independent Test**: Upload large image, verify MediaWorker executes resize/thumbnailing, verify retrieval of optimized variants.

### Tests for User Story 2

- [x] T013 [P] [US2] Create integration test tests/phase_5/test_media_transform.py verifying image resizing and thumbnailing

### Implementation for User Story 2

- [x] T014 [US2] Implement ImageTransformer service in backend/src/Modules/Media/Services/ImageTransformer.cs using SixLabors.ImageSharp
- [x] T015 [US2] Implement Hangfire job MediaWorker under backend/src/Modules/Media/Jobs/MediaWorker.cs to execute media transformations in the background
- [x] T016 [US2] Update AssetsController in backend/src/Modules/Media/API/AssetsController.cs to add thumbnail endpoint
- [x] T017 [US2] Register transformation services and schedule MediaWorker server in backend/Program.cs

**Checkpoint**: User Story 2 is fully functional and testable independently

---

## Phase 5: User Story 3 - Full Audit Trail and Decisional Logging (Priority: P3)

**Goal**: Log all API actions, CRM modifications, AI decisions, and approvals to database/Elasticsearch.

**Independent Test**: Perform API request, CRM change, AI action, and query audit trail to see before/after states, matching user IDs, and exact payloads.

### Tests for User Story 3

- [x] T018 [P] [US3] Create integration test tests/phase_5/test_audit.py verifying CRM updates write audit logs to DB/Elasticsearch, and verifying audit search endpoint

### Implementation for User Story 3

- [x] T019 [US3] Implement AuditService in backend/src/Modules/Audit/Services/AuditService.cs to log API, CRM, AI, and approval events
- [x] T020 [US3] Implement AuditController in backend/src/Modules/Audit/API/AuditController.cs to query audit logs
- [x] T021 [US3] Implement AppDbContext save changes hooks or event publishes to automatically intercept auditable entity mutations in backend/src/Shared/Infrastructure/AppDbContext.cs
- [x] T022 [US3] Register audit services in backend/Program.cs

**Checkpoint**: User Story 3 is fully functional and testable independently

---

## Phase 6: User Story 4 - System Health Monitoring and Health Metrics Dashboard (Priority: P4)

**Goal**: Expose system-wide health and telemetry API.

**Independent Test**: Query health and metrics APIs, verify connection status of all infrastructure services.

### Tests for User Story 4

- [x] T023 [P] [US4] Create integration test tests/phase_5/test_system_health.py verifying health and metrics API returns

### Implementation for User Story 4

- [x] T024 [US4] Implement SystemHealthService in backend/src/Modules/SystemHealth/Services/SystemHealthService.cs to query DB, Redis, RabbitMQ, Elasticsearch, and WhatsApp gateways
- [x] T025 [US4] Implement SystemHealthController in backend/src/Modules/SystemHealth/API/SystemHealthController.cs for health and metrics endpoints
- [x] T026 [US4] Register system health services in backend/Program.cs

**Checkpoint**: User Story 4 is fully functional and testable independently

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Build integration and final validation

- [x] T027 Add Phase 5 Makefile targets for asset-stats, audit-report, system-health, and test-phase-5 in Makefile
- [x] T028 Create operations guide .agents/skills/phase-5/SKILL.md
- [x] T029 Update master documentation docs/backend_plan.md and docs/ops_plan.md to document Phase 5 integration
- [x] T030 Run quickstart.md validation check

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
- **Polish (Final Phase)**: Depends on all desired user stories being complete
