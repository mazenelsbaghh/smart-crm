# Clean-code review

Reviewed on 2026-08-19 (Africa/Cairo) after the Phase 8 implementation pass.

## Scope

- Advertising production code, shared event/audit/storage seams, WhatsApp attribution ingress, and the ad-manager workspace.
- Clean Code, SOLID, DRY, KISS, YAGNI, and LLM-specific failure modes.
- Financial mutations, ownership, rollout gates, tracking freshness, retention, and privacy were treated as blocking concerns.

## Findings resolved

1. Financial readiness previously treated the absence of an incident as sufficient tracking proof. `AdvertisingOperationalPolicy` now requires a recent `Healthy` snapshot and no open incident; readiness, activation, safety, overview, and emergency-stop recovery use the same rule.
2. The enable endpoint could activate local authority before discovering that real activation was disabled. It now fails with `ADS_REAL_ACTIVATION_DISABLED` before mutating the envelope when the rollout/canary gate is closed.
3. The legacy creative-test path created provider objects directly, used stale Facebook/manual-placement assumptions, and bypassed the current budget and decision model. It now clones exactly the creative variable, provisions and reads back a paused hierarchy, reserves experiment budget atomically, and queues activation only through the AI review and safety pipeline.
4. The creative activation path could select manual/unowned ads. `AdvertisingDecisionService` and `WhatsAppCreativeTestService` now select only active ownership records explicitly controlled by Autopilot or imported with authority. A regression test proves a manual/unowned ad cannot enter review or create a command.
5. Retention attempted to remove shared outbox/inbox rows from an Advertising job. The job now limits deletion and compaction to Advertising-owned records while preserving authoritative audit/evidence history.
6. Settings silently supplied country, language, and spend defaults. Country/language authorization and spend caps now require explicit operator input; no geography is silently authorized.
7. Audit/error/Elasticsearch payloads could drift in redaction behavior. They now use the same sanitizer and the Elasticsearch projection indexes a sanitized document rather than the EF entity.

## Result

No unresolved production-code blocker was found in the local feature scope. The backend and the focused frontend feature compile after the fixes. Real Meta activation remains intentionally unavailable until the external paused-readback, CTWA referral, Dataset event, and bounded-canary gates have evidence.
