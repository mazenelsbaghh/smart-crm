# Meta Provider and Reconciliation Contract

The domain never constructs arbitrary Meta form fields. `CampaignPlan` is mapped to versioned provider DTOs by `MetaCampaignPlanMapper`; `MetaAdsClient` only transports typed requests and parses typed responses.

## Capability discovery

`DiscoverCapabilities(connection, destination)` returns:

```json
{
  "graphApiVersion": "v26.0",
  "checkedAtUtc": "2026-08-18T19:00:00Z",
  "permissions": {},
  "account": { "status": "Active", "currency": "EGP", "timezone": "Africa/Cairo" },
  "destination": { "pageId": "...", "wabaId": "...", "phoneNumberId": "...", "datasetId": "...", "eligible": true },
  "combinations": [
    { "objective": "OUTCOME_ENGAGEMENT", "destination": "WHATSAPP", "optimizationGoal": "CONVERSATIONS", "eligible": true }
  ],
  "bidStrategies": ["LOWEST_COST_WITHOUT_CAP"],
  "placementMode": { "advantagePlus": true, "providerValidated": true },
  "businessMessagingEvents": { "enabled": true, "purchaseOptimization": "RuntimeEligible" },
  "expiresAtUtc": "2026-08-18T19:15:00Z"
}
```

Static enums never set `eligible=true`. Only a live validation/discovery result may do so.

The adapter records the exact request/fields, Graph version, response hash, provider trace and expiry for each probe:

- `/me/permissions` for granted access;
- `/me/adaccounts` and the selected ad-account fields for account status, currency, timezone and business identity;
- `/me/accounts` for Pages/tasks available to the user;
- the selected business WABA edge and `/{waba-id}/phone_numbers` for WABA/phone membership;
- `GET|POST /{waba-id}/dataset` for the WABA-associated Dataset where Business Messaging is enabled;
- the exact compiled campaign/ad-set/creative/ad create shapes with `execution_options=["validate_only"]` only for object types the pinned version accepts;
- paused creation plus typed read-back for fields that cannot be proven by discovery or validation-only.

There is no claim that Meta exposes one complete capability-matrix endpoint. `combinations[].eligible=true` is derived from successful exact-shape validation plus fresh resource/permission probes; unsupported validation remains `Unknown` until a paused read-back proves the invariants.

## Normalized plan request

Critical provider fields:

- Campaign: objective, buying type, special-ad categories, campaign budget/bid strategy when used, paused state.
- Ad set: campaign parent, messaging optimization goal, billing event, WhatsApp destination, promoted Page/phone identity, hard audience controls, Advantage+ placement/audience mode, schedule, attribution setting, budget owner and paused state.
- Creative: Page identity, existing-story or image/video content, WhatsApp CTA and the same phone identity.
- Ad: ad-set parent, creative parent and paused state.

No mapper path may omit destination or targeting. Existing-post, image, carousel, video, clone and replacement requests share the same campaign/ad-set mapping and only vary at the creative layer.

## Preflight validation

1. Validate domain invariants locally.
2. Call provider validation-only for each supported object/request shape.
3. Persist every warning/error as `ProviderValidationFinding`.
4. Treat an unsupported validation endpoint as a capability fact, not success; creation still requires paused read-back.
5. Block on unsupported objective/goal/destination, special category, invalid targeting, missing WABA/phone/Dataset, currency/budget conflict or non-WhatsApp CTA.

## Paused creation saga

| Step | Depends on | Success proof |
|---|---|---|
| Create campaign | Validated plan | ID plus read-back objective/category/budget/status match |
| Create ad set | Verified campaign | Parent, goal, WhatsApp promoted object, targeting, placements, schedule, budget and paused status match |
| Create creative | Verified ad set | Page, source/media, CTA and WhatsApp identity match |
| Create ad | Verified ad set and creative | Parent links and paused status match |
| Verify hierarchy | All read-backs | No unresolved blocking finding; all critical fields are equivalent |

Each step has its own durable idempotency key and request fingerprint. A timeout becomes `Unknown`, then `Reconcile`. Search/read-back uses known IDs and deterministic names/fingerprints only inside the selected ad account. No unknown mutation is resent until absence is proven.

## Normalized read-back and drift

Provider JSON is normalized before hashing, but activation uses field-level equivalence rather than requiring full snapshot-hash equality:

- ordered arrays and canonical casing;
- provider minor units converted to account currency decimals;
- omitted provider defaults expanded only when documented/observed;
- secrets and irrelevant timestamps removed;
- dynamic delivery observations kept separate from configured placement mode.

For Advantage+ placements the proof is that no stale manual publisher/position restriction was sent, the configured automatic mode was accepted, `destination_type=WHATSAPP`, `promoted_object.page_id` and `promoted_object.whatsapp_phone_number` match, the creative CTA is `WHATSAPP_MESSAGE` with `app_destination=WHATSAPP`, returned targeting identifies a WhatsApp-destination ad when the pinned API exposes that field, and no provider-effective field widens a hard control. Configured/effective publisher-platform and position arrays returned by Meta are preserved with provider trace and expiry as evidence, but resolved defaults are not converted into a permanent local allowlist. Actual inventory delivery is stored later as insights, not compared to a planned placement list.

Diff severity:

- `Blocking`: destination, parent, objective/goal, budget owner/amount, hard audience control, special category, Page/phone, creative/CTA or active-before-approval mismatch.
- `Warning`: provider-normalized optional field that does not widen spend/destination or violate the plan.
- `Info`: review/effective delivery observation.

Any blocking diff keeps the hierarchy paused and sets `Drifted` or `Partial`.

## Activation and mutations

Activation rechecks immediately:

- project and role-derived envelope authority;
- exact plan/envelope/capability versions;
- current connection, WABA/Dataset and tracking health;
- complete `VerifiedPaused` hierarchy;
- current provider review/effective status;
- budget ledger reservation, reserve, cooldown and Emergency Stop;
- WhatsApp destination parity across plan, ad set and creative.
- an active `AutopilotCreated` or `ImportedWithAuthority` ownership record.

Budget/status changes use the same operation/read-back contract. A provider state-hash mismatch returns `Stale` and starts reconciliation; it never applies the old command blindly.

## Business Messaging CAPI

Eligible WhatsApp-thread events map to a Dataset/WABA payload containing:

```json
{
  "data": [
    {
      "event_name": "Purchase",
      "event_time": 1787080200,
      "event_id": "stable-canonical-event-id",
      "action_source": "business_messaging",
      "messaging_channel": "whatsapp",
      "user_data": {
        "whatsapp_business_account_id": "waba_1",
        "ctwa_clid": "unprotected-only-at-send-boundary"
      },
      "custom_data": { "currency": "EGP", "value": 950.0 }
    }
  ]
}
```

The adapter sends `POST /{dataset-id}/events` on the pinned Graph version. A readiness/test request may add top-level `test_event_code`; production never reuses it. The response contract records HTTP status, provider request/trace ID, `events_received`/accepted count, messages/warnings and retry classification. Exact field nesting is covered by adapter contract tests against the official current Business Messaging example.

Delivery is suppressed when the event did not occur in the WhatsApp thread, has no eligible `ctwa_clid`, lacks a supported Cloud API/coexistence referral proof, WABA/Dataset capability or violates current privacy state. It remains visible internally. The official event allowlist is versioned; internal events without a provider mapping remain internal rather than being relabeled.

Pinned semantic mappings are exact:

| Internal semantic fact | Meta event | Must occur in WhatsApp thread | `ctwa_clid` | Value/currency |
|---|---|---:|---:|---|
| Qualified sales lead | `QualifiedLead` | Yes | Required | Omit unless current contract supports it |
| Checkout intent | `InitiateCheckout` | Yes | Required | Optional when sourced |
| Order created | `OrderCreated` | Yes | Required | Optional when sourced |
| Verified paid purchase | `Purchase` | Yes | Required | Required when value is known; ISO currency |
| Order cancelled | `OrderCanceled` | Yes | Required | Follow pinned contract |
| Actual returned order | `OrderReturned` | Yes | Required | Follow pinned contract |
| Order delivered | `OrderDelivered` | Yes | Required | Follow pinned contract |
| Conversation start, booking, refund or unsupported internal fact | `InternalOnly` | N/A | N/A | Never relabel |

The mapper table is versioned with Graph v26.0 contract fixtures. A weaker/adjacent state is never upgraded merely to obtain a provider event name.

## Error classification

- `Auth`: reconnect, no automatic retry.
- `PermissionOrAccess`: App Review/business access guidance, no tight retry loop.
- `Validation`: field-level blocked plan.
- `RateLimit`: bounded provider-directed backoff.
- `Transient`: bounded retry with jitter when mutation absence/safety is known.
- `UnknownMutation`: reconcile only.
- `PolicyOrReview`: keep paused, surface provider reason.
- `BillingOrAccount`: freeze financial mutation.
- `Drift`: reconcile and create a new decision.

## Fake provider scenarios

Development/Test fake supports scenario headers or injected fixtures for:

- eligible and ineligible objective/goal pairs;
- dynamic placement capability;
- missing WABA/phone/Dataset permission;
- validation-only warning/error;
- partial hierarchy creation;
- timeout after provider success;
- changed read-back targeting or destination;
- rejected/pending review;
- delayed insights/spend;
- Business Messaging accepted, warning, retryable and terminal failure.

The fake records normalized requests for assertions and is impossible to enable outside Development/Test.
