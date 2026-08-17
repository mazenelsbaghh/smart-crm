# Implementation Plan: Autonomous Facebook Ads Manager

**Branch**: `033-facebook-ads-manager` | **Date**: 2026-08-17 | **Spec**: `specs/033-facebook-ads-manager/spec.md`

## Summary

Add a project-isolated `Advertising` module and a dedicated Arabic `مدير الإعلانات` workspace. V1 connects a Facebook ad account, Page and Dataset/Pixel; derives a sourced offer profile from published project knowledge; ranks existing Page posts and project images/videos; launches Facebook-only managed campaigns; attributes canonical business conversions; and executes independently reviewed budget decisions inside an explicit Owner/Admin authorization envelope. PostgreSQL remains the source of truth, Hangfire runs recurring and command jobs, Redis supplies project locks, RabbitMQ/outbox messages carry cross-module outcomes, and a typed Meta Graph REST client performs versioned/idempotent external mutations.

## Technical Context

**Language/Version**: C# 13 on .NET 9; TypeScript 5 / React 19.2 / Next.js 16.2; Python 3 integration tests

**Primary Dependencies**: ASP.NET Core, EF Core 9/Npgsql, Hangfire PostgreSQL, StackExchange.Redis, RabbitMQ.Client, ASP.NET Data Protection, ImageSharp, existing Gemini client, Next.js App Router, axios, SignalR, CSS Modules

**Storage**: PostgreSQL for configuration, ledgers and durable commands; Redis for leases/state; MinIO/S3 for derived media; RabbitMQ plus transactional outbox for integration events

**Testing**: `dotnet build`; focused deterministic service tests and Python contract/integration tests; frontend lint and production build

**Target Platform**: Linux containers, Chromium-class desktop/mobile browsers, Meta Graph Marketing API v25.0 by configurable allowlisted version

**Project Type**: Modular-monolith web application with ASP.NET API/background workers and Next.js dashboard

**Performance Goals**: dashboard first useful state under 2 seconds on normal project data; webhook acknowledgement under 500 ms excluding asynchronous Meta delivery; no overlapping job for the same project; command execution at-most-once by idempotency key

**Constraints**: Facebook placements only; no fabricated offer facts or source media; project-wide hard authorization cap; secrets encrypted and never returned; financial changes freeze when tracking/authorization is unhealthy; no permanent campaign deletion; Meta insight/spend data can be delayed, so a reserve and reconciliation are mandatory

**Scale/Scope**: one connection/envelope per active project; tens of campaigns, hundreds of ads/creatives and millions of append-only insight/conversion rows over time; six dashboard views; immediate event ingestion plus nine recurring job classes

## Constitution Check

### Pre-design gate

- **Tenant isolation — PASS**: every Advertising aggregate implements `ITenantEntity`; route project identity is re-authorized against JWT identity; background queries use explicit ProjectId even when filters are bypassed.
- **Modular boundaries — PASS**: `Modules/Advertising` owns its tables and consumes published integration events instead of querying CRM, Knowledge, Appointments or Conversations tables directly.
- **Security — PASS**: OAuth state is server-side and single-use; access tokens and webhook secrets use Data Protection; HMAC replay protection and consent-aware customer matching are required.
- **Reliable integrations — PASS**: Meta calls use a typed client, bounded retries only for safe calls, durable commands, idempotency keys and post-timeout reconciliation.
- **Risk-based authorization — PASS**: real-spend actions may execute only inside the explicit, revocable autonomy envelope after independent and deterministic checks; outside-envelope actions await Owner/Admin authority.
- **Auditability and safe failure — PASS**: every proposal/review/command/outcome is durable; tracking or connection failure freezes finance; Emergency Stop pauses owned ads without deletion.
- **Testing and observability — PASS**: allocation and safety rules are deterministic/testable; jobs emit structured project/correlation logs and health timestamps.

### Post-design gate

The design preserves the gates. The shared transactional integration outbox is a justified infrastructure extension because payment/attendance outcomes cannot tolerate the save-then-publish loss window. It does not give Advertising direct ownership of source-module data. No constitution exception remains.

## Project Structure

### Documentation

```text
specs/033-facebook-ads-manager/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── http-api.md
│   ├── integration-events.md
│   └── jobs-and-safety.md
└── tasks.md
```

### Source Code

```text
backend/
├── Program.cs
├── Migrations/
└── src/
    ├── Modules/
    │   └── Advertising/
    │       ├── API/
    │       ├── Domain/
    │       ├── DTOs/
    │       ├── Jobs/
    │       ├── Services/
    │       └── Workers/
    └── Shared/
        ├── Domain/IntegrationOutboxMessage.cs
        ├── Queue/AdvertisingIntegrationEvents.cs
        └── Security/ProjectAuthorizationService.cs

frontend/src/
├── app/(dashboard)/management/ad-manager/page.tsx
├── config/navigation.ts
└── packages/ad-manager/
    ├── api/
    ├── components/
    ├── hooks/
    ├── types/
    └── AdManager.module.css

tests/phase_1/
├── test_facebook_ads_manager.py
├── test_facebook_ads_conversions.py
└── test_facebook_ads_safety_jobs.py
```

**Structure Decision**: extend the existing modular monolith rather than add a service. Advertising owns its domain and public API; a small shared authorization/outbox seam is added because tenant authorization and reliable cross-module delivery are platform responsibilities. The frontend uses one feature package and one shared navigation definition so desktop and mobile cannot drift.

## Delivery Design

### Phase 0 research output

`research.md` records the resolved Meta API, OAuth, placement, creative, conversion, consent, budget, reliability, scheduling, UI and testing decisions. No technical clarification remains open.

### Phase 1 design output

`data-model.md` defines owned aggregates, constraints and state machines; `contracts/` defines HTTP, event, job and safety boundaries; `quickstart.md` defines mock-first verification and the real-spend readiness gate.

### Backend slices

1. **Connection and readiness**: server-side Meta OAuth, encrypted token vault, resource discovery, permission/account/Page/Dataset validation, connection health and a readiness checklist.
2. **Profile and creative intake**: consume published knowledge snapshots, build a citation-bearing immutable profile, import eligible Page posts, accept project media snapshots, validate policy/rights/format and generate only allowed variants.
3. **Campaign control**: plan managed campaign/ad-set/ad records locally, create them paused in Meta, reconcile external state, then activate only through a reviewed command.
4. **Conversion ledger**: secure generic webhook plus internal integration events, canonical deduplication, attribution touches, negative adjustments, consent-aware Meta Dataset delivery and retry/reconciliation.
5. **Decision loop**: deterministic eligibility/evidence calculations, isolated Strategist/Auditor prompts, optional Judge, Safety Engine, durable command, effect evaluation and rollback-as-new-command.
6. **Operations**: per-project Hangfire jobs, Redis leases, spend reserve, tracking incidents, normal Autopilot stop, Emergency Stop, alerts and structured audit data.

### Frontend workspace

- Re-label the current campaign destination `حملات واتساب` and add `مدير الإعلانات` to the shared desktop/mobile shell navigation.
- Unconfigured projects see a linear readiness checklist; configured projects see a persistent health/action strip and tabs for Overview, Campaigns, Creatives, Conversions, AI Decisions and Settings.
- Preserve the existing dense dark product language, Inter/system typography and restrained cyan accent. Use standard tabs/tables and meaningful funnel/allocation visuals only; avoid gradients, glass defaults, card-wall layouts, modal-first flows and decorative motion.
- Include keyboard-visible focus, semantic tables, `aria-live` failures, skeleton loading, reduced motion, RTL Arabic, and verified layouts at 375/768/1024/1440 widths.

### Job schedule

- Immediate: conversion/event ingestion and execution-command dispatch.
- Every 5 minutes: managed spend/cap monitor.
- Every 10 minutes: external-state reconciliation.
- Every 15 minutes: insights pull and tracking health.
- Hourly: evidence calculation, strategy/review/safety decision cycle.
- Every 2 hours: eligible decision-effect evaluation.
- Every 6 hours: fatigue evaluation.
- Daily at 04:00 Africa/Cairo: allocation rebalance and retention rollups.
- Every 1–3 days: propose budget-eligible creative/audience tests.
- Monday 05:00 Africa/Cairo: strategy/profile review.

Global recurring jobs enumerate eligible project IDs, enqueue a project job and acquire `advertising:{job}:{projectId}` Redis leases. Each job rechecks project, connection, tracking, envelope and stop state after obtaining its lease.

## Rollout and Migration

1. Add schema, services and APIs with Autopilot disabled by default.
2. Update Graph configuration default from v20.0 to v25.0 and validate configured version at startup; do not silently enable newly introduced placements.
3. Add connection/readiness UI and verify Page/ad-account/Dataset permissions.
4. Enable generic conversion intake and internal business-event adapters; prove deduplication and tracking health.
5. Enable paused campaign creation/reconciliation, then guarded canary activation inside the approved cap.
6. Enable autonomous hourly decisions only after all readiness checks pass. No shadow mode is introduced, per the confirmed requirement.

## Complexity Tracking

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Shared transactional integration outbox | Preserve critical payment, refund, booking and attendance events across process/RabbitMQ failure | Publishing only after `SaveChanges` has an unrecoverable loss window |
| Durable command + reconciliation state machine | Meta timeouts can leave mutation outcome unknown | Retrying the HTTP request can duplicate financial mutations |
| Separate Strategist/Auditor/Judge records | Required independent review and explainable decisions | One opaque AI response cannot prove review separation or safe `WAIT` behavior |
