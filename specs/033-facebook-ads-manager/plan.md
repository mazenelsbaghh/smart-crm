# Implementation Plan: Autonomous WhatsApp AI Media Buyer

**Branch**: `033-facebook-ads-manager` | **Date**: 2026-08-18 | **Spec**: [spec.md](spec.md)

**Input**: Repair the existing Ads Manager and replace it with a bounded, end-to-end AI media buyer. Every managed advertisement opens WhatsApp. Advantage+ may use every placement that the live Meta account validates as eligible.

## Summary

Replace the current split campaign-creation paths with a single immutable campaign-plan compiler and a durable Meta provider saga. The rebuilt feature discovers the ad account, Page, WABA, phone and Dataset capabilities; chooses an authorized offer and WhatsApp destination; derives hard audience controls and Advantage+ suggestions; creates a consolidated hierarchy paused; validates and reads back every critical field; captures official `referral.ctwa_clid` through WhatsApp Cloud API/coexistence (with a separately proven Baileys observation path for internal attribution only); delivers eligible in-thread outcomes through Business Messaging Conversions API; and lets a deterministic plus independently reviewed AI loop manage experiments, creatives, audiences, budgets and optimization goals inside a revocable envelope.

The UI becomes a high-density Arabic command center with coherent reporting windows, planned-versus-effective provider state, explicit experiment maturity and exact failure evidence. Existing records are migrated additively and remain untrusted for autonomous spend until reconciled.

## Technical Context

**Language/Version**: C# 13 on .NET 9; JavaScript ES modules on Node 22-compatible runtime; TypeScript 5, React 19.2 and Next.js 16.2

**Primary Dependencies**: ASP.NET Core, EF Core 9/Npgsql, Hangfire PostgreSQL, StackExchange.Redis, RabbitMQ.Client, ASP.NET Data Protection, ImageSharp, shared MinIO/S3 abstraction, existing Gemini client, WhatsApp Cloud API/coexistence webhooks, Baileys 7 release candidate for the existing gateway, Next.js App Router, axios, SignalR and CSS Modules

**Storage**: PostgreSQL for tenant configuration, plans, provider operations, ledgers, attribution, experiments and audit; Redis for leases and OAuth state; RabbitMQ plus transactional outbox/inbox for cross-module facts; MinIO/S3 for derived creative assets

**Testing**: xUnit service/provider contract tests; Python authenticated integration and acceptance tests; Node built-in tests for WhatsApp referral extraction; frontend lint/build and focused component/browser checks; Docker Compose configuration and health verification

**Target Platform**: Linux Docker Compose backend/workers/gateway, PostgreSQL/Redis/RabbitMQ/MinIO, current Chromium-class desktop/mobile browsers, Meta Marketing Graph API pinned to v26.0 by default

**Project Type**: modular-monolith web application plus a Node WhatsApp gateway and background jobs

**Performance Goals**: webhook acknowledgement under 500 ms excluding asynchronous work; overview first useful state under 2 seconds on normal project data; Emergency Stop blocks new commands within 10 seconds; fresh observable spend checked within 5 minutes; first-party outcomes visible within 60 seconds; no overlapping project mutation cycle

**Constraints**: WhatsApp-only destination; dynamic runtime placement eligibility; real-spend mutations only inside a revocable envelope; Business Messaging CAPI only after supported Cloud API/coexistence referral proof; no invented commercial facts or source media; no automatic permanent campaign deletion; unknown provider results reconcile before retry; missing/opaque attribution is never inferred or reverse-engineered; customer audiences require explicit authorization plus consent/legal basis; current dirty worktree must be preserved

**Scale/Scope**: one active advertising connection and envelope family per project; multiple authorized offer-to-destination pairs; tens of campaigns, hundreds of experiments/ads and millions of append-only insight/source-event rows over time; nine URL-addressable workspace views; 11 recurring or event-driven operation classes

## Constitution Check

*GATE: passed before research and rechecked after data model and contracts.*

### Pre-design gate

- **I. Modular Monolith Architecture - PASS WITH REQUIRED REPAIR**: Advertising continues as one module. Current direct reads of Project Settings, Connected Pages, Knowledge, Media, CRM and Booking tables are removed from the decision path and replaced by Advertising-owned projections updated through versioned outbox/inbox events. Conversations owns inbound message persistence and publishes only the attribution-observation contract. Creative variant storage uses a Shared object-storage abstraction; Advertising no longer injects `Modules.Media.Services.IMinIoStorageService`.
- **II. Strict Multi-Tenant Project Isolation - PASS**: every new row contains non-null `ProjectId`; external identifiers are unique in project/connection scope; provider operations, background jobs, webhook events and UI queries re-authorize or explicitly filter ProjectId.
- **III. Gemini 3.5 Flash Unified AI Engine - PASS**: all unstructured offer/creative interpretation and Strategist/Auditor/Judge work uses the existing project-scoped Gemini client. Provider validation, arithmetic and policy invariants are deterministic.
- **IV. Human-Like Messaging and Aggregation - PASS**: referral extraction occurs before the existing aggregation path and does not change typing, aggregation or reply behavior.
- **V. Risk-Based Authorization and Bounded Autonomy - PASS**: Owner/Admin authorizes resources, offer-to-WhatsApp pairs, audience sources, hard controls, financial caps, time and maximum change. AI acts autonomously only inside the envelope after independent review and final deterministic safety checks.
- **Audit, security and reliable integrations - PASS**: secrets stay protected; referral/match data is minimized; provider mutations are durable and reconciled; critical source changes use transactional outbox; errors and decisions carry correlation/evidence without credentials or PII.

### Post-design gate

The target model preserves all gates once the required repair tasks land. `WhatsAppAttributionObserved.v1` is a shared integration contract, not a direct Advertising write into Conversations. Projects publishes lifecycle/timezone/AI-configuration changes; Knowledge, Media, CRM and Booking/Payment publish source aggregate changes through the transactional outbox; Advertising consumes them with inbox receipts and local projections. The outbox dispatcher is changed from its current closed type switch to registered versioned handlers before producers are enabled. Provider capability and snapshot tables are Advertising-owned. No new cross-module database dependency is introduced and no constitution exception is required.

## Project Structure

### Documentation

```text
specs/033-facebook-ads-manager/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── http-api.md
│   ├── integration-events.md
│   ├── jobs-and-safety.md
│   ├── meta-provider-and-reconciliation.md
│   └── ui-state.md
└── tasks.md
```

### Source Code

```text
backend/
├── Program.cs
├── Migrations/
├── src/
│   ├── Modules/
│   │   ├── Advertising/
│   │   │   ├── API/
│   │   │   │   ├── AdvertisingConnectionController.cs
│   │   │   │   ├── AdvertisingPlanningController.cs
│   │   │   │   ├── AdvertisingOperationsController.cs
│   │   │   │   └── AdvertisingConversionsController.cs
│   │   │   ├── Domain/
│   │   │   │   ├── AdvertisingConnection.cs
│   │   │   │   ├── AdvertisingPlan.cs
│   │   │   │   ├── AdvertisingCreative.cs
│   │   │   │   ├── AdvertisingBudget.cs
│   │   │   │   ├── AdvertisingConversion.cs
│   │   │   │   └── AdvertisingDecision.cs
│   │   │   ├── Infrastructure/Facebook/
│   │   │   │   ├── MetaAdsClient.cs
│   │   │   │   ├── MetaCampaignPlanMapper.cs
│   │   │   │   ├── MetaBusinessMessagingClient.cs
│   │   │   │   ├── MetaInsightsClient.cs
│   │   │   │   └── FakeMetaAdsHandler.cs
│   │   │   ├── Services/
│   │   │   │   ├── CampaignPlanCompiler.cs
│   │   │   │   ├── CampaignProvisioningService.cs
│   │   │   │   ├── MetaProviderReconciliationService.cs
│   │   │   │   ├── AudienceStrategyService.cs
│   │   │   │   ├── AdvertisingExperimentService.cs
│   │   │   │   ├── AdvertisingAttributionService.cs
│   │   │   │   ├── AdvertisingTrackingHealthService.cs
│   │   │   │   ├── PortfolioAllocationService.cs
│   │   │   │   └── AdvertisingDecisionService.cs
│   │   │   ├── Workers/
│   │   │   │   ├── WhatsAppAttributionObservationConsumer.cs
│   │   │   │   ├── AdvertisingAiWorkResultConsumer.cs
│   │   │   │   ├── BusinessOutcomeConsumer.cs
│   │   │   │   └── AdvertisingCommandWorker.cs
│   │   │   └── Jobs/AdvertisingRecurringJobs.cs
│   │   ├── AI/Workers/AdvertisingAiWorkConsumer.cs
│   │   ├── Conversations/
│   │   │   ├── API/WebhookController.cs
│   │   │   └── Workers/WhatsAppInboundMessageConsumer.cs
│   │   └── WhatsApp/
│   │       ├── API/WhatsAppCloudWebhookController.cs
│   │       └── Workers/WhatsAppInboundRouteConsumer.cs
│   └── Shared/
│       ├── Audit/ElasticsearchAuditIndexer.cs
│       ├── Queue/AdvertisingIntegrationEvents.cs
│       └── Storage/IObjectStorage.cs
└── tests/Advertising.UnitTests/
    ├── CampaignPlanCompilerTests.cs
    ├── MetaProviderContractTests.cs
    ├── AudienceStrategyTests.cs
    ├── AdvertisingAttributionTests.cs
    ├── AdvertisingExperimentTests.cs
    └── AdvertisingSafetyJobsTests.cs

whatsapp-gateway/
├── src/
│   ├── ad-referral.js
│   └── baileys-manager.js
└── test/
    ├── ad-referral.test.js
    └── fixtures/README.md

frontend/src/
├── app/(dashboard)/management/ad-manager/page.tsx
└── packages/ad-manager/
    ├── api/ad-manager-api.ts
    ├── hooks/use-ad-manager.ts
    ├── types/index.ts
    ├── components/
    │   ├── AdManagerShell.tsx
    │   ├── ControlStrip.tsx
    │   ├── StrategyView.tsx
    │   ├── OverviewView.tsx
    │   ├── CampaignHierarchyView.tsx
    │   ├── AudiencesView.tsx
    │   ├── CreativesView.tsx
    │   ├── ExperimentsView.tsx
    │   ├── WhatsAppOutcomesView.tsx
    │   ├── DecisionsView.tsx
    │   └── SettingsPanel.tsx
    └── AdManager.module.css

tests/phase_1/
├── test_facebook_ads_manager.py
├── test_facebook_ads_conversions.py
└── test_facebook_ads_safety_jobs.py
```

**Structure Decision**: extend the existing modular monolith and feature package. Keep provider transport and provider mapping separate from domain planning/reconciliation. Add an official Cloud API/coexistence webhook adapter and a small pure experimental Baileys referral extractor behind one normalized observation contract. Do not create a new microservice or let Advertising own Conversation records.

## Target Architecture

### 0. Module projections and deployment order

`IntegrationOutboxDispatcher` first moves from a closed event-type switch to registered versioned publishers. Projects publishes lifecycle, IANA timezone and AI-configuration versions; Knowledge publishes committed revision/fact changes in the same transaction; Media publishes asset/rights/availability changes; CRM and Booking/Payment/Attendance publish canonical business aggregate changes; Conversations publishes the first-message observation and every later referral-bearing observation. Advertising consumes each with inbox idempotency into its own projections. Only after projection backfill and parity checks do `ProjectAiConfigurationProvider`, `FacebookPageTokenResolver`, `CreativeVariantJob` and other Advertising paths stop reading/injecting another module's internals. Object storage moves to a Shared interface implemented by the existing MinIO adapter; no Advertising-to-Media service dependency remains.

Gemini credentials never enter Advertising events or projections. Advertising persists `AdvertisingAiWorkItem` plus `AdvertisingAiWorkRequested.v1`; the AI module resolves the encrypted project credential internally, invokes Gemini 3.5 Flash and returns `AdvertisingAiWorkCompleted.v1`. Advertising resumes the profile/creative/decision state machine from that result. This replaces both the direct Project Settings read and any direct AI-module service reference.

### 1. Strategy and immutable plan

`AdvertisingProfile` projects approved knowledge into offers, facts, economics, capacity and policy. Owner/Admin creates an envelope containing allowed offer-to-WhatsApp pairs and customer-audience grants. `CampaignPlanCompiler` selects an eligible pair, objective fallback, broad audience strategy, Advantage+ placement mode, consolidated budget owner, creatives and experiments. It emits a versioned plan and field-level readiness findings. The plan is immutable after provider work begins; a changed fact or envelope produces a new version.

### 2. Provider creation saga

```text
Draft plan
  -> Locally validated
  -> Provider preflight validated
  -> Campaign created paused/read back
  -> Ad set created paused/read back
  -> Creative created/read back
  -> Ad created paused/read back
  -> Hierarchy verified paused
  -> Activation command safety recheck
  -> Active and continuously reconciled
```

Each transition is one durable provider operation with dependency, request fingerprint, attempt state, provider trace and normalized planned/effective snapshot. `Unknown`, `Partial`, `Rejected` and `Drifted` never transition directly to retry or active. They reconcile first. Partial objects remain paused and visible.

### 3. Targeting and placements

`AudienceStrategy` stores hard controls separately from suggestions and observed evidence. Its normalized hard-control boundary must be equal to or narrower than the envelope boundary. The Meta mapper omits static Facebook-only placement fields to request Advantage+ placements, then proves automatic mode, WhatsApp destination, promoted Page/phone and CTA through exact-shape validation and read-back. It uses field-level invariant/equivalence checks rather than full provider-snapshot hash equality; actual inventory delivery is a later insight. Suggestions may include interests, customer lists and lookalikes only when capability and privacy gates pass.

### 4. WhatsApp attribution and business truth

WhatsApp Cloud API/coexistence is the supported production source for documented Messages-webhook `referral.ctwa_clid`. Its public endpoint verifies GET challenge and raw-body `X-Hub-Signature-256`, resolves WABA/phone only through a globally unambiguous WhatsApp-owned route projection, deduplicates provider message ID and publishes a durable inbound event to Conversations. The existing Baileys gateway additionally inspects `contextInfo.externalAdReply.ctwaClid`, but that optional protobuf field is treated as experimental until a real configured-account fixture proves it arrives; opaque `ctwaPayload`/`conversionData` is only hashed and is never decoded. Conversations emits `WhatsAppAttributionObserved.v1` for the first message regardless of state and for every later referral-bearing/opaque-marker message. Missing or unproven identifiers force tracking `Unsafe/WAIT` for outcome-based financial changes.

Advertising persists eligible touches across a protected customer journey, selects the latest eligible touch by `TouchedAtUtc` with a stable-ID tie-breaker, and unprotects `ctwa_clid` only inside the CAPI adapter under a fixed Data Protection purpose. Business Messaging CAPI is enabled only for supported Cloud API/coexistence, WABA/Dataset and real referral/test-event proof. A Baileys observation can remain explicitly labeled internal attribution but cannot by itself authorize CAPI.

CRM, booking, payment, attendance and conversation-classification events update a canonical business outcome without inventing attribution. Truth, attribution, correction and delivery are separate states. Consent changes are consumed before every retry. In-thread eligible events are delivered through Meta Business Messaging CAPI with WABA and `ctwa_clid`; website/app events keep their applicable delivery path. Missing referral remains unattributed and lowers tracking health.

### 5. Evidence, experiments and portfolio allocation

Every `AdvertisingExperiment` has one primary variable, control, variants, hypothesis, budget, maturity rule, attribution window and stop rule. `AdvertisingEvidenceService` produces windowed evidence for the configured outcome hierarchy and includes coverage/freshness/correction/learning gates. `PortfolioAllocationService` reserves and releases budget across mature winners and viable tests instead of applying fixed percentages. A reservation locks and debits all applicable daily and monthly/total ledgers atomically; each period enforces `max(observed + delayed-spend estimate, committed + delta) <= usable cap`. Lowest-cost bidding is default; cost cap is eligible only after historical outcome maturity.

Strategist proposes from a closed action catalog. Auditor independently reviews the proposal and evidence. Judge handles configured escalation classes. Safety re-resolves tenant, plan, envelope, provider state, destination, tracking, ledger and cooldown immediately before issuing a command.

### 6. Operational truth and UI

Every API response uses one reporting window, timezone, attribution window and currency. First-party verified, Meta-reported, inferred and unattributed values are separate fields. The command center keeps stale data visible during refresh, shows freshness and partial errors by resource, and gives planned-versus-effective field comparisons rather than generic success/failure prose.

`TrackingHealthPolicy` versions referral, exact-match, attribution, delay, acceptance, conflict and correction thresholds per goal. `Unsafe` always activates `TrackingUnsafe` Emergency Stop. Connection/destination revoke blocks commands, protects and reconciles owned delivery while credentials remain usable, then disposes credentials/routing; an explicit force revoke records potentially unmonitored continuing spend rather than claiming success.

The `impeccable` product register shapes the UI: restrained navy surfaces, cyan only for focus/current action, semantic warning/error states, high density without nested card walls, standard tabs and tables, skeleton loading, visible keyboard focus, Arabic RTL and state-only motion.

## Data and Migration Plan

1. Change executable Meta defaults in `.env.example`, Compose, backend startup and Advertising options from v25.0 to v26.0 only with adapter/provider contract coverage; startup rejects unsupported versions.
2. Add WABA/phone/capability/integration-mode/timezone fields, authorized offer-destination pairs, hard audience boundaries and customer-audience grants without removing existing columns.
3. Add normalized campaign, ownership, ad-set, plan, audience, experiment, provider-operation/snapshot, disconnect-saga, multi-period budget-debit, WhatsApp-observation/attribution, conversion delivery/attempt, webhook-source, tracking-policy/health, AI-work, disable/audit and decision-impact tables.
4. Backfill existing `ManagedAdvertisement` rows into hierarchy records with local `LegacyUnverified` plus `ManualUnowned` state. Preserve external IDs and history; the migration makes no Meta status/budget call.
5. Keep legacy plans and discovered campaigns read-only for autonomy until an Owner/Admin explicitly imports ownership and live read-back proves destination, parents, targeting, placement mode and budget owner. “Paused for autonomy” never means pausing an unimported provider campaign.
6. Backfill conversions without `ctwa_clid` as explicitly unattributed. Do not manufacture touches.
7. Keep old JSON/config fields for one compatibility migration; switch reads to normalized records and remove obsolete columns only in a later separately approved cleanup.
8. Deploy registered outbox handlers and Advertising projection consumers before enabling new Project/Knowledge/Media/CRM/Booking producers; backfill projection snapshots, then remove direct cross-module reads.
9. Replace Advertising's direct Media service dependency with the Shared object-storage abstraction and the direct Gemini configuration/service path with asynchronous AI work before enabling the new creative/decision jobs.
10. Use real provider-neutral concurrency tokens and project-scoped unique/check indexes. Migration tests run against disposable PostgreSQL, not only in-memory EF.

## API and Contract Plan

- A global single-use-state OAuth callback resolves project/user server-side; project-scoped connection endpoints discover/select ad account, Page, WABA, phone, Dataset, integration mode and a capability snapshot.
- Envelope endpoints manage offer-to-destination pairs, audience grants, hard controls, financial bounds and IANA reporting timezone. Normal disable is an audited per-request choice, not a standing envelope permission.
- Strategy/plan endpoints are asynchronous operator controls and return immutable plan versions, validation findings and operation progress. Once the envelope/Autopilot is active, system actors invoke the same application services autonomously inside that version without per-action approval.
- Campaign detail returns the full hierarchy and planned/effective diff.
- Audience, creative, experiment, outcome, decision, incident and tracking-health endpoints are independently paginated.
- Normal disable defaults to `PauseManaged`; `LeaveRunning` is explicit and leaves monitoring active.
- All mutations require `Idempotency-Key`; concurrency-sensitive updates require `If-Match`.
- Errors include stage, object type/ID, field, provider code/subcode/message, trace ID, retryability and next safe action, with secrets/PII removed.
- Cross-module facts use versioned transactional outbox/inbox contracts.
- Webhook-source rotate/revoke, Business Messaging test-event/readiness, audit and monotonic change-cursor endpoints cover the Settings and <=60-second UI update contracts.
- The Cloud webhook has an absolute public verification/signature contract and resolves `phone_number_id` through a globally unique WhatsApp route projection before publishing to Conversations.
- Disconnect/destination revoke is an asynchronous protective-stop/reconcile/credential-disposal saga; explicit LeaveRunning/force-revoke paths audit continuing or unmonitored spend.

## Job and Reliability Plan

| Work | Cadence | Required behavior |
|---|---:|---|
| Conversion/outbox dispatch | Event-driven plus 1 min retry | Idempotent delivery and bounded failure |
| Spend guard | 5 min | Pull freshest managed spend, forecast reserve/cap and stop risk |
| Provider reconciliation | 10 min | Read hierarchy, review/effective state, targeting and drift |
| Insights | 15 min with overlap | Incremental windowed spend/actions/placement observations |
| Tracking health | 15 min | Referral coverage, attributable outcomes, delay, conflicts and CAPI acceptance |
| Decision cycle | Hourly | Mature evidence, independent review, safety, one command |
| Impact review | 2 hours | Only due windows; compare stored baseline and outcome |
| Fatigue | 6 hours | Impression/frequency/outcome decline gates |
| Experiment proposal | 1 to 3 days | Only when sample and budget capacity exist |
| Portfolio allocation | Daily 04:00 in active IANA project timezone | Release/reserve next-period budget atomically across daily and monthly/total ledgers |
| Strategy review | Monday 05:00 in active IANA project timezone | Offer, outcome fallback, audience and creative portfolio |

Every global dispatcher enumerates project IDs, resolves the active validated IANA timezone (including DST for the relevant date), then a project job acquires a Redis lease and durable UTC `(ProjectId, JobType, TimeBucket)` guard. It rechecks stop, envelope, connection, capability and tracking after the lease. Invalid/missing timezone fails closed. Monitoring and reconciliation continue while Autopilot is disabled or financially frozen.

Every authoritative Advertising audit row writes `AdvertisingAuditRecorded.v1` transactionally. The shared audit indexer writes Elasticsearch with retry/dead-letter health; PostgreSQL remains authoritative and index failure cannot erase or roll back the audit.

## Test Strategy

### Deterministic/unit

- Campaign-plan compatibility and WhatsApp invariants.
- Objective/goal runtime fallback and refusal of traffic/link-click goals.
- Hard controls versus suggestions, removed detailed exclusions and special-category rules.
- Consolidation, campaign budget ownership, atomic daily-plus-period reserve/release and delayed-spend safety formula.
- Experiment one-variable and maturity gates.
- Cross-conversation last-eligible-touch attribution with deterministic tie-break, canonical dedupe, corrections, consent revocation and unattributed truth.
- `WAIT` on sparse/late/unhealthy evidence and no target CPA from daily budget.
- Full action catalog, independent review and final safety.

### Provider/gateway contracts

- Stateful fake Meta capability and request capture.
- Validation failure, partial hierarchy, timeout/unknown result, drift, rejection and read-back recovery.
- Image, video, existing-post, clone and replacement payload/read-back parity.
- Cloud API/coexistence challenge/signature/routing/dedupe fixtures plus Baileys wrapper coverage for first and later present/opaque/missing identifiers; opaque payloads are never parsed.
- A manual real-account referral proof is a production go/no-go. Installed optional Baileys typings never satisfy it alone.
- Business Messaging adapter tests assert `POST /{dataset-id}/events`, one `data[]` wrapper, WABA plus `ctwa_clid` inside `user_data`, test-event handling and accepted/warning/retry response parsing.

### API/integration/frontend

- Project/role isolation, idempotency, ETag and typed error contracts.
- WhatsApp inbound outbox to Conversations to first/later attribution observation to qualified/paid/corrected delivery.
- Async AI request/completion, credential non-disclosure, stale/gapped event versions and Elasticsearch audit-index retry.
- Coherent reporting windows and source labels.
- URL-addressable views, project-switch cancellation, retained data on partial refresh, plan diff and experiment state.
- Keyboard, focus, RTL, reduced motion and 375/768/1024/1440 structure.
- Emergency Stop and both normal-disable modes.

Exact commands are maintained in [quickstart.md](quickstart.md).

## Rollout

1. Ship schema, registered outbox handlers/projections, read-only capabilities and attribution observations with Autopilot disabled.
2. Configure supported WhatsApp Cloud API/coexistence, prove a real `referral.ctwa_clid` fixture for the selected destination and show missing/opaque cases as unsafe.
3. Prove fake-provider plan compilation, exact-shape validation, partial recovery and field-level read-back equivalence.
4. Send a Business Messaging test event and verify Dataset/Events Manager acceptance.
5. Create one real hierarchy paused and reconcile it without activation.
6. Exercise both normal-stop modes, Emergency Stop and unknown-result recovery.
7. Activate one guarded real-spend canary within the existing envelope.
8. Enable budget, experiment and optimization-goal autonomy only after attribution and stop gates stay healthy.

Rollback disables new plan/decision commands, keeps monitoring/reconciliation active, pauses system-owned delivery when required and preserves all external/local history. No rollback deletes provider campaigns.

## Complexity Tracking

No constitution violation needs an exception. The added entities represent externally observable state, independent experiment/evidence lifecycles or required reliability boundaries. The provider mapper/reconciler split avoids leaking Meta payloads into domain services; it is not a second provider framework.
