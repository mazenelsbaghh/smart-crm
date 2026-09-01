# Implementation Inventory: Autonomous WhatsApp AI Media Buyer

Inventory date: 2026-08-19 (Africa/Cairo). This is a code-backed baseline, not evidence that the feature is production-ready.

## Existing implementation

- The backend already has an `Advertising` module with connection, profile, creative, budget, conversion and decision entities; six project-scoped controllers; a Meta transport under `Infrastructure/Facebook`; recurring jobs; provider command handling; and a Development-only fake handler.
- The current unit-test project contains security, budget, creative, conversion, profile, decision and safety suites, but it has no PostgreSQL integration-test project or migration fixture.
- The frontend route and feature package exist, with API/state helpers, settings, creative/import components and a monolithic workspace page. The planned nine-view command-center shell and component/browser test harness do not exist.
- The WhatsApp gateway uses Baileys and has no `npm test` script. Its inbound pipeline retains some `contextInfo`, but there is no isolated referral extractor or fixture-backed Cloud/Baileys attribution contract.
- The shared database already contains Advertising tables and an integration outbox/inbox foundation. Changes must therefore be additive and tested as an existing-database upgrade.

## Unsafe or incomplete behavior found

- Executable defaults still use Graph `v25.0`; Compose defaults the fake Meta provider to enabled and uses the obsolete Facebook callback path.
- Startup accepts arbitrary Graph versions and does not reject a mock provider outside Development/Test or incomplete production credentials.
- `FacebookPageTokenResolver` reads `ConnectedPages` directly, `ProjectAiConfigurationProvider` reads `ProjectSettings` directly, `AdvertisingDecisionAi` injects the AI module, and `CreativeVariantJob` injects the Media module's storage service. These violate the modular-monolith event boundary.
- `AdvertisingOperationsController` also reads project AI settings directly.
- The outbox dispatcher uses a closed event switch and projection consumers do not yet implement monotonic source-version/gap/tombstone recovery.
- There is no official WhatsApp Cloud/coexistence webhook route projection, signed raw-body adapter, Conversations inbound consumer, or durable first/later advertising observation path.
- The current provider/client and jobs are Facebook-only in several assumptions, do not yet prove the complete paused hierarchy through typed v26 read-back, and still include unsafe schedule/decision shortcuts.
- The current decision/creative logic can optimize from weak messaging evidence and does not yet close the verified WhatsApp purchase/correction feedback loop.
- The current workspace is raw-table oriented and does not implement the planned persistent health/authority/stop strip, nine resource views, coherent metric windows or full responsive/accessibility states.

## Existing user changes that must be preserved

- `backend/Program.cs`, multiple Advertising services/provider/jobs, `docker-compose.yml`, the Ads Manager page/styles/types, and `whatsapp-gateway/src/baileys-manager.js` already contain uncommitted changes.
- Unrelated TalkTips, AI reply, CRM, Conversations, Projects, Settings and WhatsApp changes are also present. Advertising work must patch around them and must not revert or auto-commit them.
- New migrations already exist for unrelated TalkTips work. The Advertising migration must use a new timestamp/name and update the snapshot without altering those migrations.

## Safe implementation order

1. Add executable startup/version and migration tests before changing defaults or schema.
2. Add versioned integration contracts, consumers, resumable backfill and parity checks before enabling producers or removing direct reads.
3. Cut over cross-module storage/AI/page/context dependencies only after their projections or request/result paths are proven.
4. Keep Autopilot and real activation disabled while connection, planning, provider, attribution and stop layers are implemented.
5. Permit only fake-provider operations and a real paused read-back canary until official referral proof, Dataset test-event acceptance and stop/recovery drills pass.

## Verification baseline

- Required build/test commands are defined in `quickstart.md`.
- No clean baseline result is claimed here because the working tree contains active user changes. Each task is checked only after its focused test and the applicable project build pass.
