# HTTP API Contract: WhatsApp AI Media Buyer

Base authenticated namespace: `/api/projects/{projectId}/ad-manager`. Every endpoint in that namespace requires JWT authentication and server-side project membership. `X-Project-Id` is correlation context only and never authority. The sole OAuth callback exception is the global `/api/ad-manager/meta/oauth/callback`; it accepts no project ID or JWT authority and resolves project/user only from a short-lived single-use server-side state record.

## Common protocol

- Mutations require `Idempotency-Key`; replay with the same normalized request returns the original operation, while a conflicting body returns `409 ADS_IDEMPOTENCY_CONFLICT`.
- Updates to envelope, plan control or stop state require `If-Match`; stale versions return `412 ADS_STATE_STALE` with the current ETag.
- Async mutations return `202 Accepted` with `operationId`, `resourceId`, `state`, `statusUrl` and `submittedAtUtc`.
- List endpoints use opaque cursor pagination with stable descending time/ID order and maximum `limit=100`.
- Operational responses include `asOfUtc`, `timezone`, `reportingWindow`, `attributionWindowDays`, `currency`, `freshness` and truth-source labels.
- No response contains access tokens, signing secrets after their one-time reveal, raw customer match data or raw `ctwa_clid`.

## Connection and capabilities

| Method | Path | Role | Result |
|---|---|---|---|
| POST | `/meta/oauth/start` | Owner/Admin | Opaque authorization URL and one-time state handle |
| GET | `/api/ad-manager/meta/oauth/callback` (absolute) | Anonymous provider redirect with valid one-time state | Stores token server-side and redirects with opaque result ID only |
| GET | `/meta/resources` | Owner/Admin | Ad accounts, Pages, WABAs, phone identities and Datasets without secrets |
| PUT | `/connection` | Owner/Admin | Selects account/Page/WABA/phone/Dataset and starts mutual validation |
| POST | `/connection/validate` | Owner/Admin | Refreshes permission, billing, capability and resource-link health |
| GET | `/connection` | Project member | Selected resources, state, freshness and exact blocking findings |
| GET | `/capabilities` | Project member | Current objective/goal/bid/placement/automation snapshot |
| POST | `/connection/disconnect` | Owner/Admin | Runs audited stop/transfer sequencing before credential disposal |
| GET | `/connection/disconnect/{operationId}` | Owner/Admin | Durable phase, per-owned-object read-back, credential/tombstone state and recovery action |
| GET | `/destinations` | Project member | Versioned WhatsApp destinations, integration/referral proof and eligibility |
| POST | `/destinations` | Owner/Admin | Validates and records a Page/WABA/phone/Dataset destination version |
| POST | `/destinations/{id}/revalidate` | Owner/Admin | Refreshes mutual eligibility and referral proof |
| POST | `/destinations/{id}/revoke` | Owner/Admin | Blocks new use, protects owned delivery targeting it, reconciles, then publishes a routing tombstone |

`PUT /connection`:

```json
{
  "adAccountId": "act_123",
  "pageId": "page_1",
  "wabaId": "waba_1",
  "phoneNumberId": "phone_1",
  "datasetId": "dataset_1"
}
```

Readiness is false unless all identities are mutually eligible and required production permissions are present. Business Messaging readiness also requires a supported Cloud API/coexistence integration, WABA Dataset association and a successful real referral/test-event proof; an optional Baileys field alone is not sufficient.

Envelope grant mutation uses local immutable `offerId` and `destinationId` values from these resources; provider/WABA/phone IDs are never accepted directly as AI-authored grant targets.

Disconnect/revoke returns a durable `ConnectionDisconnectOperation`. It defaults to `PauseManaged`: suspend the envelope and command claims, activate `LostAuthorization` protective stop, create idempotent per-ownership pause targets, reconcile every target while credentials are still usable, then revoke/erase credentials and publish the destination-route tombstone. Workers resume from the persisted phase after crashes. Explicit `LeaveRunning` requires per-request continuing-spend acknowledgement and retains least-privilege read/monitoring credentials until ownership transfer or later stop. A force revoke for suspected compromise may erase immediately, but finishes as `ManualActionRequired` while delivery is unverified, records that provider delivery may continue unmonitored and gives exact manual Meta stop steps; it never reports stopped without read-back proof.

## Envelope and authorization

| Method | Path | Role | Result |
|---|---|---|---|
| GET | `/envelope` | Project member | Active/draft envelope and ETag |
| PUT | `/envelope` | Owner/Admin | Creates a new immutable version and supersedes the old draft |
| POST | `/envelope/activate` | Owner/Admin | Activates after readiness validation |
| POST | `/envelope/suspend` | Owner/Admin | Blocks new autonomous mutations |
| POST | `/envelope/revoke` | Owner/Admin | Irrevocably ends this authorization version |

Envelope body includes:

- daily and period caps, currency, reserve, maximum increase and cooldown;
- start/end time and attribution window;
- authorized offer-to-WhatsApp destination pair IDs;
- hard location, minimum-age, required-language and custom-audience exclusions;
- individually authorized customer/retargeting/lookalike sources with legal-basis state;
- placement policy `DynamicEligibleMeta`;
- validated IANA reporting timezone and its source/version.

Normal disable mode is not a standing envelope permission. Each disable request defaults to `PauseManaged`; `LeaveRunning` requires an explicit per-request continuing-spend acknowledgement and audit actor/time.

## Strategy, plans and provider creation

| Method | Path | Role | Result |
|---|---|---|---|
| GET | `/strategy` | Project member | Current profile, authorized opportunities, selected goal and blockers |
| POST | `/strategy/refresh` | Owner/Admin | Refreshes facts, offers, economics/capacity and policy classification |
| GET | `/offers` | Project member | Sourced eligible/blocked offers and authorized destinations |
| POST | `/plans` | Owner/Admin operator | Requests immediate plan compilation inside the envelope |
| GET | `/plans/{planId}` | Project member | Plan, readiness, audience, creatives, experiment and validation state |
| POST | `/plans/{planId}/validate` | Owner/Admin operator | Requests local plus provider preflight validation, no spend |
| POST | `/plans/{planId}/provision` | Owner/Admin operator | Requests full paused hierarchy and read-back |
| POST | `/plans/{planId}/activate` | Owner/Admin operator | Requests guarded activation after fresh safety checks |
| GET | `/operations/{operationId}` | Project member | Saga progress, object state and actionable findings |

`GET /plans/{planId}` includes `plannedHierarchy` and `effectiveHierarchy`. Each critical field has `planned`, `effective`, `match`, `severity` and `reason`.

These endpoints are operator controls, not approval gates. After Owner/Admin activates an envelope and Autopilot, background system actors call the same application services directly and may compile, validate, provision and activate inside that exact envelope version without another human request. Out-of-envelope work remains blocked.

Provider object creation is never reported `ready` from an ID alone. Terminal success for provision is `VerifiedPaused` for campaign/ad set/ad and `Verified` for the non-delivery creative, with field-level equivalence and no blocking finding.

## Audiences, creatives and experiments

- `GET /audiences?state=&cursor=&limit=`
- `GET /audiences/{id}`
- `GET /creative-sources?type=&eligibility=&cursor=&limit=`
- `POST /creatives/analyze`
- `GET /creatives?state=&fatigue=&cursor=&limit=`
- `GET /experiments?state=&cursor=&limit=`
- `GET /experiments/{id}`
- `POST /experiments/{id}/stop`

Audience response separates `hardControls`, `suggestions`, `authorizedSources`, `estimatedReach`, `observedDelivery` and `changeEvidence`.

Experiment detail includes hypothesis, primary variable, control/arms, budget, maturity requirements, current evidence, attribution cutoff, stop rule and conclusion. `winner`/`loser` is absent until maturity.

## Operations and coherent reporting

- `GET /overview?from=&to=&timezone=&attributionWindowDays=`
- `GET /campaigns?state=&cursor=&limit=`
- `GET /campaigns/{id}`
- `GET /outcomes?type=&attributionState=&truthSource=&cursor=&limit=`
- `GET /decisions?verdict=&state=&cursor=&limit=`
- `GET /decisions/{id}`
- `GET /tracking-health?from=&to=`
- `GET /incidents?state=&cursor=&limit=`
- `GET /audit?category=&cursor=&limit=`
- `GET /changes?after=&limit=`

`/changes` returns a project-scoped monotonic cursor of plan, provider-operation, tracking, decision and stop-state changes. While an operation is non-terminal the UI polls it no slower than every five seconds and polls `/changes` no slower than every fifteen seconds; reconnect uses the last cursor. This is the fallback even when SignalR is available, and is the contract used to surface completed provider results within 60 seconds of local receipt.

Overview has separate fields for:

- observed spend and allocated/reserved amounts;
- provider-reported conversations;
- attributable new WhatsApp conversations;
- verified qualified leads, bookings, paid orders, corrections and net value;
- unattributed business outcomes;
- internal return and provider-reported return;
- referral/outcome attribution coverage and freshness.

All numerator/denominator values use the returned reporting window. Unknown values are `null` with a reason code, never numeric zero.

## Normal disable and Emergency Stop

| Method | Path | Role | Behavior |
|---|---|---|---|
| POST | `/autopilot/enable` | Owner/Admin | Enables decisions only after current readiness |
| POST | `/autopilot/disable` | Owner/Admin | Defaults to `PauseManaged`; explicit `LeaveRunning` allowed |
| GET | `/stop-state` | Project member | Autonomy, continuing spend and per-object pause progress |
| POST | `/emergency-stop` | Owner/Admin | Immediately blocks commands and queues all owned pauses |
| POST | `/emergency-stop/resume` | Owner/Admin | Requires fresh health, reconciliation and envelope authority |

Disable body:

```json
{
  "mode": "PauseManaged",
  "reason": "Operator requested normal stop"
}
```

`LeaveRunning` keeps spend/reconciliation monitoring active and returns `continuingSpend=true` until every managed object stops or Autopilot resumes.

## Webhook sources and Business Messaging readiness

| Method | Path | Role | Result |
|---|---|---|---|
| POST | `/webhook-sources` | Owner/Admin | Creates versioned source and reveals signing secret once |
| POST | `/webhook-sources/{id}/rotate` | Owner/Admin | Rotates with bounded overlap and one-time reveal |
| POST | `/webhook-sources/{id}/revoke` | Owner/Admin | Rejects future deliveries/retries from the source |
| GET | `/webhook-sources` | Owner/Admin | Safe status, event allowlist, freshness and replay failures |
| POST | `/business-messaging/test` | Owner/Admin | Sends a Dataset test event using `test_event_code` and records acceptance/warnings |
| GET | `/business-messaging/readiness` | Project member | Cloud/coexistence mode, WABA/Dataset, referral proof, permissions and last accepted test |

## Public WhatsApp Cloud API/coexistence webhook

Absolute route outside the project namespace: `/api/integrations/whatsapp/cloud`.

- `GET` implements Meta verification challenge. It uses constant-time comparison with the protected verify token and echoes `hub.challenge` only when `hub.mode=subscribe` and the token is valid.
- `POST` reads a bounded raw body, verifies `X-Hub-Signature-256` as HMAC-SHA256 with the configured Meta App secret before JSON parsing, and never accepts project/destination authority from the payload.
- The WhatsApp module extracts WABA/`phone_number_id`, resolves exactly one active `WhatsAppInboundRouteProjection`, deduplicates provider message ID, and transactionally writes `WhatsAppInboundMessageReceived.v1` to its outbox.
- Unknown/ambiguous routing, invalid signatures, oversized bodies and malformed provider IDs produce a security incident and no Conversation event. Logs contain hashes/correlation only, not sender, message body or raw referral.
- The endpoint returns success only after durable receipt/outbox commit and does not wait for Conversations, Advertising or AI consumers. Provider retry remains idempotent.

## External conversion webhook

`POST /api/integrations/ad-manager/{projectId}/conversions/{sourceKey}` requires:

- `X-Ads-Timestamp`: Unix seconds inside five-minute replay window.
- `X-Ads-Signature`: `v1=` plus HMAC-SHA256 of `timestamp + "." + rawBody`.
- `Idempotency-Key`: source delivery identity.

```json
{
  "schemaVersion": 2,
  "externalEventId": "pay_123",
  "businessAggregate": { "type": "Order", "id": "order_42" },
  "eventType": "Purchase",
  "journeyLocation": "WhatsAppThread",
  "occurredAtUtc": "2026-08-18T18:10:00Z",
  "value": 950.0,
  "currency": "EGP",
  "customer": { "externalId": "cus_7" },
  "conversationId": "uuid-or-null",
  "attribution": { "ctwaClid": null, "adExternalId": null, "sessionId": null },
  "privacy": { "consentState": "Granted", "legalBasis": "Consent" },
  "originalExternalEventId": null,
  "metadata": {}
}
```

Supported semantic outcomes include `ConversationStarted`, `QualifiedLead`, `InitiateCheckout`, `OrderCreated`, `Purchase`, `BookingConfirmed`, `AttendanceConfirmed`, subscription/enrollment states, `OrderDelivered`, `Cancellation`, `Refund`, `Chargeback`, `Absent`, `Churn`, `DealWon` and `DealLost`. Only provider-supported in-thread events are sent through Business Messaging CAPI.

Responses: `202` accepted/new or duplicate-same-payload; `409` duplicate conflicting payload; `401` signature/replay failure; `422` invalid event/business identity/currency/correction; `413` payload too large.

## Error envelope

```json
{
  "code": "ADS_PROVIDER_FIELD_MISMATCH",
  "message": "Meta created the ad set with a different WhatsApp destination.",
  "correlationId": "corr_...",
  "retryable": false,
  "stage": "ReadBack",
  "object": { "type": "AdSet", "localId": "uuid", "providerId": "123" },
  "provider": { "code": "100", "subcode": "...", "traceId": "..." },
  "details": [
    { "field": "promoted_object.whatsapp_phone_number", "planned": "masked", "effective": "different", "reason": "Mismatch" }
  ],
  "nextSafeAction": "Keep paused and reconnect the authorized WhatsApp destination."
}
```

Provider messages are sanitized. The API never echoes tokens, raw phones, emails or raw `ctwa_clid`.
