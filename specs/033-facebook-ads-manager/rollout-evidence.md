# Rollout evidence

Last updated: 2026-08-19 06:41 EEST (Africa/Cairo).

## Current rollout state

`Advertising__Enabled=false`, `Advertising__AllowRealActivation=false`, and mock delivery are the safe local defaults. Production activation is not authorized.

| Gate | Status | Evidence |
|---|---|---|
| Graph v26 configuration and startup rejection | Local PASS | Unit/configuration tests and backend build |
| Registry, projections, idempotency, API mutation contract | Local PASS | 169 unit tests, including concrete outbox dispatch, module boundary and registration contracts |
| Schema migration on clean/existing PostgreSQL | Local PASS | Real PostgreSQL 17 + pgvector; integration suite 3/3 |
| Resumable projection backfill/parity on PostgreSQL | Local PASS | Backfill ran twice with one completed run and no failure code; module boundary test confirms legacy reads removed |
| Fake-provider paused/unknown/drift/WhatsApp invariants | Local PASS | Provider, provisioning, Advantage+, and safety unit suites |
| Cloud/Baileys referral parsing and no opaque decode | Local PASS | 4/4 gateway tests plus backend webhook/attribution tests |
| Focused no-spend HTTP workflows | Local PASS | 22/22 connection, strategy, provisioning, budget, outcome, decision and safety acceptance tests |
| Historical cross-module regression | Local PASS | 68/68 Phase 1/3/4/5 HTTP tests with real PostgreSQL, Redis and MinIO plus mock external providers |
| Real paused hierarchy and effective read-back | NOT RUN | Requires authorized Meta account |
| Real documented `referral.ctwa_clid` | NOT RUN | Requires selected Cloud API/coexistence destination |
| Dataset Business Messaging test event | NOT RUN | Requires WABA Dataset permissions and Events Manager confirmation |
| Owner/Admin bounded real-spend canary | FORBIDDEN | Prior gates incomplete; activation flag remains false |

The local rollout order is proven through schema/registry/projection tests with Autopilot disabled, idempotent backfill/parity, then fake-provider invariant and failure gates. This does not authorize real Meta delivery.

## Canary authority requirements

Before the last row can change, evidence must record the approving Owner/Admin, exact offer and WhatsApp destination, daily and monthly/total caps, safety reserve, countries/exclusions, minimum age, languages, timezone, capability expiry, fresh healthy tracking, stop readiness, and the exact paused objects read back from Meta. A real spend result must never be inferred from mock or unit tests.
