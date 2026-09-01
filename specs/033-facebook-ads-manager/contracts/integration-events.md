# Integration Event Contracts

All events use the shared transactional outbox/inbox envelope. The source module writes the event in the same database transaction as the source aggregate change. Delivery is at least once; each consumer inserts `(Consumer, EventId)` and its projection/update atomically.

```json
{
  "eventId": "uuid",
  "eventType": "WhatsAppAttributionObserved.v1",
  "schemaVersion": 1,
  "projectId": "uuid",
  "occurredAtUtc": "2026-08-18T19:00:00Z",
  "source": { "aggregateType": "Conversation", "aggregateId": "uuid", "version": 3 },
  "data": {}
}
```

## `WhatsAppAttributionObserved.v1`

Publisher: Conversations, after an authenticated inbound message is deduplicated and conversation/customer identity is resolved. It publishes for the first inbound message of every conversation regardless of identifier state (the health denominator), and for every later message carrying a referral, CTWA marker or opaque conversion payload (additional touches/diagnostics). Ordinary later messages with no attribution marker do not create redundant observations.

```json
{
  "conversationId": "uuid",
  "customerId": "uuid",
  "journeyKey": "protected-stable-customer-reference",
  "messageExternalId": "wamid-or-baileys-id",
  "messageOccurredAtUtc": "2026-08-18T18:59:58Z",
  "destination": {
    "destinationId": "uuid",
    "destinationVersion": 4,
    "receivingIdentityExternalId": "phone-number-id-or-gateway-account-id",
    "integrationMode": "CloudApiCoexistence"
  },
  "referral": {
    "identifierState": "CtwaClid",
    "protectedCtwaClid": "optional-ciphertext",
    "protectionPurpose": "Advertising.BusinessMessaging.CtwaClid.v1",
    "ctwaClidHash": "optional-sha256",
    "opaquePayloadHash": "optional-sha256-without-parsing",
    "providerAdExternalId": "optional",
    "sourceId": "optional",
    "sourceType": "optional",
    "payloadHash": "sha256"
  },
  "gateway": { "type": "CloudApi|CloudApiCoexistence|Baileys", "schemaVersion": 1 }
}
```

Rules:

- `identifierState` is `CtwaClid | OpaquePayloadOnly | Missing | Invalid`. Only `CtwaClid` creates a touch.
- Cloud API/coexistence reads the documented Messages-webhook `referral.ctwa_clid`. Baileys may read `contextInfo.externalAdReply.ctwaClid` only when a real configured-account fixture proves it is populated; the protobuf field's existence alone is not readiness evidence.
- Undocumented `ctwaPayload`/`conversionData` is never decrypted, reverse-engineered or treated as a click ID; only a hash may be retained for diagnostics.
- The gateway-to-backend webhook may carry raw `ctwaClid` over the protected internal network; Conversations protects it for the Advertising-specific purpose before persistence/outbox.
- Logs, SignalR payloads and ordinary conversation/message APIs never contain raw referral data.
- Duplicate message/referral identity is idempotent.
- Missing/opaque referral produces an observation but never creates inferred attribution. It contributes to missing-referral health and can force `Unsafe/WAIT`.

## `WhatsAppInboundMessageReceived.v1`

Publisher: WhatsApp module, only after the public Cloud API/coexistence webhook raw body passes `X-Hub-Signature-256`, the provider message ID is deduplicated, and `phone_number_id` resolves server-side to exactly one active project/destination version.

The event contains resolved `projectId`, `destinationId`/version, provider message ID/time, protected sender reference, normalized message content/media reference and a protected/minimized referral envelope. It does not accept project/destination authority from webhook JSON. Unknown or ambiguous phone/WABA routing is quarantined as a security incident and no Conversation event is published.

Conversations consumes this event with inbox idempotency, resolves/creates the customer/conversation, stores the message, and writes `WhatsAppAttributionObserved.v1` to its outbox in the same transaction. Webhook acknowledgement occurs after the WhatsApp module durably records its outbox item, not after downstream AI work.

## Sourced knowledge and media

- `KnowledgePublishedChanged.v2`: document/revision/hash/status plus safe fact projection and affected offer keys; stales plan/profile versions.
- `ProjectAssetChanged.v2`: asset ID/hash/media type/object reference/rights/availability/brand metadata; no direct Media table read.
- `ProjectAiConfigurationChanged.v1`: allowed model/settings revision without exposing the key; stales AI prompt execution configuration where applicable.
- `ProjectAdvertisingContextChanged.v1`: project lifecycle plus validated IANA reporting timezone/version; Advertising updates its local projection.

## AI work without credential leakage

- `AdvertisingAiWorkRequested.v1`: Advertising request ID, project ID, purpose (`Profile | Creative | Strategist | Auditor | Judge`), prompt/input version, a size-limited sourced input snapshot or Shared object-storage references, bounded server-owned candidate IDs and content hash; never an API key or an unrestricted provider identifier.
- `AdvertisingAiWorkCompleted.v1`: request ID, model/prompt version, validated structured result or safe failure code/token usage; never credentials or raw provider secrets.

The AI module consumes requests, resolves the current encrypted project Gemini credential inside its own boundary, calls Gemini 3.5 Flash, and publishes the result. Advertising persists a pending work item and resumes its state machine when the completion arrives; it never reads Project Settings or calls an AI module internal service.

## Business outcomes consumed by Advertising

Every business event includes `businessAggregateType`, `businessAggregateId`, `customerId`, optional `conversationId`, source version and an explicit occurred time.

- `DealOutcomeChanged.v2`: qualified/won/lost state, amount/currency, closed timestamp and source evidence.
- `BookingChanged.v2`: created/confirmed/cancelled state, capacity and timestamp.
- `BookingPaymentChanged.v2`: paid/refunded/chargeback state, amount/currency, payment/order identity and timestamp.
- `BookingAttendanceChanged.v2`: confirmed/attended/absent state and timestamp.
- `ConversationSalesClassificationChanged.v2`: closed taxonomy (`Spam | Support | Unqualified | QualifiedLead | BookingIntent | PurchaseIntent | ConfirmedPayment`), confidence, evidence message range, classifier version and timestamp.
- `CustomerAdvertisingConsentChanged.v1`: customer reference, consent/legal-basis state/version and effective timestamp; retries re-evaluate it before any match-data or CAPI delivery.
- `ProjectLifecycleChanged.v1`: archived/deleted/restored; archive/delete suspends automation.

Advertising accepts old v1 events during the compatibility migration but cannot assign ad attribution without a conversation/referral or explicit verified external ad identity. It records such outcomes as unattributed.

## Advertising events published

- `AdvertisingPlanStateChanged.v1`
- `AdvertisingWhatsAppDestinationChanged.v1`: validated local destination ID/version plus provider WABA/phone routing identity and revoked tombstone; WhatsApp updates its inbound route projection monotonically.
- `AdvertisingManagedDeliveryChanged.v2`
- `AdvertisingExperimentStateChanged.v1`
- `AdvertisingDecisionExecuted.v2`
- `AdvertisingTrackingHealthChanged.v2`
- `AdvertisingTrackingIncidentChanged.v2`
- `AdvertisingEmergencyStopActivated.v2`
- `AdvertisingAuditRecorded.v1`

Published advertising events contain local project-scoped IDs, state, reason codes and correlation IDs. They never expose tokens, provider payloads, raw match data or raw `ctwaClid`.

`AdvertisingAuditRecorded.v1` is written from the same PostgreSQL transaction as the authoritative audit row. The shared audit indexer consumes it into Elasticsearch with bounded retry/dead-letter alerting. Index failure never rolls back or deletes the PostgreSQL audit record; search shows index freshness/degraded state.

## Delivery and poison-message behavior

- Publisher retries are bounded and move to dead-letter/incident state after the configured threshold.
- Consumer validation failure records a safe inbox failure code; it never infinitely requeues.
- Breaking schema changes require a new event name/version.
- For versioned aggregates, a consumer applies only the next/newer monotonic source version, ignores an already-applied older version and records a gap when versions are missing. A gap is repaired by replay or an explicit versioned snapshot event; it never triggers a synchronous read of the publisher's tables.
- Tombstone/lifecycle events carry their source aggregate version and supersede older facts. A late older active event cannot resurrect a revoked destination, consent, asset, offer or project.
- Project mismatch, missing tenant ID or foreign aggregate identity is a security incident and no projection is written.
- Consumer replay produces the existing projection result and no duplicate canonical outcome/touch.
