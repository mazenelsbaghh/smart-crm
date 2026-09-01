# Quickstart: Autonomous WhatsApp AI Media Buyer

This feature can create provider objects and spend real money. Complete the mock, referral, validation-only and paused-read-back checks before any real activation.

## Configuration

Provide secrets through environment or secret storage; never commit values:

```text
FACEBOOK_APP_ID
FACEBOOK_APP_SECRET
FACEBOOK_ADS_OAUTH_REDIRECT_URI
FACEBOOK_GRAPH_API_VERSION=v26.0
WHATSAPP_CLOUD_VERIFY_TOKEN
ADVERTISING_META_USE_MOCK=true         # Compose-facing; Development/Test only
ADVERTISING_SAFETY_RESERVE_PERCENT=15  # Compose-facing
DataProtection__KeysPath=/app/data-protection-keys  # Direct backend process; Compose mounts this path
```

The Data Protection path must use a persistent access-restricted volume. Rotate any secret that was previously committed, printed or exposed in a Compose default. Graph `v26.0` is the supported default for this delivery, but every account/object combination is still runtime capability-checked before mutation.

## Local no-spend verification

1. Start PostgreSQL, Redis, RabbitMQ, MinIO, backend, frontend and WhatsApp gateway with the mock Meta provider enabled.
2. Apply the generated database migration, then sign in as an Owner/Admin and open `/management/ad-manager` for a test project.
3. Complete mock readiness for the Ad Account, Page, WABA phone identity and WABA-associated Dataset. Verify the UI lists exact missing permissions/capabilities instead of a generic connection error.
4. Publish one factual offer, authorize one offer-to-WhatsApp destination pair, configure hard audience controls and set a small cap with reserve.
5. Generate a plan and verify all of the following before provisioning:
   - destination is the authorized WhatsApp identity at campaign-plan, ad-set and creative/ad levels;
   - the optimization goal is messaging-compatible and never traffic, landing-page views or link clicks;
   - Advantage+ placements are requested without a stale publisher/position allowlist;
   - audience hard controls are separate from AI suggestions;
   - special-ad-category classification is explicit;
   - plan fingerprint and capability snapshot are recorded.
6. Run validation-only, provision the complete campaign -> ad set -> creative -> ad hierarchy as paused, and read every object back. The hierarchy must remain `VerifiedPaused`; no mock spend or activation is allowed in this step.
7. Inject partial-create, timeout-after-create, provider rejection and read-back drift failures. Verify reconciliation finds the existing object, does not duplicate it, keeps delivery paused and exposes the exact failed stage/field/provider trace.
8. Replay the same command/idempotency key. Verify at most one external mutation and one effective local result.

## WhatsApp referral and outcome verification

1. Run Cloud API/coexistence challenge, raw-body signature, unknown/ambiguous phone routing, provider-message dedupe and referral fixtures plus gateway extractor tests with direct, ephemeral, view-once and media-wrapped Baileys messages containing present, opaque and missing identifiers.
2. Submit one authenticated first inbound message twice, then a later message with a different valid referral. Verify Conversations stores each provider message once, publishes the first observation once plus the later touch once, and logs/ordinary APIs never expose raw `ctwa_clid`.
3. Verify Advertising creates one protected attribution context and preserves subsequent eligible referrals as separate touches.
4. Submit a qualified-lead or paid-order business event tied to that conversation. Verify one canonical outcome, last-eligible-WhatsApp-touch attribution and one Business Messaging delivery identity.
5. Submit a refund/cancellation correction. Verify an append-only adjustment changes net reporting without erasing the original outcome.
6. Submit an outcome without referral. Verify it is shown as `Unattributed`, not assigned by phone/time proximity and not counted as attributed success.
7. Send the same Business Messaging event twice. Verify `POST /{dataset-id}/events` has one `data[]` wrapper, `action_source=business_messaging`, `messaging_channel=whatsapp`, and WABA ID plus `ctwa_clid` inside `user_data`, while producing only one provider event identity and multiple child attempts only when a retry is forced.
8. Before production, capture a real click-to-WhatsApp message through the selected Cloud API/coexistence destination and verify documented `referral.ctwa_clid`. Baileys field presence or an opaque payload does not pass this go/no-go; tracking stays `Unsafe` and outcome-based financial changes stay `WAIT`.

## Decision and safety verification

1. Run a decision cycle with sparse/delayed evidence and verify `WAIT` with exact maturity/coverage reason codes before any AI mutation.
2. Run Strategist -> Auditor -> optional Judge -> Safety Engine using only server-provided IDs and the closed action catalog.
3. Verify lowest-cost/highest-volume is the initial bid strategy; a cost cap is ineligible until mature attributed economics and runtime provider validation exist.
4. Verify allocation reserve/release is atomic, remaining period cap cannot be exceeded and the five-minute spend guard reads fresher spend than analytical snapshots.
5. Disable Autopilot using the default `PauseManaged`: managed active objects pause while insights, tracking and reconciliation continue.
6. Disable using explicit `LeaveRunning`: autonomous mutations stop, existing delivery can continue and the UI persistently warns that real spend continues.
7. Activate Emergency Stop. Verify pending financial commands stop, every owned active object receives an idempotent protective pause, per-object progress is visible and nothing is deleted.
8. Disconnect the connection and revoke one active destination. Verify the default flow blocks commands, pauses/reconciles owned delivery while credentials work, then disposes credentials/routing. Force-revoke failure must say delivery may continue unmonitored and never claim a successful stop.

## Real Meta prerequisites

- Production access for `ads_read`, `ads_management` and required business/Page discovery.
- WABA/Dataset readiness and the required `whatsapp_business_management` and `whatsapp_business_manage_events` access for Business Messaging events.
- An active funded Ad Account with verified currency/timezone plus an authorized Page, WABA phone and WABA-associated Dataset.
- Live account validation of the exact WhatsApp destination, objective, optimization goal, bid, creative and dynamic Advantage+ placement combination.
- Valid privacy notice and recorded legal basis for every customer/custom-audience source.
- Verified offer facts, rights-cleared media, special-category decision and prohibited-claim checks.
- Accepted test Dataset event, healthy webhook/referral capture and visible Events Manager confirmation.
- Owner/Admin-approved cap, reserve, time window, geography, minimum age/language controls and maximum autonomous increase.

Turn `Advertising__Meta__UseMock` off only after all checks pass. First real provisioning still creates and verifies a paused hierarchy. First activation is a guarded low-cap canary and can spend real money.

### Paused-only capability evidence checklist

Use `tests/phase_1/fixtures/advertising/meta-capabilities.json` as the expected evidence shape. For one real project, record and verify all of the following without activating delivery:

1. Graph API is exactly `v26.0`; the access token has every listed permission and no token appears in logs, responses or fixtures.
2. The chosen Ad Account is active and its currency/timezone match the owner-authorized envelope.
3. The Page, WABA, receiving phone and WABA-associated Dataset are visible through the same authorized business chain; the phone is not active in another project.
4. Current runtime probes confirm WhatsApp destination, Conversations optimization, lowest-cost bidding and Advantage+ automatic placement eligibility. Enum presence alone is not evidence.
5. Persist the provider trace, configured/effective fields, checked time and an expiry no later than six hours.
6. Keep `Advertising__AllowRealActivation=false`. The only permitted real mutation at this gate is a validation-only request or, later, one complete hierarchy created and read back as paused.

## Build and tests

```bash
dotnet build backend/backend.csproj
dotnet test backend/tests/Advertising.UnitTests/Advertising.UnitTests.csproj
dotnet test backend/tests/Advertising.IntegrationTests/Advertising.IntegrationTests.csproj
(cd whatsapp-gateway && npm test)
(cd frontend && npm test)
(cd frontend && npm run lint -- 'src/app/(dashboard)/management/ad-manager/page.tsx' src/packages/ad-manager)
(cd frontend && npx tsc --noEmit)
(cd frontend && npm run test:e2e)
(cd frontend && npm run build)
docker compose config
TEST_API_BASE_URL=http://localhost:${BACKEND_PORT:-5000}/api .venv/bin/pytest -q tests/phase_1/test_facebook_ads_manager.py tests/phase_1/test_facebook_ads_conversions.py tests/phase_1/test_facebook_ads_safety_jobs.py tests/phase_1/test_whatsapp_gateway.py
```

Also run the relevant knowledge, CRM, booking, messaging and media regression suites because Advertising consumes their versioned integration events.

The focused lint command above is the acceptance gate for this feature. A repository-wide `npm run lint` may expose pre-existing errors in unrelated workspaces; record those separately and do not hide a failure in any ad-manager file. PostgreSQL integration and Python API acceptance tests require a responsive Docker/PostgreSQL environment and a running local stack; never replace them with an in-memory result.

Before accepting the suite, replace every obsolete assertion that requires a Facebook-only publisher/position list with dynamic Advantage+ configuration evidence plus invariant WhatsApp destination checks. The gateway package must define its `npm test` script as part of this feature.

## Recurring schedule

Schedules that represent business time are converted from the project timezone and stored as UTC execution instants. For the current Cairo deployment (UTC+3 on 2026-08-18), the daily 04:00 Cairo rebalance runs at 01:00 UTC.

| Work | Schedule |
|---|---:|
| Conversion/outbox retry | Every minute |
| Fresh-spend guard | Every 5 minutes |
| Provider state/read-back reconciliation | Every 10 minutes |
| Incremental insights and tracking health | Every 15 minutes |
| Decision cycle | Hourly |
| Mature impact review | Every 2 hours |
| Creative fatigue | Every 6 hours |
| Portfolio rebalance | Daily at 04:00 Cairo |
| Experiment proposal | Every 2 days when eligible |
| Strategy review | Monday at 05:00 Cairo |
| Retention | Daily at 05:43 Cairo |

Each global dispatcher enqueues one project job. The project job obtains a Redis lease, claims a durable database time bucket and rechecks connection, capability, tracking, envelope, provider drift, cap and stop state before mutation.

## Operational checks

- Confirm Hangfire has at most one effective project run per job/time bucket.
- Confirm logs contain project/correlation/command IDs but no tokens, match PII or raw `ctwa_clid`.
- Confirm read-only sync continues during tracking freeze, normal disable and Emergency Stop.
- Simulate token expiry, permission loss, WABA/Dataset mismatch, event rejection, delayed insights, provider timeout and RabbitMQ unavailability.
- Confirm poison events and provider retries are bounded, idempotent and produce actionable incidents.
- Compare internal attributed outcomes, unattributed outcomes and Meta-reported results as separate truth sources for one coherent window.
- Validate RTL layout, keyboard focus, screen-reader labels, stale/partial states and reduced motion at 375, 768, 1024 and 1440 px.
