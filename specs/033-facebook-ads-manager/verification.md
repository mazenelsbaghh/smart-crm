# Verification record

Last updated: 2026-08-19 06:41 EEST (Africa/Cairo).

## Passed after review fixes

| Check | Result |
|---|---|
| `dotnet build backend/backend.csproj --no-restore` | PASS; two pre-existing Firebase obsolete warnings, zero errors |
| `dotnet test backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj --no-restore` | PASS; 169/169 |
| PostgreSQL 17 integration suite with pgvector 0.8.6 | PASS; 3/3 clean migration, resumable backfill, and concurrent budget authority |
| `npm test` in `whatsapp-gateway` | PASS; 4/4 |
| `npm test` in `frontend` | PASS; 5/5 |
| focused ad-manager ESLint | PASS |
| `npx tsc --noEmit` in `frontend` | PASS |
| `npm run test:e2e` in `frontend` | PASS; 9/9 Chromium scenarios at 375, 768, 1024 and 1440 px |
| `npm run build` in `frontend` | PASS; production build and `/management/ad-manager` route |
| `docker compose config --quiet` | PASS |
| Focused HTTP acceptance for connection, planning, provisioning, budgets, attribution/CAPI, decisions, stop/recovery and gateway | PASS; 22/22 against PostgreSQL 17, Redis, mock Meta and the local gateway |
| Historical Phase 1/3/4/5 HTTP regression suite | PASS; 68/68 against PostgreSQL 17, Redis, MinIO, mock Gemini/Meta and the local gateway |

The unit run includes the module-boundary, API mutation contract, registration, privacy/redaction, tracking freshness, budget, provider reconciliation, attribution, CAPI, decision, stop, recovery, manual-ownership and concrete outbox-event dispatch regressions. The PostgreSQL fixture now accepts `ADVERTISING_TEST_POSTGRES_CONNECTION` for a real externally managed test database and otherwise retains the pgvector Testcontainers default. The collection shares one fixture and disables cross-class parallel migration, fixing the duplicate migration-history race found during verification.

The final HTTP run also proved that an approved knowledge document is dispatched as its concrete versioned event, projected into an eligible offer, bound to an authorized WhatsApp destination and active envelope, and used to import an existing click-to-WhatsApp campaign exactly once. Meta mock resource identities are project-scoped, and enum-string handling is scoped to Advertising request enums so legacy CRM response contracts remain unchanged.

## Local environment notes

- Docker Desktop's server did not answer `docker info`; a normal Desktop restart also remained stuck while stopping and was terminated after approximately 50 seconds. PostgreSQL integration was therefore run against a real local PostgreSQL 17 + pgvector instance and passed.
- The complete historical Phase 1/3/4/5 pytest sweep now passes 68/68. The direct-run harness uses the development in-memory event transport, a real local MinIO instance, and observable application outcomes instead of inspecting a RabbitMQ queue. Async scheduler, aggregation, multimodal AI and follow-up tests poll their final state within bounded deadlines. They do not claim an outbound WhatsApp reply was delivered when the local gateway correctly reports a disconnected session.
- Repository-wide frontend lint currently reports pre-existing errors in unrelated booking, Quran automation, shared CRM/management, settings, and service files. The complete ad-manager scope passes ESLint and TypeScript; the unrelated baseline is recorded rather than modified as part of this feature.

## External gates intentionally blocked

- No real Meta paused hierarchy/read-back was created.
- No real Cloud API/coexistence `referral.ctwa_clid` was captured.
- No real WABA Dataset test event was accepted and confirmed in Events Manager.
- No real-spend canary was enabled.

Real activation remains fail-closed through configuration and returns `ADS_REAL_ACTIVATION_DISABLED` until those gates are evidenced.
