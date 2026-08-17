# Research: Autonomous Facebook Ads Manager

## 1. Meta integration surface

**Decision**: use a typed `HttpClient` REST adapter over the Meta Marketing Graph API, with `FACEBOOK_GRAPH_API_VERSION` configurable and defaulted to `v25.0`. Keep a code-owned allowlist of Facebook placement capabilities and reject every non-Facebook publisher platform.

**Rationale**: the repository is .NET and Meta does not publish an official current C# Business SDK. Meta's official SDK release stream shows v25 in 2026 and recent releases have removed/changed placements and automation behavior. A versioned REST boundary prevents vendor payloads from leaking through the domain and prevents old hard-coded `v20.0` behavior from silently selecting unsupported placements.

**Alternatives considered**: an unofficial C# SDK was rejected because maintenance and security lag are material for spend mutations; reusing the existing Page messaging client was rejected because it lacks advertising permissions/resources and exposes an insecure token flow.

**Primary references**: [Meta Marketing API documentation](https://developers.facebook.com/docs/marketing-api/), [official Meta Business SDK releases](https://github.com/facebook/facebook-python-business-sdk/releases), [official Meta Business SDK repository](https://github.com/facebook/facebook-python-business-sdk).

## 2. OAuth, permissions and secret storage

**Decision**: add a separate advertising OAuth flow with server-held single-use state, PKCE where supported, server-side callback exchange, encrypted long-lived token storage, resource selection and revalidation. Required capabilities are `ads_read`, `ads_management`, appropriate business/resource discovery permission and the existing Page permissions needed to read eligible Page posts. Never send the token to the browser.

**Rationale**: the existing Facebook connection is messaging-oriented; its scopes and plaintext/token-in-redirect flow are not acceptable for a financial integration. The repository already has an ASP.NET Data Protection vault pattern and persistent container key volume.

**Alternatives considered**: extending `ConnectedPage.AccessToken` was rejected because it conflates messaging and advertising authority; browser storage was rejected because it exposes the secret. A Business System User token remains a supported deployment option but is not required for local first connection.

## 3. Existing-post and asset creatives

**Decision**: preserve eligible Facebook Page posts using `object_story_id`. For project media, create an unpublished creative from verified offer copy and the existing image/video asset. The allowed derivative pipeline is copy/headline/CTA, crop, dimensions, thumbnail and format-preserving rendering only.

**Rationale**: this satisfies the confirmed requirement to run normal image, carousel and video ads while retaining source identity and preventing the AI from fabricating source imagery or commercial facts.

**Alternatives considered**: generating images/video from scratch was explicitly rejected; boosting every recent post was rejected because relevance, rights, policy, format and budget evidence must be checked first.

**Primary reference**: [Meta official Postman collection: create using existing creative](https://www.postman.com/meta/facebook-marketing-api/request/zakdxi8/createusingexistingcreative).

## 4. Placement policy

**Decision**: always send `publisher_platforms=["facebook"]` and select only code-verified Facebook positions supported by the configured API/account/creative. Do not name or depend on deprecated “Facebook Video Feeds”; prefer currently supported feed, stories, reels and other explicitly discovered Facebook positions.

**Rationale**: choosing Facebook as a destination is not sufficient to prevent Meta defaults from expanding delivery. Explicit publisher and position allowlists enforce the product boundary. Recent API releases removed at least one historical video placement.

**Alternatives considered**: Advantage+ automatic placements were rejected for V1 because they can expand beyond Facebook; hard-coding a permanent position list was rejected because placement availability changes.

## 5. Conversion ledger and Meta Conversions API

**Decision**: normalize every source outcome into a canonical, append-only project conversion ledger. Deduplicate on `(ProjectId, SourceSystem, ExternalEventId)` and a canonical business key; represent refunds/cancellations/absence/churn as linked adjustments. Deliver eligible events asynchronously to `/{dataset_id}/events` with a stable `event_id`; browser/server duplicates use the same `event_name` and `event_id`.

**Rationale**: business reporting needs one source of truth before sending data to Meta. Meta documents CAPI as accepting server, CRM, offline and website outcomes; consistent IDs support browser/server deduplication. The ledger also preserves deeper outcomes when they are too sparse to optimize directly.

**Alternatives considered**: counting Meta-reported conversions as authoritative was rejected because payments/refunds/attendance belong to business systems; the legacy Offline Conversions API was rejected in favor of Dataset/Conversions API delivery.

**Primary references**: [Meta About Conversions API](https://www.facebook.com/business/help/AboutConversionsAPI), [Meta Conversions API documentation](https://developers.facebook.com/docs/marketing-api/conversions-api/).

## 6. Consent-aware matching and webhook security

**Decision**: accept conversion events through authenticated internal events or an HMAC-SHA256 external webhook with timestamp/replay validation and idempotency. Normalize and protect permitted match data only when the payload carries a recorded consent/legal-basis state; otherwise send click identifiers and non-identifying event data only. Never log raw match data.

**Rationale**: it implements the user's selected option A and minimizes personal-data exposure. A project-specific encrypted webhook secret prevents one integration from forging another project's results.

**Alternatives considered**: always hashing and sending email/phone was rejected because hashing does not remove the need for a lawful basis; unauthenticated webhook ingestion was rejected due financial and optimization poisoning risk.

## 7. Budget semantics and hard authorization

**Decision**: treat the user's cap as the maximum authorized project spend across system-owned ads, not as a promise that Meta's delayed reporting will stop at an exact cent. Maintain a safety reserve, calculate usable allocation below the cap, poll spend every five minutes, block increases near the boundary and pause managed delivery on breach/abnormal forecast.

**Rationale**: external delivery and reporting are asynchronous. A local allocator alone cannot guarantee exact real-time spend. Authorization can still be strictly enforced by never issuing allocations above the envelope and by reserving for reporting delay.

**Alternatives considered**: dividing the full cap among ads was rejected because there is no room for delayed spend; one campaign per creative was rejected because it fragments small budgets and multiplies Meta minimum constraints.

## 8. Autonomous decision architecture

**Decision**: separate deterministic evidence calculation from AI narrative judgment. Strategist and Auditor are isolated structured calls; Auditor sees the proposal and raw evidence but not hidden Strategist reasoning. Judge runs only on configured escalation classes. The deterministic Safety Engine is final and cannot be overridden by an AI result.

**Rationale**: arithmetic, caps, placement boundaries, tracking health, minimum evidence, stale state and idempotency should not depend on a model. Separate durable records make `APPROVE`, `REJECT`, `WAIT` and `ESCALATE` explainable and testable.

**Alternatives considered**: a single model call was rejected as non-independent; three model calls for every no-op cycle were rejected as wasteful and less reliable than deterministic screening.

## 9. Reliable commands and cross-module events

**Decision**: use a durable `ExecutionCommand` state machine with a unique idempotency key, expected external version, request fingerprint, reconciliation state and one external mutation owner. Add a shared transactional integration outbox so source modules can save business state and its event in one PostgreSQL transaction; Advertising consumes at least once and deduplicates.

**Rationale**: Meta timeouts can make results unknown, while the current save-then-publish pattern can lose critical outcomes. A durable command and outbox close both failure windows without coupling module tables.

**Alternatives considered**: direct synchronous reads of CRM/appointments were rejected by module isolation; blind HTTP retry was rejected because it can duplicate financial mutations.

## 10. Scheduling and concurrency

**Decision**: use Hangfire recurring dispatchers that enumerate eligible projects, enqueue project-scoped jobs and acquire Redis leases named `advertising:{job}:{projectId}`. Jobs re-read stop/readiness state after the lease and use explicit ProjectId filters. Immediate commands and conversions are queue-driven.

**Rationale**: it matches existing infrastructure, avoids a recurring-job explosion, survives restarts and prevents two instances from mutating one project concurrently. Explicit tenant predicates are mandatory because background scopes have no request tenant.

**Alternatives considered**: in-process timers were rejected because they are not durable; one global mutation loop was rejected because one slow account would block every project.

## 11. UI system

**Decision**: add one shared navigation registry, a dedicated RTL workspace, a readiness-first empty state, a persistent health/action bar and six URL-addressable tabs. Preserve the project's dense dark visual language, system/Inter typography and restrained cyan accent; use semantic tables and only small useful allocation/funnel visuals.

**Rationale**: desktop/mobile navigation is currently duplicated. A shared registry prevents label/permission/shortcut drift. The feature has operational density and benefits from clear status hierarchy more than decorative cards.

**Alternatives considered**: embedding Ads inside WhatsApp Campaigns was rejected because the mental models and risk differ; glassmorphism, gradients, card grids and modal-first workflows were rejected for clarity, accessibility and consistency.

## 12. Testing strategy

**Decision**: test deterministic safety/allocation/idempotency at service level; API tenant/role/webhook contracts through integration tests; Meta behavior behind a fake HTTP handler enabled only in Development/Test; and UI navigation/readiness/emergency controls through build/lint plus focused component/E2E coverage supported by the repository.

**Rationale**: real Meta spend must never be part of automated tests. Most high-risk behavior is deterministic and can be verified without an external account.

**Alternatives considered**: test-only magic access tokens were rejected because they can leak into production branches; live sandbox spend was rejected because Meta test resources do not fully emulate delivery and financial behavior.
