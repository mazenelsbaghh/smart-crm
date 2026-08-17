# HTTP API Contract

Base authenticated namespace: `/api/projects/{projectId}/ad-manager`. Every endpoint requires JWT authentication and a server-side project/role check; `X-Project-Id` is correlation context, not authority. Mutation requests require `Idempotency-Key`; updates require an ETag/concurrency token.

## Connection and readiness

| Method | Path | Role | Result |
|---|---|---|---|
| POST | `/facebook/oauth/start` | Owner/Admin | Opaque authorize URL/state handle |
| GET | `/facebook/oauth/callback` | OAuth callback | Stores token server-side; redirects with opaque result ID only |
| GET | `/facebook/resources` | Owner/Admin | Eligible ad accounts, Pages and Datasets without secrets |
| PUT | `/connection` | Owner/Admin | Select and validate account/Page/Dataset |
| POST | `/connection/test` | Owner/Admin | Permission/resource/measurement health |
| DELETE | `/connection` | Owner/Admin | Revoke local authority; never delete provider campaigns |
| GET | `/readiness` | Project member | Typed checklist and blocking reasons |

## Profile, creatives and launch

- `GET /profile`, `POST /profile/refresh`, `GET /offers`
- `GET /creative-sources?type=&cursor=`, `POST /creatives/analyze`
- `POST /launch-plans`, `GET /launch-plans/{id}`, `POST /launch-plans/{id}/activate`

Activation body includes offer, destination, envelope version and selected/recommended sources. The response is `202 Accepted` with an operation/decision ID; provider creation is asynchronous and begins paused.

## Financial control

- `GET /envelope`, `PUT /envelope`
- `POST /autopilot/enable`, `POST /autopilot/disable`
- `POST /emergency-stop`, `POST /emergency-stop/resume`

Only Owner/Admin may mutate these resources. Disable response explicitly states whether owned ads remain at their last safe provider state. Emergency Stop always queues pauses and blocks pending commands.

## Operations

- `GET /overview?from=&to=`
- `GET /campaigns?status=&cursor=&limit=` and `GET /campaigns/{id}`
- `GET /creatives?eligibility=&fatigue=&cursor=&limit=`
- `GET /conversions?type=&state=&cursor=&limit=`
- `GET /decisions?verdict=&state=&cursor=&limit=` and `GET /decisions/{id}`
- `GET /incidents?state=&cursor=&limit=`

Responses include `asOfUtc`, connection/tracking freshness, first-party outcome fields and provider-reported fields separately. Pagination uses opaque cursor, stable descending time/id order and maximum 100 rows.

## Generic conversion webhook

`POST /api/integrations/ad-manager/{projectId}/conversions/{sourceKey}` is not JWT-authenticated. It requires:

- `X-Ads-Timestamp`: Unix seconds within five-minute replay window.
- `X-Ads-Signature`: `v1=` plus HMAC-SHA256 of `timestamp + "." + rawBody`.
- `Idempotency-Key`: source delivery identity.

Minimal payload:

```json
{
  "schemaVersion": 1,
  "externalEventId": "pay_123",
  "eventType": "Purchase",
  "occurredAtUtc": "2026-08-17T11:10:00Z",
  "value": 950.0,
  "currency": "EGP",
  "customer": { "externalId": "cus_7" },
  "attribution": { "fbclid": "...", "sessionId": "..." },
  "privacy": { "consentState": "Granted", "legalBasis": "Consent" },
  "originalExternalEventId": null,
  "metadata": {}
}
```

Supported V1 event types: `Lead`, `QualifiedLead`, `Signup`, `TrialStarted`, `SubscriptionStarted`, `SubscriptionRenewed`, `EnrollmentPaid`, `BookingConfirmed`, `AttendanceConfirmed`, `Purchase`, `Refund`, `Cancellation`, `Chargeback`, `Absent`, `Churn`, `DealWon`, `DealLost`.

Responses: `202` accepted/new or duplicate-same-payload; `409` duplicate ID with conflicting payload; `401` signature/replay failure; `422` invalid event/currency/correction link; `413` payload too large.

## Error envelope

```json
{
  "code": "ADS_TRACKING_UNHEALTHY",
  "message": "Financial changes are frozen until tracking recovers.",
  "correlationId": "...",
  "retryable": false,
  "details": [{ "field": "dataset", "reason": "No successful test event" }]
}
```

No response or log may contain access tokens, webhook secrets, raw phone/email match data or cross-project identifiers.
