# Tasks: Campaigns, Advanced Analytics & Reporting

# Spec Kit Preparation Workflow / سير عمل إعداد عدة المواصفات

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

**Input**: Design documents from `/specs/005-campaigns-analytics/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Python-based integration tests (`tests/phase_4/`).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Create project folders for new modules inside `backend/src/Modules/Campaigns/`, `backend/src/Modules/Analytics/`, `backend/src/Modules/Search/`, and `backend/src/Modules/CRM/`
- [x] T002 Add Elasticsearch C# package dependency `Elastic.Clients.Elasticsearch` (v8) to C# project file `backend/backend.csproj`
- [x] T003 [P] Verify and update local `docker-compose.yml` health check and port mapping (expose 9200) for Elasticsearch

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core database tables, event queues, and dependency registration

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create and run EF Core DbMigration for all Phase 4 tables (`Segments`, `Campaigns`, `CampaignRecipients`, `AnalyticsSnapshots`, `PipelineStages`, `Deals`) via C# project
- [x] T005 Register `ElasticsearchClient` and Elasticsearch connection strings inside `backend/Program.cs`
- [x] T006 [P] Configure RabbitMQ event exchange configurations for search index queues inside `backend/src/Shared/Infrastructure/Queue/QueueConfiguration.cs`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Multi-Channel Campaign Launch & Anti-Ban Delivery (Priority: P1) 🎯 MVP

**Goal**: Segment contacts dynamically, generate AI campaign copy, partition variants for A/B testing, and broadcast with anti-ban throttling.

**Independent Test**: Use pytest to create a segment, launch a campaign, verify random delay dispatching, and check delivery status transitions from Pending to Sent.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T007 [P] [US1] Write campaigns integration test in `tests/phase_4/test_campaigns.py` covering A/B split routing and throttled dispatch checks.

### Implementation for User Story 1

- [x] T008 [P] [US1] Implement Segment database model class in `backend/src/Modules/CRM/Domain/Segment.cs`
- [x] T009 [P] [US1] Implement Campaign database model class in `backend/src/Modules/Campaigns/Domain/Campaign.cs`
- [x] T010 [P] [US1] Implement CampaignRecipient database model class in `backend/src/Modules/Campaigns/Domain/CampaignRecipient.cs`
- [x] T011 [US1] Implement `CampaignAIService` to generate personalized templates using Gemini client in `backend/src/Modules/Campaigns/Application/Services/CampaignAIService.cs`
- [x] T012 [US1] Implement `CampaignSenderJob` background task with random delay scheduler (5-15s) in `backend/src/Modules/Campaigns/Jobs/CampaignSenderJob.cs`
- [x] T013 [US1] Implement Campaign API Controller endpoints for CRUD and scheduling in `backend/src/Modules/Campaigns/API/CampaignsController.cs`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Advanced Analytics Dashboard & Automated Reporting (Priority: P2)

**Goal**: Daily pre-calculated analytics snapshots, reports generation.

**Independent Test**: Verify daily stats snapshot job computes averages and returns them via the analytics API endpoint.

### Tests for User Story 2

- [x] T014 [P] [US2] Write analytics integration test in `tests/phase_4/test_analytics.py` and reports test in `tests/phase_4/test_advanced_reports.py` ensuring snapshots contain aggregated stats.

### Implementation for User Story 2

- [x] T015 [P] [US2] Implement AnalyticsSnapshot database model class in `backend/src/Modules/Analytics/Domain/AnalyticsSnapshot.cs`
- [x] T016 [US2] Implement `AnalyticsEngine` service calculating team response time, AI accuracy, and conversions in `backend/src/Modules/Analytics/Application/Services/AnalyticsEngine.cs`
- [x] T017 [US2] Implement Hangfire daily CRON task `DailyAnalyticsJob` in `backend/src/Modules/Analytics/Jobs/DailyAnalyticsJob.cs`
- [x] T018 [US2] Implement Analytics and Reports API Controller endpoints in `backend/src/Modules/Analytics/API/AnalyticsController.cs`

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - Instant Unified Search (Priority: P3)

**Goal**: Full-text multi-tenant search indexing of contacts, messages, and notes using Elasticsearch.

**Independent Test**: Run search query API with `ProjectId` parameter and verify correct matching documents are returned.

### Tests for User Story 3

- [x] T019 [P] [US3] Write search indexing integration test in `tests/phase_4/test_search.py`.

### Implementation for User Story 3

- [x] T020 [US3] Implement `ElasticsearchIndexerWorker` RabbitMQ event consumer to index entities on modification in `backend/src/Modules/Search/Workers/ElasticsearchIndexerWorker.cs`
- [x] T021 [US3] Implement `SearchService` to query Elasticsearch indices with strict `ProjectId` tenant filters in `backend/src/Modules/Search/Application/Services/SearchService.cs`
- [x] T022 [US3] Implement Search API Controller endpoints in `backend/src/Modules/Search/API/SearchController.cs`

**Checkpoint**: All three user stories should now be independently functional

---

## Phase 6: User Story 4 - CRM Advanced Pipelines (Priority: P4)

**Goal**: CRM opportunity deals tracking and sales pipelines stage transitions.

**Independent Test**: Move a deal between stages and verify order updates.

### Tests for User Story 4

- [x] T023 [P] [US4] Write pipeline and deal management integration test in `tests/phase_4/test_crm_advanced.py`.

### Implementation for User Story 4

- [x] T024 [P] [US4] Implement PipelineStage database model class in `backend/src/Modules/CRM/Domain/PipelineStage.cs`
- [x] T025 [P] [US4] Implement Deal database model class in `backend/src/Modules/CRM/Domain/Deal.cs`
- [x] T026 [US4] Implement pipeline and deal endpoints in `backend/src/Modules/CRM/API/CRMAdvancedController.cs`

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T027 Configure new Makefile targets (`make campaign-status`, `make analytics-dashboard`, `make search-reindex`, `make test-phase-4`) in `Makefile`
- [x] T028 Create Phase 4 documentation skill file in `.agents/skills/phase-4/SKILL.md`
- [x] T029 Run whole validation suite `make test-phase-4` and verify quickstart guide
- [x] T030 Perform code cleanup and DRY audits

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel or sequentially (US1 → US2 → US3 → US4)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- Models within a story marked [P] can run in parallel

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready
