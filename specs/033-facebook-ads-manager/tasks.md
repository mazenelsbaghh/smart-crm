# Tasks: Autonomous WhatsApp AI Media Buyer

**Input**: `specs/033-facebook-ads-manager/{spec,plan,research,data-model,quickstart}.md` and `specs/033-facebook-ads-manager/contracts/`

**Tests**: Mandatory. This feature can create external objects, mutate live spend and process customer outcome data. Provider tests use fakes; real-account validation remains a guarded paused canary.

**Task format**: Every task is initially unchecked. `[P]` means it can run in parallel after its phase dependencies. Story labels map to the eight independently testable user stories in `spec.md`.

## Phase 1: Setup and safe baseline

- [X] T001 Inventory all existing Advertising, WhatsApp, Conversations, CRM, Bookings, Media, Brain and shared-infrastructure dependencies in `specs/033-facebook-ads-manager/implementation-inventory.md`
- [X] T002 [P] Write startup rejection and supported Graph-version tests before changing provider defaults in `backend/tests/Advertising.UnitTests/AdvertisingStartupConfigurationTests.cs`
- [X] T003 Add Graph version, mock-provider, safety reserve, tracking-policy and WhatsApp Cloud webhook configuration after T002 passes in `backend/src/Modules/Advertising/Services/AdvertisingOptions.cs`, `.env.example`, and `docker-compose.yml`
- [X] T004 [P] Create the PostgreSQL integration test project/fixture and failing existing-database upgrade test plus WhatsApp gateway test scripts in `backend/tests/Advertising.IntegrationTests/Advertising.IntegrationTests.csproj`, `backend/tests/Advertising.IntegrationTests/PostgresFixture.cs`, `backend/tests/Advertising.IntegrationTests/MigrationTests.cs`, `tests/phase_1/fixtures/advertising/`, and `whatsapp-gateway/package.json`
- [X] T005 [P] Add the RTL Ads Manager frontend package structure in `frontend/src/packages/ad-manager/`
- [X] T006 Register only development/test fake-provider handlers and fail closed when production configuration is incomplete in `backend/Program.cs`
- [X] T007 Verify no committed provider secrets, unsafe defaults, or production auto-activation paths remain in `.env.example`, `docker-compose.yml`, and `backend/src/Modules/Advertising/`

---

## Phase 2: Foundational domain, module boundaries and reliability

**Purpose**: Establish tenant-safe persistence, versioned integration messaging and deterministic invariants that block every story until complete.

- [X] T008 [P] Write primitive, state-transition and WhatsApp-destination invariant tests in `backend/tests/Advertising.UnitTests/AdvertisingPrimitivesTests.cs`
- [X] T009 [P] Write tenant-query-filter, cross-project denial, forbidden cross-module dependency and HTTP mutation-contract tests in `backend/tests/Advertising.UnitTests/AdvertisingTenantIsolationTests.cs`, `backend/tests/Advertising.UnitTests/ModuleBoundaryTests.cs`, and `backend/tests/Advertising.UnitTests/AdvertisingApiContractTests.cs`
- [X] T010 [P] Write integration ordering, stale/gap/replay, async-AI ownership/hash/deadline, and audit retry/dead-letter tests in `backend/tests/Advertising.UnitTests/AdvertisingIntegrationMessagingTests.cs`, `backend/tests/Advertising.UnitTests/AdvertisingAiWorkTests.cs`, and `backend/tests/Advertising.UnitTests/AdvertisingAuditTests.cs`
- [X] T011 Define WhatsApp destination, capability, truth-source, operation, health, ownership and closed autonomous-action primitives in `backend/src/Modules/Advertising/Domain/AdvertisingPrimitives.cs`
- [X] T012 Implement fail-closed state transitions and field-level invariant/equivalence rules in `backend/src/Modules/Advertising/Domain/AdvertisingStateMachine.cs`
- [X] T013 Create the connection, destination, capability, disconnect-operation and disconnect-target aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingConnection.cs`
- [X] T014 Create sourced profile, fact, offer, envelope and envelope-grant aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingProfile.cs`
- [X] T015 Create immutable campaign plan, audience strategy and plan-creative aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingPlan.cs`
- [X] T016 Create experiment, arm and evaluation aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingExperiment.cs`
- [X] T017 Create provider hierarchy, ownership, operation, snapshot and validation-finding aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingDelivery.cs`
- [X] T018 Create creative and variant aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingCreative.cs`
- [X] T019 Create multi-period budget ledger, allocation, debit and revisioned insights aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingBudget.cs`
- [X] T020 Create attribution, canonical conversion, adjustment, delivery, attempt and webhook-source aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingConversion.cs`
- [X] T021 Create tracking policy/snapshot, AI work item, decision, review, command, impact, incident, stop, disable, audit and cycle aggregates in `backend/src/Modules/Advertising/Domain/AdvertisingDecision.cs`
- [X] T022 Register all Advertising DbSets, tenant filters, money precision, concurrency tokens, ownership keys and uniqueness constraints in `backend/src/Shared/Infrastructure/AppDbContext.cs`
- [X] T023 Generate an additive consolidated schema migration with no provider calls or destructive legacy removal in `backend/Migrations/*_RebuildWhatsAppAiMediaBuyer.cs`
- [X] T024 Define versioned cross-module event contracts without credentials in `backend/src/Shared/Queue/AdvertisingIntegrationEvents.cs`
- [X] T025 Generalize the transactional outbox dispatcher to registered handlers instead of a closed switch in `backend/src/Shared/Queue/IntegrationOutboxDispatcher.cs`
- [X] T026 Implement monotonic source-version, gap recovery, tombstone and poison-message behavior in `backend/src/Shared/Queue/IntegrationProjectionConsumer.cs`
- [X] T027 Implement versioned project-context, knowledge, media, consent and WhatsApp-route consumers before enabling producers in `backend/src/Modules/Advertising/Workers/ProjectContextProjectionConsumer.cs`, `backend/src/Modules/Advertising/Workers/KnowledgeProjectionConsumer.cs`, `backend/src/Modules/Advertising/Workers/MediaProjectionConsumer.cs`, and `backend/src/Modules/WhatsApp/Workers/WhatsAppInboundRouteConsumer.cs`
- [X] T028 Implement resumable projection backfill, watermarks and parity proof before cutover in `backend/src/Modules/Advertising/Jobs/AdvertisingProjectionBackfillJob.cs`
- [X] T029 After parity, enable transactional producers, shared storage and guarded async-AI request/results, then remove every direct Advertising cross-module read/service registration in `backend/src/Modules/Projects/API/ProjectController.cs`, `backend/src/Modules/Brain/Services/KnowledgeBaseService.cs`, `backend/src/Modules/Media/Services/AssetService.cs`, `backend/src/Modules/CRM/API/CRMController.cs`, `backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs`, `backend/src/Modules/Advertising/Services/FacebookPageTokenResolver.cs`, `backend/src/Modules/Advertising/Services/ProjectAiConfigurationProvider.cs`, `backend/src/Modules/Advertising/Services/AdvertisingDecisionAi.cs`, `backend/src/Shared/Storage/IObjectStorage.cs`, `backend/src/Modules/Media/Services/MinIoStorageService.cs`, `backend/src/Modules/AI/Workers/AdvertisingAiWorkConsumer.cs`, `backend/src/Modules/Advertising/Workers/AdvertisingAiWorkResultConsumer.cs`, and `backend/src/Modules/Advertising/Jobs/CreativeVariantJob.cs`
- [X] T030 Add project authorization, system-autopilot actor, idempotency, If-Match, 202 operation and sanitized-error helpers in `backend/src/Shared/Security/ProjectAuthorizationService.cs`, `backend/src/Modules/Advertising/API/AdvertisingControllerBase.cs`, and `backend/src/Modules/Advertising/Services/AdvertisingErrors.cs`
- [X] T031 Persist/index audit records with retry/dead-letter handling and register every Foundation consumer, async-AI worker, backfill job, audit indexer and subscription before the checkpoint in `backend/src/Modules/Advertising/Services/AdvertisingAuditService.cs`, `backend/src/Shared/Audit/ElasticsearchAuditIndexer.cs`, and `backend/Program.cs`

**Checkpoint**: Migration applies on an existing database, manual external campaigns remain untouched, every new record is tenant-scoped, and duplicate/out-of-order integration events are deterministic.

---

## Phase 3: User Story 1 — Connect and authorize Meta advertising (P1)

**Independent test**: An Owner connects an eligible ad account, Page, WABA, phone and Dataset, approves a bounded envelope, and activation is blocked for any mismatched resource, missing permission, stale capability, unsupported optimization or non-Owner/Admin caller.

- [X] T032 [P] [US1] Write OAuth state, replay, callback, secret-redaction and connection API acceptance scenarios in `backend/tests/Advertising.UnitTests/MetaOAuthAndSecretsTests.cs` and `tests/phase_1/test_whatsapp_ads_connection.py`
- [X] T033 [P] [US1] Write live-capability, mutual-resource-eligibility and stale-snapshot tests in `backend/tests/Advertising.UnitTests/AdvertisingCapabilityTests.cs`
- [X] T034 [P] [US1] Write envelope hard-control, offer-destination grant, currency, timezone and authority tests in `backend/tests/Advertising.UnitTests/AutonomyEnvelopeTests.cs`
- [X] T035 [P] [US1] Write disconnect/revoke crash-resume and LeaveRunning acknowledgement tests in `backend/tests/Advertising.UnitTests/AdvertisingDisconnectTests.cs`
- [X] T036 [US1] Implement encrypted, versioned and revocable provider credential storage in `backend/src/Modules/Advertising/Services/AdvertisingSecretVault.cs`
- [X] T037 [US1] Implement global single-use OAuth state and callback recovery without callback JWT in `backend/src/Modules/Advertising/Services/MetaAdsOAuthService.cs`
- [X] T038 [US1] Expose authenticated OAuth start in the project controller and a separate anonymous global callback derived only from single-use state in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs` and `backend/src/Modules/Advertising/API/FacebookAdsOAuthCallbackController.cs`
- [X] T039 [US1] Implement version-pinned Meta REST transport, trace capture, paging, error classification and rate-limit handling in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaGraphClient.cs`
- [X] T040 [US1] Discover accessible ad accounts, Pages, WABAs, phones, Datasets, permissions and currencies in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaCapabilityClient.cs`
- [X] T041 [US1] Runtime-probe supported WhatsApp objectives, optimization outcomes, bid strategies, automatic placement mode and Business Messaging readiness in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaCapabilityClient.cs`
- [X] T042 [US1] Persist immutable configured/effective capability evidence with expiry and provider trace in `backend/src/Modules/Advertising/Services/AdvertisingReadinessService.cs`
- [X] T043 [US1] Validate mutual ad-account/Page/WABA/phone/Dataset eligibility, persist destinations, and transactionally publish route create/revoke/tombstone events in `backend/src/Modules/Advertising/Services/AdvertisingReadinessService.cs` and `backend/src/Shared/Queue/AdvertisingIntegrationEvents.cs`
- [X] T044 [US1] Implement envelope create/activate/suspend/version validation with fixed location, minimum age, language, exclusions, legal restrictions, timezone and multi-period caps in `backend/src/Modules/Advertising/Services/AutonomyEnvelopeService.cs`
- [X] T045 [US1] Expose resources, capabilities, readiness, destinations and envelope APIs and register US1 services/worker subscriptions in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs` and `backend/Program.cs`
- [X] T046 [US1] Implement persistent pause/reconcile/revoke disconnect saga, resumable target progress and destination tombstones in `backend/src/Modules/Advertising/Services/AdvertisingDisconnectService.cs` and `backend/src/Modules/Advertising/Workers/ConnectionDisconnectWorker.cs`
- [X] T047 [US1] Require explicit per-request LeaveRunning acknowledgement and continue monitoring until transfer or stop in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs`
- [X] T048 [US1] Emit audit records for connection, envelope, cross-project denial, disconnect and credential lifecycle in `backend/src/Modules/Advertising/API/AdvertisingConnectionController.cs`
- [X] T049 [US1] Run and fix connection, capability, envelope and disconnect API acceptance coverage in `tests/phase_1/test_whatsapp_ads_connection.py`
- [X] T050 [US1] Add real-account paused-only capability checklist and expected evidence fixtures in `specs/033-facebook-ads-manager/quickstart.md` and `tests/phase_1/fixtures/advertising/meta-capabilities.json`

---

## Phase 4: User Story 2 — Build a sourced WhatsApp launch strategy (P1)

**Independent test**: Published project knowledge and media produce a cited offer/funnel/plan; missing, stale, contradictory, restricted or low-confidence facts return an exact blocking reason and no provider object.

- [X] T051 [P] [US2] Write knowledge projection, stale-version, tombstone and sourced-planning acceptance scenarios in `backend/tests/Advertising.UnitTests/AdvertisingKnowledgeProjectionTests.cs` and `tests/phase_1/test_whatsapp_ads_strategy.py`
- [X] T052 [P] [US2] Write hallucination, contradiction, source-citation and prohibited-claim tests in `backend/tests/Advertising.UnitTests/AdvertisingProfileTests.cs`
- [X] T053 [P] [US2] Write funnel inference and multi-offer destination-grant tests in `backend/tests/Advertising.UnitTests/AdvertisingStrategyTests.cs`
- [X] T054 [P] [US2] Write asynchronous AI request/result credential-isolation tests in `backend/tests/Advertising.UnitTests/AdvertisingAiWorkTests.cs`
- [X] T055 [US2] Consume published knowledge and project-context events into versioned local projections in `backend/src/Modules/Advertising/Workers/KnowledgeProjectionConsumer.cs`
- [X] T056 [US2] Consume consent/legal-basis, capacity, timezone and offer availability events in `backend/src/Modules/Advertising/Workers/ProjectContextProjectionConsumer.cs`
- [X] T057 [US2] Extract evidence-bound facts with source, version, confidence, refresh time and contradiction state in `backend/src/Modules/Advertising/Services/AdvertisingProfileExtractor.cs`
- [X] T058 [US2] Block invented or silently changed commercial facts and prohibited wording in `backend/src/Modules/Advertising/Services/AdvertisingFactValidator.cs`
- [X] T059 [US2] Infer WhatsApp-centered funnels for supported business types from verified facts and outcomes in `backend/src/Modules/Advertising/Services/AdvertisingFunnelService.cs`
- [X] T060 [US2] Rank only authorized offer-destination pairs using economics, capacity, eligibility and evidence in `backend/src/Modules/Advertising/Services/AdvertisingStrategyService.cs`
- [X] T061 [US2] Publish credential-free AI work and accept results only for the current pending owner, version, input hash and deadline in `backend/src/Modules/Advertising/Services/AdvertisingAiWorkCoordinator.cs` and `backend/src/Modules/Advertising/Workers/AdvertisingAiWorkResultConsumer.cs`
- [X] T062 [US2] Resolve Gemini/project AI credentials only inside the owning AI module and verify no direct Advertising AI/settings fallback remains in `backend/src/Modules/AI/Workers/AdvertisingAiWorkConsumer.cs` and `backend/src/Modules/Advertising/Services/ProjectAiConfigurationProvider.cs`
- [X] T063 [US2] Compile an immutable strategy and campaign-plan snapshot containing destination, objective fallback order, controls, policy class and evidence window in `backend/src/Modules/Advertising/Services/CampaignPlanCompiler.cs`
- [X] T064 [US2] Classify special-ad categories and unresolved/prohibited targeting constraints before plan eligibility in `backend/src/Modules/Advertising/Services/MetaPolicyClassificationService.cs`
- [X] T065 [US2] Expose sourced profile, offers, funnel, strategy, plan and exact readiness blockers and register US2 consumers/services in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs` and `backend/Program.cs`
- [X] T066 [US2] Audit source versions, AI schema results, plan decisions and every WAIT/blocking reason in `backend/src/Modules/Advertising/Services/AdvertisingAuditService.cs`
- [X] T067 [US2] Run and fix sourced-planning and hallucination-blocking acceptance coverage in `tests/phase_1/test_whatsapp_ads_strategy.py`
- [X] T068 [US2] Verify the story independently with the fake-provider fixture in `tests/phase_1/fixtures/advertising/strategy/eligible-plan.json`

---

## Phase 5: User Story 3 — Create and verify click-to-WhatsApp advertisements (P1)

**Independent test**: Image, carousel, video, existing-post, clone and replacement paths either reconcile a complete paused hierarchy whose effective state matches the approved WhatsApp plan, or fail closed with the exact differing field and zero spend.

- [X] T069 [P] [US3] Write exact Graph v26 campaign/ad-set/creative/ad contracts and paused-hierarchy acceptance scenarios in `backend/tests/Advertising.UnitTests/MetaProviderContractTests.cs` and `tests/phase_1/test_whatsapp_ads_provisioning.py`
- [X] T070 [P] [US3] Write WhatsApp destination, CTA, Page/WABA/phone, objective-fallback and non-Meta rejection tests in `backend/tests/Advertising.UnitTests/MetaWhatsAppInvariantTests.cs`
- [X] T071 [P] [US3] Write dynamic Advantage+ automatic-placement and field-level equivalence tests in `backend/tests/Advertising.UnitTests/MetaAdvantagePlusTests.cs`
- [X] T072 [P] [US3] Write partial creation, timeout, unknown result, provider drift and repair tests in `backend/tests/Advertising.UnitTests/MetaProvisioningSagaTests.cs`
- [X] T073 [P] [US3] Write creative source rights, format, stale-offer, derivative and no-source-generation tests in `backend/tests/Advertising.UnitTests/CreativeAndPlacementTests.cs`
- [X] T074 [P] [US3] Write clone/replacement preservation and one-variable experiment tests in `backend/tests/Advertising.UnitTests/AdvertisingCloneTests.cs`
- [X] T075 [US3] Map immutable plans to typed Graph v26 campaign payloads without stale manual placement or targeting fields in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaCampaignPlanMapper.cs`
- [X] T076 [US3] Map messaging-compatible objective/optimization capability fallbacks and highest-volume default bidding in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaCampaignPlanMapper.cs`
- [X] T077 [US3] Implement `validate_only` preflight when live capabilities expose it and record unsupported validation explicitly in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs`
- [X] T078 [US3] Implement paused campaign, ad-set, provider-creative and advertisement Graph mutations with idempotency evidence in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs`
- [X] T079 [US3] Retrieve and normalize effective parent, objective, optimization, bid, budget, schedule, audience, placement, identity, creative, CTA and status fields in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaAdsClient.cs`
- [X] T080 [US3] Expand the stateful fake provider for success, rejection, normalization, partial, timeout, duplicate, drift and repair scenarios in `backend/src/Modules/Advertising/Infrastructure/Facebook/FakeMetaAdsHandler.cs`
- [X] T081 [US3] Implement durable per-step paused provisioning with unknown-result reconciliation in `backend/src/Modules/Advertising/Services/CampaignProvisioningService.cs`
- [X] T082 [US3] Implement field-level planned/effective equivalence, critical drift findings and safe repair proposals in `backend/src/Modules/Advertising/Services/MetaProviderReconciliationService.cs`
- [X] T083 [US3] Require `Verified` for provider creatives and `VerifiedPaused` for all delivery objects before activation eligibility in `backend/src/Modules/Advertising/Services/CampaignProvisioningService.cs`
- [X] T084 [US3] Discover eligible existing Page image, carousel and video posts with stable source identity in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaCreativeSourceClient.cs`
- [X] T085 [US3] Consume project media rights, versions, formats and tombstones into a local projection in `backend/src/Modules/Advertising/Workers/MediaProjectionConsumer.cs`
- [X] T086 [US3] Score and explain creative candidates from relevance, policy, format, freshness, organic and prior-paid evidence in `backend/src/Modules/Advertising/Services/CreativeRankingService.cs`
- [X] T087 [US3] Generate only copy, headline, description, CTA, crop, dimensions, thumbnail and format-preserving derivatives in `backend/src/Modules/Advertising/Jobs/CreativeVariantJob.cs`
- [X] T088 [US3] Block creative paths with no eligible source media and request project media without fabricating source images/videos in `backend/src/Modules/Advertising/Services/CreativeRankingService.cs`
- [X] T089 [US3] Implement exact-plan clone and replacement services that preserve every invariant except the declared test variable in `backend/src/Modules/Advertising/Services/AdvertisingCloneService.cs`
- [X] T090 [US3] Expose source, recommendation, variant, validate, provision, operation-progress and activation-readiness APIs and register US3 provider/services/jobs in `backend/src/Modules/Advertising/API/AdvertisingPlanningController.cs` and `backend/Program.cs`
- [X] T091 [US3] Backfill legacy records as `LegacyUnverified` and `ManualUnowned` without provider mutation in `backend/src/Modules/Advertising/Jobs/AdvertisingProjectionBackfillJob.cs`
- [X] T092 [US3] Require explicit verified import/ownership transfer before Autopilot can touch an existing external hierarchy in `backend/src/Modules/Advertising/Services/ExistingCampaignImportService.cs` and `backend/src/Modules/Advertising/API/AdvertisingCampaignImportController.cs`
- [X] T093 [US3] Run and fix paused-hierarchy, drift, clone and zero-spend acceptance coverage in `tests/phase_1/test_whatsapp_ads_provisioning.py`
- [ ] T094 [US3] Run the guarded one-hierarchy real-account paused read-back canary documented in `specs/033-facebook-ads-manager/quickstart.md`

---

## Phase 6: User Story 4 — Allocate caps, run experiments and scale proven winners (P1)

**Independent test**: Concurrent allocation respects daily and monthly/total ledgers atomically, sparse or delayed evidence returns WAIT, only mature losers pause, and scale changes remain inside the approved increase and cooldown.

- [X] T095 [P] [US4] Write concurrent daily/monthly/total ledger tests and cap/experiment acceptance scenarios in `backend/tests/Advertising.UnitTests/BudgetAndPlacementTests.cs` and `tests/phase_1/test_whatsapp_ads_budget_experiments.py`
- [X] T096 [P] [US4] Write observed-spend, outstanding-commitment and delayed-spend hard-cap tests in `backend/tests/Advertising.UnitTests/AdvertisingSpendGuardTests.cs`
- [X] T097 [P] [US4] Write revisioned overlapping-insight pull and current-revision deduplication tests in `backend/tests/Advertising.UnitTests/MetaInsightsTests.cs`
- [X] T098 [P] [US4] Write one-variable, maturity, attribution-delay, inconclusive and stop-rule experiment tests in `backend/tests/Advertising.UnitTests/AdvertisingExperimentTests.cs`
- [X] T099 [P] [US4] Write evidence hierarchy, cost-cap eligibility, cooldown, learning and fatigue tests in `backend/tests/Advertising.UnitTests/AdvertisingEvidenceTests.cs`
- [X] T100 [US4] Implement atomic multi-ledger reservation across every applicable period in `backend/src/Modules/Advertising/Services/BudgetAllocator.cs`
- [X] T101 [US4] Enforce usable-cap safety reserve and the combined observed/forecast/commitment hard-cap formula in `backend/src/Modules/Advertising/Services/AdvertisingSpendGuard.cs`
- [X] T102 [US4] Retrieve overlapping Meta Insights windows with immutable revisions, attribution metadata and provider truth separation in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaInsightsClient.cs`
- [X] T103 [US4] Build coherent reporting windows that never mix current spend with lifetime outcomes in `backend/src/Modules/Advertising/Services/AdvertisingReportingWindowService.cs`
- [X] T104 [US4] Evaluate paid value/contribution, verified outcomes, qualified WhatsApp leads and conversations in descending truth strength in `backend/src/Modules/Advertising/Services/AdvertisingEvidenceService.cs`
- [X] T105 [US4] Return declared WAIT reasons for insufficient spend, time, volume, delay, learning, corrections or health in `backend/src/Modules/Advertising/Services/AdvertisingEvidenceService.cs`
- [X] T106 [US4] Detect fatigue only from sufficient impressions, frequency, engagement decline, conversion decline and cost increase in `backend/src/Modules/Advertising/Services/CreativeFatigueService.cs`
- [X] T107 [US4] Create immutable experiments with control, single variable, budget, attribution window, maturity and stop rules in `backend/src/Modules/Advertising/Services/AdvertisingExperimentService.cs`
- [X] T108 [US4] Allocate the smallest viable evidence-driven portfolio across prospecting, tests, retargeting and winners in `backend/src/Modules/Advertising/Services/PortfolioAllocationService.cs`
- [X] T109 [US4] Scale gradually with highest-volume default bidding and allow cost caps only after credible historical evidence in `backend/src/Modules/Advertising/Services/PortfolioAllocationService.cs`
- [X] T110 [US4] Pause mature proven losers without deleting them and never pause solely for early zero results in `backend/src/Modules/Advertising/Services/AllocationPolicyService.cs`
- [X] T111 [US4] Expose audience, experiment, allocation, ledger and coherent-performance APIs and register US4 services/jobs in `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs` and `backend/Program.cs`
- [X] T112 [US4] Run and fix cap, concurrency, insight-revision, maturity and sparse-WAIT acceptance coverage in `tests/phase_1/test_whatsapp_ads_budget_experiments.py`

---

## Phase 7: User Story 5 — Close the WhatsApp outcome and attribution loop (P1)

**Independent test**: The first inbound and every later advertising referral are captured without decoding opaque payloads; qualified, booking, order, payment, cancellation and refund events produce one corrected business truth; only eligible in-thread events are delivered to Meta with exact consent-aware Business Messaging payloads.

- [X] T113 [P] [US5] Add deterministic Baileys wrapper, present-referral, opaque-marker and missing-referral fixtures in `whatsapp-gateway/test/fixtures/advertising/`
- [X] T114 [P] [US5] Write gateway tests proving optional `ctwaClid` capture and no opaque `ctwaPayload` or `conversionData` decoding in `whatsapp-gateway/test/ad-referral.test.js`
- [X] T115 [P] [US5] Write Cloud webhook GET challenge, signature, route, dedupe and gateway acceptance scenarios in `backend/tests/Advertising.UnitTests/WhatsAppCloudWebhookTests.cs` and `tests/phase_1/test_whatsapp_gateway.py`
- [X] T116 [P] [US5] Write first-message denominator, later-referral touch, provider-message dedupe and destination-routing tests in `backend/tests/Advertising.UnitTests/AdvertisingAttributionTests.cs`
- [X] T117 [P] [US5] Write protected referral lifecycle, last-eligible-touch, stable tie-breaker, window expiry and Meta/internal separation tests in `backend/tests/Advertising.UnitTests/AdvertisingAttributionJourneyTests.cs`
- [X] T118 [P] [US5] Write duplicate, late, pending-base, correction, refund, cancellation and truth-strength tests in `backend/tests/Advertising.UnitTests/ConversionLedgerTests.cs`
- [X] T119 [P] [US5] Write consent/legal-basis revocation, retry recheck and conversion acceptance scenarios in `backend/tests/Advertising.UnitTests/ConversionConsentTests.cs` and `tests/phase_1/test_whatsapp_ads_conversions.py`
- [X] T120 [P] [US5] Write exact Business Messaging payload plus webhook-source/readiness/test endpoint contracts in `backend/tests/Advertising.UnitTests/MetaBusinessMessagingContractTests.cs` and `backend/tests/Advertising.UnitTests/AdvertisingConversionsApiTests.cs`
- [X] T121 [P] [US5] Write tracking policy version, exact-match rate, provider match, coverage, delay, correction and Unsafe tests in `backend/tests/Advertising.UnitTests/TrackingHealthTests.cs`
- [X] T122 [US5] Extract only documented referral identifiers and normalized opaque/missing states in `whatsapp-gateway/src/ad-referral.js`
- [X] T123 [US5] Attach normalized advertising context to authenticated Baileys inbound events without logging protected values in `whatsapp-gateway/src/baileys-manager.js`
- [X] T124 [US5] Enforce globally unique active phone/WABA routing and fail closed on ambiguous or stale routes in `backend/src/Modules/WhatsApp/Domain/WhatsAppInboundRouteProjection.cs` and `backend/src/Modules/WhatsApp/Workers/WhatsAppInboundRouteConsumer.cs`
- [X] T125 [US5] Implement public Cloud/coexistence verification challenge and raw-body `X-Hub-Signature-256` validation in `backend/src/Modules/WhatsApp/API/WhatsAppCloudWebhookController.cs`
- [X] T126 [US5] Resolve `phone_number_id` only from trusted route state, dedupe provider messages and publish durable credential-free inbound events in `backend/src/Modules/WhatsApp/API/WhatsAppCloudWebhookController.cs`
- [X] T127 [US5] Consume WhatsApp inbound events transactionally into Conversations and acknowledge only durable persistence in `backend/src/Modules/Conversations/Workers/WhatsAppInboundMessageConsumer.cs`
- [X] T128 [US5] Use one publisher for Cloud and existing gateway paths and emit first-message plus later-marker attribution observations in `backend/src/Modules/Conversations/Services/WhatsAppInboundEventPublisher.cs` and `backend/src/Modules/Conversations/API/WebhookController.cs`
- [X] T129 [US5] Protect referral identifiers under a dedicated key purpose with controlled raw unwrap only for eligible CAPI delivery in `backend/src/Modules/Advertising/Services/AdvertisingReferralProtector.cs`
- [X] T130 [US5] Consume observations, persist every qualifying touch and maintain denominator counts without fabricating touch identity in `backend/src/Modules/Advertising/Workers/WhatsAppAttributionObservationConsumer.cs`
- [X] T131 [US5] Resolve contact journeys by last eligible touch time plus stable touch ID while preserving earlier touches in `backend/src/Modules/Advertising/Services/AdvertisingAttributionService.cs`
- [X] T132 [US5] Implement signed project-scoped outcome ingestion, one-time webhook-source create/rotate/revoke/list semantics and replay prevention in `backend/src/Modules/Advertising/Services/ConversionIngressService.cs` and `backend/src/Modules/Advertising/Services/AdvertisingWebhookSourceService.cs`
- [X] T133 [US5] Publish CRM qualified/won/lost, booking/payment/attendance and consent changes transactionally in `backend/src/Modules/CRM/API/CRMController.cs`, `backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs`, and `backend/src/Modules/Projects/API/ProjectController.cs`
- [X] T134 [US5] Consume versioned first-party outcomes without direct cross-module reads in `backend/src/Modules/Advertising/Workers/BusinessOutcomeConsumer.cs`
- [X] T135 [US5] Canonicalize source history, dedupe equivalent events, hold out-of-order corrections pending and recompute corrected value in `backend/src/Modules/Advertising/Services/ConversionLedgerService.cs`
- [X] T136 [US5] Distinguish conversation, qualified lead, checkout/order intent, order, paid purchase, cancellation, refund and delivery without upgrading weak evidence in `backend/src/Modules/Advertising/Services/WhatsAppJourneyEventMapper.cs`
- [X] T137 [US5] Restrict Business Messaging attribution to interactions occurring inside the message thread and route website/app evidence separately in `backend/src/Modules/Advertising/Services/ConversionAttributionPolicy.cs`
- [X] T138 [US5] Map only Meta-supported messaging events to `/{dataset-id}/events` with WABA and `ctwa_clid` inside `user_data` in `backend/src/Modules/Advertising/Infrastructure/Facebook/MetaBusinessMessagingClient.cs`
- [X] T139 [US5] Implement one delivery identity with child attempts, accepted-count evidence, bounded retry and consent recheck in `backend/src/Modules/Advertising/Jobs/ConversionDeliveryJob.cs`
- [X] T140 [US5] Calculate versioned tracking health from attribution coverage, exact match, provider match, delay, missing referrals, delivery acceptance and correction rate in `backend/src/Modules/Advertising/Services/AdvertisingTrackingHealthService.cs`
- [X] T141 [US5] Mark tracking `Unsafe` when live Cloud/coexistence referral proof is absent and make financial decisions return a separate WAIT in `backend/src/Modules/Advertising/Services/AdvertisingTrackingHealthService.cs`
- [X] T142 [US5] Expose signed conversion intake, truth, touches, delivery, webhook-source, Business Messaging readiness/test and tracking APIs and register US5 consumers/jobs in `backend/src/Modules/Advertising/API/AdvertisingConversionsController.cs` and `backend/Program.cs`
- [X] T143 [US5] Run and fix gateway, attribution, outcome, correction, consent and CAPI acceptance coverage in `tests/phase_1/test_whatsapp_ads_conversions.py` and `tests/phase_1/test_whatsapp_gateway.py`
- [ ] T144 [US5] Run the real referral-identifier and Dataset test-event gates documented in `specs/033-facebook-ads-manager/quickstart.md`; keep tracking Unsafe if either gate fails

---

## Phase 8: User Story 6 — Make autonomous decisions explainable and at-most-once (P2)

**Independent test**: Strategist proposals stay inside the closed action catalog, deterministic evidence can force WAIT, Auditor/Judge results cannot bypass safety, and an approved command mutates the provider at most once even across timeout, retry and restart.

- [X] T145 [P] [US6] Write closed action-schema, hallucination rejection, exact WAIT reasons and decision acceptance scenarios in `backend/tests/Advertising.UnitTests/DecisionPipelineTests.cs` and `tests/phase_1/test_whatsapp_ads_decisions.py`
- [X] T146 [P] [US6] Write Strategist/Auditor/Judge sequencing and non-bypass tests in `backend/tests/Advertising.UnitTests/AdvertisingDecisionReviewTests.cs`
- [X] T147 [P] [US6] Write stale input, late AI completion, wrong owner/hash and credential-isolation tests in `backend/tests/Advertising.UnitTests/AdvertisingAiWorkTests.cs`
- [X] T148 [P] [US6] Write tenant, envelope, destination, budget, health, ownership and expected-state safety tests in `backend/tests/Advertising.UnitTests/AdvertisingSafetyEngineTests.cs`
- [X] T149 [P] [US6] Write duplicate command, timeout, unknown-result, reconciliation and restart tests in `backend/tests/Advertising.UnitTests/AdvertisingCommandWorkerTests.cs`
- [X] T150 [P] [US6] Write delayed impact, correction, inconclusive and rollback tests in `backend/tests/Advertising.UnitTests/AdvertisingDecisionImpactTests.cs`
- [X] T151 [US6] Build versioned evidence packages with coherent window, truth source, thresholds, attribution and learning state in `backend/src/Modules/Advertising/Services/AdvertisingEvidenceService.cs`
- [X] T152 [US6] Publish schema-bound Strategist, Auditor and Judge AI work without facts or identifiers supplied by the model in `backend/src/Modules/Advertising/Services/AdvertisingDecisionService.cs`
- [X] T153 [US6] Consume AI work in the owning AI module and return validated credential-free results in `backend/src/Modules/AI/Workers/AdvertisingAiWorkConsumer.cs`
- [X] T154 [US6] Accept results only for the current pending owner, version, input hash and deadline in `backend/src/Modules/Advertising/Workers/AdvertisingAiWorkResultConsumer.cs`
- [X] T155 [US6] Enforce the closed autonomous catalog for create, validate, activate, pause, resume, replace, experiment, audience suggestion, budget, scale, optimization and drift repair in `backend/src/Modules/Advertising/Services/AdvertisingDecisionService.cs`
- [X] T156 [US6] Implement deterministic maturity and eligibility evaluation before independent audit in `backend/src/Modules/Advertising/Services/AdvertisingDecisionService.cs`
- [X] T157 [US6] Require Judge review for configured large increases, optimization changes, launches and value-producing pause disputes in `backend/src/Modules/Advertising/Services/AdvertisingDecisionService.cs`
- [X] T158 [US6] Implement the final deterministic safety layer immediately before execution in `backend/src/Modules/Advertising/Services/AdvertisingSafetyEngine.cs`
- [X] T159 [US6] Claim durable commands, verify expected external state and execute one provider mutation per identity in `backend/src/Modules/Advertising/Workers/AdvertisingCommandWorker.cs`
- [X] T160 [US6] Reconcile provider state before retrying unknown results and repair only managed drift in `backend/src/Modules/Advertising/Workers/AdvertisingCommandWorker.cs`
- [X] T161 [US6] Schedule event-appropriate impact review and label positive, negative, inconclusive or reverted in `backend/src/Modules/Advertising/Services/AdvertisingDecisionImpactService.cs`
- [X] T162 [US6] Persist proposal, evidence, reviews, safety, command, reconciliation, impact, rollback and precise no-change reasons in `backend/src/Modules/Advertising/Services/AdvertisingAuditService.cs`
- [X] T163 [US6] Expose decisions, evidence, changes, audit, operation state and cursor pagination and register US6 workers/services in `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs` and `backend/Program.cs`
- [X] T164 [US6] Run and fix decision, duplicate-command, unknown-result and impact acceptance coverage in `tests/phase_1/test_whatsapp_ads_decisions.py`

---

## Phase 9: User Story 7 — Monitor health, freeze risk and stop safely (P2)

**Independent test**: Jobs run once per project/time bucket, actual spend is polled every five minutes, Unsafe tracking deterministically triggers Emergency Stop, only system-owned delivery is paused, and explicit healthy authorized resume is required.

- [X] T165 [P] [US7] Write timezone/DST/time-bucket tests and cadence/stop acceptance scenarios in `backend/tests/Advertising.UnitTests/AdvertisingJobScheduleTests.cs` and `tests/phase_1/test_whatsapp_ads_safety_jobs.py`
- [X] T166 [P] [US7] Write Redis lease loss, duplicate dispatch and restart recovery tests in `backend/tests/Advertising.UnitTests/AdvertisingJobLeaseTests.cs`
- [X] T167 [P] [US7] Write freshest-spend, hard-cap, abnormal-spend and delayed-report guard tests in `backend/tests/Advertising.UnitTests/AdvertisingSpendGuardTests.cs`
- [X] T168 [P] [US7] Write TrackingUnsafe mandatory stop, repeated-command, lost-authorization and tenant-mismatch trigger tests in `backend/tests/Advertising.UnitTests/AdvertisingEmergencyStopTests.cs`
- [X] T169 [P] [US7] Write normal PauseManaged, explicit LeaveRunning, per-object progress and continuing-spend tests in `backend/tests/Advertising.UnitTests/AdvertisingDisableTests.cs`
- [X] T170 [P] [US7] Write stop-command replay, partial provider failure, unknown result and guarded resume tests in `backend/tests/Advertising.UnitTests/AdvertisingSafetyJobsTests.cs`
- [X] T171 [US7] Dispatch global schedules into project/timezone jobs and calculate IANA timezone/DST buckets in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T172 [US7] Acquire project-scoped Redis leases plus durable database bucket guards for every recurring cycle in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T173 [US7] Poll actual spend at least every five minutes and reconcile reservations/forecast in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T174 [US7] Synchronize managed provider state at least every ten minutes and insights at least every fifteen minutes in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T175 [US7] Evaluate tracking health at least every fifteen minutes and run hourly eligibility/decision cycles in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T176 [US7] Run periodic fatigue, daily budget and weekly strategy reviews in the project timezone in `backend/src/Modules/Advertising/Jobs/AdvertisingRecurringJobs.cs`
- [X] T177 [US7] Process payment, booking, attendance and corrections immediately outside scheduled decision cycles in `backend/src/Modules/Advertising/Workers/BusinessOutcomeConsumer.cs`
- [X] T178 [US7] Freeze all financial changes whenever connection, capability, tracking, account or budget health is unsafe in `backend/src/Modules/Advertising/Services/AdvertisingSafetyEngine.cs`
- [X] T179 [US7] Trigger Emergency Stop deterministically for abnormal spend, cap risk, cross-project mismatch, Unsafe tracking, repeated commands or lost authorization in `backend/src/Modules/Advertising/Services/AdvertisingEmergencyStopService.cs`
- [X] T180 [US7] Block pending/new commands within ten seconds and pause each system-owned hierarchy with durable progress in `backend/src/Modules/Advertising/Services/AdvertisingEmergencyStopService.cs`
- [X] T181 [US7] Never mutate ManualUnowned or unrelated external campaigns during stop, disable, repair or disconnect in `backend/src/Modules/Advertising/Services/AdvertisingOwnershipPolicy.cs`
- [X] T182 [US7] Default normal disable to PauseManaged and require actor/time acknowledgement for LeaveRunning in `backend/src/Modules/Advertising/Services/AdvertisingDisableService.cs`
- [X] T183 [US7] Keep continuing spend prominent and monitored while autonomy is disabled with LeaveRunning in `backend/src/Modules/Advertising/Services/AdvertisingDisableService.cs`
- [X] T184 [US7] Require healthy read-back, reconciled spend and authorized Owner/Admin action before resume in `backend/src/Modules/Advertising/Services/AdvertisingEmergencyStopService.cs`
- [X] T185 [US7] Expose health, incidents, disable, Emergency Stop, stop progress, resume and operation APIs and register all recurring US7 schedules in `backend/src/Modules/Advertising/API/AdvertisingOperationsController.cs` and `backend/Program.cs`
- [X] T186 [US7] Run and fix cadence, cap, tracking-stop, disable, replay and recovery acceptance coverage in `tests/phase_1/test_whatsapp_ads_safety_jobs.py`
- [X] T187 [US7] Run normal-stop, Emergency Stop, unknown-result and disconnect crash-recovery drills from `specs/033-facebook-ads-manager/quickstart.md`

---

## Phase 10: User Story 8 — Operate the AI media buyer from one clear workspace (P3)

**Independent test**: Desktop and mobile users can distinguish `حملات واتساب` from `مدير الإعلانات`, see coherent health/outcomes and planned-versus-effective drift, switch projects without stale data, and reach Emergency Stop by keyboard or touch.

- [X] T188 [P] [US8] Add Vitest/Testing Library frontend component and state harness in `frontend/package.json`, `frontend/vitest.config.ts`, and `frontend/src/test/setup.ts`
- [X] T189 [P] [US8] Add Playwright dependencies, lockfile, configuration and authenticated fixtures in `frontend/package.json`, `frontend/package-lock.json`, `frontend/playwright.config.ts`, and `frontend/tests/ad-manager/fixtures.ts`
- [X] T190 [P] [US8] Write project-switch abort, late-response rejection, cursor and polling fallback tests in `frontend/src/packages/ad-manager/__tests__/ad-manager-state.test.tsx`
- [X] T191 [P] [US8] Write RTL tabs, keyboard, screen-reader status and persistent-stop tests in `frontend/src/packages/ad-manager/__tests__/ad-manager-shell.test.tsx`
- [X] T192 [P] [US8] Write 375/768/1024/1440 responsive and keyboard browser tests in `frontend/tests/ad-manager/workspace.spec.ts`
- [X] T193 [P] [US8] Write loading, empty, stale, degraded, failed, WAIT and continuing-spend browser tests in `frontend/tests/ad-manager/resource-states.spec.ts`
- [X] T194 [P] [US8] Write Emergency Stop, confirmation, progress and resume accessibility tests in `frontend/tests/ad-manager/safety.spec.ts`
- [X] T195 [US8] Define discriminated resource, operation, metric-window, truth-source and provider-drift types in `frontend/src/packages/ad-manager/types/index.ts`
- [X] T196 [US8] Implement authenticated project-scoped API methods, idempotency keys, If-Match and cursor parsing in `frontend/src/packages/ad-manager/api/ad-manager-api.ts`
- [X] T197 [US8] Implement project-reset, abortable requests, late-response guards, operation polling and 60-second freshness fallback in `frontend/src/packages/ad-manager/hooks/use-ad-manager.ts`
- [X] T198 [P] [US8] Build the restrained RTL workspace shell and persistent control strip in `frontend/src/packages/ad-manager/components/AdManagerShell.tsx` and `frontend/src/packages/ad-manager/components/ControlStrip.tsx`
- [X] T199 [P] [US8] Build first-run readiness, sourced strategy and authority settings in `frontend/src/packages/ad-manager/components/ReadinessPanel.tsx`, `frontend/src/packages/ad-manager/components/StrategyView.tsx`, and `frontend/src/packages/ad-manager/components/SettingsPanel.tsx`
- [X] T200 [P] [US8] Build coherent health, KPI, funnel and allocation overview in `frontend/src/packages/ad-manager/components/OverviewView.tsx`
- [X] T201 [P] [US8] Build planned/effective campaign hierarchy and field-level drift views in `frontend/src/packages/ad-manager/components/CampaignHierarchyView.tsx`
- [X] T202 [P] [US8] Build hard controls, exclusions, Advantage+ suggestions, estimated reach and withheld-reason views in `frontend/src/packages/ad-manager/components/AudiencesView.tsx`
- [X] T203 [P] [US8] Build source, recommendation, testing, winning, fatigue, rejection and outcome creative views in `frontend/src/packages/ad-manager/components/CreativesView.tsx`
- [X] T204 [P] [US8] Build experiment lifecycle, control, variable, evidence and rule views in `frontend/src/packages/ad-manager/components/ExperimentsView.tsx`
- [X] T205 [P] [US8] Build canonical outcome, journey touch, attribution quality, correction and CAPI evidence views in `frontend/src/packages/ad-manager/components/WhatsAppOutcomesView.tsx`
- [X] T206 [P] [US8] Build proposal, WAIT reason, review, command, reconciliation, impact and rollback views in `frontend/src/packages/ad-manager/components/DecisionsView.tsx`
- [X] T207 [US8] Compose URL-addressable nine-view workspace with project reset and operation states in `frontend/src/app/(dashboard)/management/ad-manager/page.tsx`
- [X] T208 [US8] Rename outreach to `حملات واتساب` and add `مدير الإعلانات` consistently in desktop/mobile navigation in `frontend/src/config/navigation.ts`, `frontend/src/packages/inbox/shared/ThinSidebar.tsx`, and `frontend/src/app/(dashboard)/layout.tsx`
- [X] T209 [US8] Implement dark-navy, restrained-cyan, dense flat RTL design tokens without gradients/glass/card-wall patterns in `frontend/src/packages/ad-manager/AdManager.module.css`
- [X] T210 [US8] Add visible focus, semantic tables, touch targets, reduced motion, screen-reader live regions and responsive transformations in `frontend/src/packages/ad-manager/AdManager.module.css` and all view components
- [X] T211 [US8] Show timezone, range, currency, attribution window and truth source beside every comparison in `frontend/src/packages/ad-manager/components/MetricContext.tsx`
- [X] T212 [US8] Keep unsafe tracking, active incidents, LeaveRunning spend and Emergency Stop visible from every view in `frontend/src/packages/ad-manager/components/ControlStrip.tsx`

---

## Phase 11: Cross-cutting hardening, rollout and proof

- [X] T213 [P] Re-run and fix the foundational forbidden-dependency architecture gate after all stories in `backend/tests/Advertising.UnitTests/ModuleBoundaryTests.cs`
- [X] T214 [P] Re-run clean-install and existing-database PostgreSQL upgrade tests with zero provider calls in `backend/tests/Advertising.IntegrationTests/MigrationTests.cs`
- [X] T215 Re-run resumable backfill/parity proof and confirm legacy direct reads remain removed in `backend/src/Modules/Advertising/Jobs/AdvertisingProjectionBackfillJob.cs`
- [X] T216 Add bounded retention, protected-field deletion, project archive/delete and evidence-preserving compaction in `backend/src/Modules/Advertising/Jobs/AdvertisingRetentionJob.cs`
- [X] T217 Verify logs, errors, audit export and Elasticsearch documents redact secrets, referral identifiers and unauthorized PII in `backend/tests/Advertising.UnitTests/AdvertisingPrivacyTests.cs`
- [X] T218 Verify every service, consumer, subscription, worker and job has one production registration and re-run the shared HTTP mutation contract in `backend/Program.cs` and `backend/tests/Advertising.UnitTests/AdvertisingApiContractTests.cs`
- [X] T219 Run and fix backend unit and PostgreSQL integration builds/tests for `backend/backend.csproj`, `backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj`, and `backend/tests/Advertising.IntegrationTests/Advertising.IntegrationTests.csproj`
- [X] T220 Run and fix WhatsApp gateway tests in `whatsapp-gateway/` and all focused/regression pytest suites in `tests/phase_1/`, `tests/phase_3/`, `tests/phase_4/`, and `tests/phase_5/`
- [X] T221 Run and fix frontend component tests, Playwright tests, lint and production build in `frontend/`
- [X] T222 Execute every local no-spend, referral, decision, stop and recovery drill in `specs/033-facebook-ads-manager/quickstart.md`
- [X] T223 Confirm schema/registry/projection rollout with Autopilot disabled, then backfill/parity, then fake-provider gates in `specs/033-facebook-ads-manager/rollout-evidence.md`
- [ ] T224 Confirm exactly one real paused hierarchy, effective read-back, referral proof and Dataset test event before permitting any activation in `specs/033-facebook-ads-manager/rollout-evidence.md`
- [ ] T225 Enable only the bounded canary after US1–US7 gates and record Owner/Admin authority, effective caps and stop readiness in `specs/033-facebook-ads-manager/rollout-evidence.md`
- [X] T226 Run clean-code review, resolve production findings and record evidence in `specs/033-facebook-ads-manager/reviews/clean-code-review.md`
- [X] T227 Run test-quality review, remove brittle/redundant tests and record evidence in `specs/033-facebook-ads-manager/reviews/test-review.md`
- [X] T228 Re-run feature tests after review fixes and record commands, versions, outcomes and any external gate still intentionally blocked in `specs/033-facebook-ads-manager/verification.md`
- [X] T229 Update operator-facing implementation notes and verified config/API examples in `specs/033-facebook-ads-manager/quickstart.md`
- [X] T230 Complete Phase 4–9 evidence and final honest status in `achievements.md`

---

## Dependencies and safe delivery gates

- Phase 2 blocks all user stories.
- US1 establishes resources and bounded authority; it remains read-only/no-spend by itself.
- US2 can compile sourced plans after Phase 2; provider creation requires both US1 and US2.
- US3 depends on US1+US2 and may create only paused, verified hierarchies.
- US4 depends on managed hierarchy from US3; activation remains mocked until attribution and safety gates pass.
- US5 ingestion may begin after Phase 2+US1, but CAPI delivery needs managed identities from US3.
- US6 depends on US1 authority, US4 evidence and US5 business truth.
- US7 safety primitives start after US1, but its complete jobs and stop flow depend on US3 provider ownership, US4 budget/insights, US5 tracking/outcomes and US6 command reconciliation; it blocks every real activation and financial command.
- US8 starts once API resource shapes are stable; final controls depend on US1–US7.
- Real-spend canary is forbidden until US1–US7 independent tests, paused provider read-back, real referral proof, Dataset test event and stop/recovery drills all pass.

## Parallel opportunities

- Setup tasks T002–T005 are independent.
- In each story, `[P]` tests are authored before implementation and may run concurrently in disjoint files.
- After Phase 2, US1 provider connection and US2 knowledge projection can proceed in parallel.
- Within US3, provider-contract tests and creative pipeline tests can proceed in parallel before provisioning integration.
- Within US5, gateway/Cloud transport, canonical outcome ledger and Business Messaging contract tests are independent until attribution integration.
- US8 harness tasks T188–T189 block tests T190–T194; view components T198–T206 are independent after shared types/state stabilize.

## Implementation strategy

1. **Safe foundation**: deploy only additive schema, messaging registry and projections with Autopilot disabled; prove backfill parity before removing legacy direct reads.
2. **No-spend functional MVP**: finish US1–US5 with fake-provider scenarios, paused hierarchy reconciliation and honest tracking health.
3. **Autonomy safety**: finish US6–US7, including at-most-once commands, mandatory Unsafe stop and crash-recovery drills.
4. **Operator workspace**: finish US8 with coherent business outcomes and accessible persistent safety controls.
5. **Bounded rollout**: validate one real paused hierarchy, one real referral and one Dataset test event; only then allow an explicitly authorized canary and enable portfolio autonomy last.
