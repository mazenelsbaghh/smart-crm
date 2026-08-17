# Tasks: Autonomous Facebook Ads Manager

**Input**: `specs/033-facebook-ads-manager/{spec,plan,research,data-model,quickstart}.md` and `contracts/`

**Tests**: Required because this feature can mutate real advertising spend.

## Phase 1: Setup

- [x] T001 Create the Advertising module folders and assembly namespaces in `backend/src/Modules/Advertising/`
- [x] T002 [P] Add Meta Ads, safety reserve and mock-provider configuration bindings in `backend/src/Modules/Advertising/Services/AdvertisingOptions.cs`
- [x] T003 [P] Add the frontend feature package skeleton in `frontend/src/packages/ad-manager/`
- [x] T004 Register Advertising configuration, typed clients, services and jobs in `backend/Program.cs`
- [x] T005 Replace committed Facebook secret defaults, set Graph v25.0 and document persistent token-key configuration in `docker-compose.yml` and `.env.example`

## Phase 2: Foundational

- [x] T006 Create project authorization and role enforcement service in `backend/src/Shared/Security/ProjectAuthorizationService.cs`
- [x] T007 Apply authenticated project authorization to Advertising endpoints through `backend/src/Modules/Advertising/API/AdvertisingControllerBase.cs`
- [x] T008 Create transactional outbox/inbox entities and dispatcher in `backend/src/Shared/Domain/IntegrationOutboxMessage.cs`, `backend/src/Shared/Domain/IntegrationInboxReceipt.cs`, and `backend/src/Shared/Queue/IntegrationOutboxDispatcher.cs`
- [x] T009 Define versioned cross-module Advertising integration event records in `backend/src/Shared/Queue/AdvertisingIntegrationEvents.cs`
- [x] T010 Create common Advertising enums/value objects and fail-closed state machines in `backend/src/Modules/Advertising/Domain/AdvertisingPrimitives.cs`
- [x] T011 Register Advertising and outbox DbSets, tenant filters, precision, unique indexes and concurrency in `backend/src/Shared/Infrastructure/AppDbContext.cs`
- [x] T012 Generate the consolidated EF migration in `backend/Migrations/*_AddFacebookAdsManager.cs`
- [x] T013 Create structured Advertising errors and secret/PII-safe logging helpers in `backend/src/Modules/Advertising/Services/AdvertisingErrors.cs`
- [x] T014 Create a Development/Test-only fake Meta HTTP handler in `backend/src/Modules/Advertising/Infrastructure/Facebook/FakeMetaAdsHandler.cs`
- [x] T015 Create the backend unit test project and common fixtures in `backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj`

## Phase 3: User Story 1 — Connect and authorize Facebook advertising (P1)

**Independent test**: connect mock resources, create an envelope, and prove activation is blocked for wrong role, project, placement, currency or unhealthy tracking.

- [x] T016 [P] [US1] Write connection, tenant and envelope tests in `backend/tests/Advertising.UnitTests/ProjectAuthorizationTests.cs` and `backend/tests/Advertising.UnitTests/BudgetAndPlacementTests.cs`
- [x] T017 [US1] Create connection and autonomy entities in `backend/src/Modules/Advertising/Domain/AdvertisingConnection.cs`
- [x] T018 [US1] Implement encrypted token/webhook-secret vault in `backend/src/Modules/Advertising/Services/AdvertisingSecretVault.cs`
- [x] T019 [US1] Implement versioned Meta REST client, error mapping and Facebook-only capability registry in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs`
- [x] T020 [US1] Implement server-side single-use OAuth state and callback storage in `backend/src/Modules/Advertising/Services/FacebookAdsOAuthService.cs`
- [x] T021 [US1] Implement resource discovery, mutual-eligibility and permission health checks in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs` and `backend/src/Modules/Advertising/Services/AdvertisingReadinessService.cs`
- [x] T022 [US1] Implement envelope validation, activation, suspension and revocation in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs` and `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs`
- [x] T023 [US1] Expose connection, resource, readiness and envelope APIs in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs`
- [x] T024 [US1] Add mock HTTP acceptance coverage for connection and project isolation in `tests/phase_1/test_facebook_ads_manager.py`

## Phase 4: User Story 2 — Build a sourced launch plan (P1)

**Independent test**: published knowledge produces cited eligible offers/funnel; missing, stale or contradictory commercial facts block launch.

- [x] T025 [P] [US2] Write sourced profile and hallucination-blocking tests in `backend/tests/Advertising.UnitTests/AdvertisingProfileTests.cs`
- [x] T026 [US2] Create profile, fact-source, offer and promotion entities in `backend/src/Modules/Advertising/Domain/AdvertisingProfile.cs`
- [x] T027 [US2] Implement published-knowledge projection consumer and staleness handling in `backend/src/Modules/Advertising/Workers/KnowledgeProjectionConsumer.cs`
- [x] T028 [US2] Implement evidence-bound profile extraction and funnel selection in `backend/src/Modules/Advertising/Services/AdvertisingProfileExtractor.cs`
- [x] T029 [US2] Implement launch readiness and plan generation in `backend/src/Modules/Advertising/Services/AdvertisingReadinessService.cs` and `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs`
- [x] T030 [US2] Expose profile, offers and launch-plan APIs in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs`

## Phase 5: User Story 3 — Create and test multiple Facebook ads (P1)

**Independent test**: rank Page posts/images/videos, exclude ineligible sources, choose a cap-viable test count and create only Facebook placements initially paused.

- [x] T031 [P] [US3] Write creative ranking, variant and placement tests in `backend/tests/Advertising.UnitTests/CreativeAndPlacementTests.cs`
- [x] T032 [US3] Create creative/variant and managed delivery entities in `backend/src/Modules/Advertising/Domain/AdvertisingCreative.cs`
- [x] T033 [US3] Implement Page post discovery and existing-post creative payloads in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs`
- [x] T034 [US3] Implement project asset projection, rights/format checks and source invalidation in `backend/src/Modules/Advertising/Workers/MediaProjectionConsumer.cs`
- [x] T035 [US3] Implement offer-safe creative scoring and recommendation in `backend/src/Modules/Advertising/Services/CreativeRankingService.cs`
- [x] T036 [US3] Implement ImageSharp/FFmpeg-backed allowed derivative jobs with MinIO output in `backend/src/Modules/Advertising/Jobs/CreativeVariantJob.cs`
- [x] T037 [US3] Implement cap-aware experiment sizing and paused campaign/ad-set/ad creation in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs`
- [x] T038 [US3] Expose creative-source, recommendation and activation APIs in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs`

## Phase 6: User Story 4 — Allocate cap and scale winners (P1)

**Independent test**: concurrent allocation never exceeds usable cap; low evidence returns WAIT; increases respect maximum/cooldown; losers pause without deletion.

- [x] T039 [P] [US4] Write allocation, concurrency, cooldown and sparse-evidence tests in `backend/tests/Advertising.UnitTests/BudgetAndPlacementTests.cs`
- [x] T040 [US4] Create budget ledger/allocation and insight snapshot entities in `backend/src/Modules/Advertising/Domain/AdvertisingBudget.cs`
- [x] T041 [US4] Implement atomic ledger reservation, safety reserve and release rules in `backend/src/Modules/Advertising/Services/BudgetAllocator.cs`
- [x] T042 [US4] Implement incremental Meta Insights retrieval and normalized evidence windows in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaInsightsClient.cs`
- [x] T043 [US4] Implement deterministic winner/loser/fatigue evidence evaluator in `backend/src/Modules/Advertising/Services/AdvertisingEvidenceService.cs`
- [ ] T044 [US4] Implement daily rebalance, gradual scale, pause and retargeting allocation policies in `backend/src/Modules/Advertising/Services/AllocationPolicyService.cs`

## Phase 7: User Story 5 — Optimize for reliable business conversions (P1)

**Independent test**: signed internal/webhook events dedupe to one conversion; later refund/absence corrects value; matching PII is omitted without consent.

- [x] T045 [P] [US5] Write canonical conversion, correction, signature and consent tests in `backend/tests/Advertising.UnitTests/ConversionLedgerTests.cs`
- [x] T046 [US5] Create attribution, source event, canonical conversion, adjustment and delivery entities in `backend/src/Modules/Advertising/Domain/AdvertisingConversion.cs`
- [x] T047 [US5] Implement HMAC validation, replay prevention and generic conversion intake in `backend/src/Modules/Advertising/Services/ConversionIngressService.cs`
- [x] T048 [US5] Implement canonical deduplication, strength ordering, late attribution and negative adjustments in `backend/src/Modules/Advertising/Services/ConversionLedgerService.cs`
- [x] T049 [US5] Emit transactional deal/booking/payment/attendance events from `backend/src/Modules/CRM/` and `backend/src/Modules/GroupAppointments/`
- [x] T050 [US5] Consume internal business outcomes and qualified-message classifications in `backend/src/Modules/Advertising/Workers/BusinessOutcomeConsumer.cs`
- [x] T051 [US5] Implement consent-aware Meta Dataset payload/delivery/retry logic in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs` and `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [x] T052 [US5] Expose signed conversion webhook and conversion reporting APIs in `backend/src/Modules/Advertising/API/AdvertisingConversionsController.cs`
- [x] T053 [US5] Add conversion webhook/dedupe acceptance coverage in `tests/phase_1/test_facebook_ads_conversions.py`

## Phase 8: User Story 6 — Explain and review autonomous decisions (P2)

**Independent test**: approved in-envelope command executes once; sparse pause waits; stale state reconciles; out-of-envelope action never executes.

- [x] T054 [P] [US6] Write AI schema, decision pipeline, stale-state and idempotency tests in `backend/tests/Advertising.UnitTests/DecisionPipelineTests.cs`
- [x] T055 [US6] Create decision, review, command and impact entities in `backend/src/Modules/Advertising/Domain/AdvertisingDecision.cs`
- [x] T056 [US6] Implement closed-schema Strategist/Auditor/Judge clients in `backend/src/Modules/Advertising/Services/AdvertisingDecisionAi.cs`
- [x] T057 [US6] Implement deterministic final Safety Engine in `backend/src/Modules/Advertising/Services/AdvertisingSafetyEngine.cs`
- [x] T058 [US6] Implement durable command claim/send/unknown-result reconciliation in `backend/src/Modules/Advertising/Workers/AdvertisingCommandWorker.cs`
- [x] T059 [US6] Implement decision orchestration and impact review in `backend/src/Modules/Advertising/Services/AdvertisingDecisionService.cs`
- [x] T060 [US6] Expose decision/evidence history in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs`

## Phase 9: User Story 7 — Monitor health and stop safely (P2)

**Independent test**: tracking/connection failures freeze finance; abnormal spend activates Emergency Stop and pauses only system-owned ads; explicit healthy resume is required.

- [x] T061 [P] [US7] Write job lease, financial-freeze and emergency-stop tests in `backend/tests/Advertising.UnitTests/AdvertisingSafetyJobsTests.cs`
- [x] T062 [US7] Create incident, emergency-stop and cycle-run entities in `backend/src/Modules/Advertising/Domain/AdvertisingDecision.cs`
- [x] T063 [US7] Implement project-scoped Redis lease and durable time-bucket guards in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [x] T064 [US7] Implement spend, sync, insights, tracking, decision, impact, fatigue, rebalance, test and strategy jobs in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [x] T065 [US7] Register Cairo-aware recurring dispatchers and outbox publishing in `backend/Program.cs`
- [x] T066 [US7] Implement normal disable, Emergency Stop and guarded resume in `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs`
- [x] T067 [US7] Expose overview, incidents, stop and resume APIs in `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs`
- [ ] T068 [US7] Add tracking-freeze, command-replay and stop acceptance coverage in `tests/phase_1/test_facebook_ads_safety_jobs.py`

## Phase 10: User Story 8 — Dedicated shell workspace (P3)

**Independent test**: desktop/mobile show `حملات واتساب` and `مدير الإعلانات`; readiness and six views are RTL/responsive; project switch clears prior data.

- [x] T069 [P] [US8] Create typed frontend API client and query/state hook in `frontend/src/packages/ad-manager/api/ad-manager-api.ts` and `frontend/src/packages/ad-manager/hooks/use-ad-manager.ts`
- [x] T070 [US8] Consolidate desktop/mobile navigation, rename WhatsApp campaigns and add Ads Manager in `frontend/src/config/navigation.ts`, `frontend/src/packages/inbox/shared/ThinSidebar.tsx`, and `frontend/src/app/(dashboard)/layout.tsx`
- [x] T071 [P] [US8] Build readiness checklist and connection/envelope settings in `frontend/src/packages/ad-manager/components/ReadinessPanel.tsx` and `frontend/src/packages/ad-manager/components/SettingsPanel.tsx`
- [x] T072 [P] [US8] Build health bar, KPI/funnel/allocation overview in `frontend/src/packages/ad-manager/components/AdvertisingOverview.tsx`
- [x] T073 [P] [US8] Build campaign and creative operational views in `frontend/src/packages/ad-manager/components/CampaignsView.tsx` and `frontend/src/packages/ad-manager/components/CreativesView.tsx`
- [x] T074 [P] [US8] Build conversions and decision evidence views in `frontend/src/packages/ad-manager/components/ConversionsView.tsx` and `frontend/src/packages/ad-manager/components/DecisionsView.tsx`
- [x] T075 [US8] Compose URL-addressable RTL workspace with project-reset, loading/error/empty states and Emergency Stop in `frontend/src/app/(dashboard)/management/ad-manager/page.tsx`
- [x] T076 [US8] Add responsive/accessibility styles for 375/768/1024/1440 widths in `frontend/src/packages/ad-manager/AdManager.module.css`

## Phase 11: Polish and cross-cutting verification

- [x] T077 Add structured audit coverage, metric names and secret/PII redaction across `backend/src/Modules/Advertising/`
- [x] T078 Add retention/compaction and project archive/delete handling in `backend/src/Modules/Advertising/Jobs/AdvertisingRetentionJob.cs`
- [x] T079 Run and fix `dotnet build` and `dotnet test` for `backend/backend.csproj` and `backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj`
- [ ] T080 Run and fix focused and regression pytest suites under `tests/phase_1/`, `tests/phase_3/`, `tests/phase_4/`, and `tests/phase_5/`
- [x] T081 Run and fix frontend lint/build and verify RTL/responsive/accessibility in `frontend/`
- [x] T082 Execute the mock-provider quickstart and Emergency Stop recovery drill in `specs/033-facebook-ads-manager/quickstart.md`
- [x] T083 Verify Compose configuration, Graph version, persistent Data Protection keys and absence of committed secrets in `docker-compose.yml` and `.env.example`

## Dependencies

- Phase 2 depends on Phase 1 and blocks every story.
- US1 establishes connection/authorization required for real provider work.
- US2 can build profiles after Phase 2; activation integrates with US1 readiness.
- US3 depends on US1 resources and US2 offer facts.
- US4 depends on US3 managed delivery.
- US5 can ingest after Phase 2 but optimization integration depends on US3 identifiers.
- US6 depends on US1 envelope, US4 evidence and US5 outcomes.
- US7 wraps all command/decision execution and must finish before Autopilot can be enabled.
- US8 may proceed after API contracts stabilize; final activation UI depends on US1–US7.
- Polish depends on all selected stories.

## Parallel opportunities

- T002/T003, then T006/T008/T009/T010/T013/T014 can be developed in separate files.
- Tests marked `[P]` can be authored before their story implementation.
- After foundation, US2 projections and US1 provider connection can proceed independently until readiness integration.
- Within US8, API state, readiness, overview, campaigns/creatives and conversions/decisions components are independent.

## Implementation strategy

The first safe increment is foundation + US1, but it must remain incapable of spend. Add sourced planning/creatives, conversion truth and budget rules before the decision loop. Create provider structures paused, verify tracking and stop controls, then enable the confirmed guarded real-spend canary. Automated tests always use the fake provider; real credentials require the quickstart production checklist.
