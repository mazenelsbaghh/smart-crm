# Integration Event Contracts

All events use a shared envelope and are written to the transactional outbox with the source aggregate change.

```json
{
  "eventId": "uuid",
  "eventType": "DealOutcomeChanged.v1",
  "schemaVersion": 1,
  "projectId": "uuid",
  "occurredAtUtc": "2026-08-17T12:00:00Z",
  "source": { "aggregateType": "Deal", "aggregateId": "uuid", "version": 8 },
  "data": {}
}
```

V1 events consumed by Advertising:

- `KnowledgePublishedChanged.v1`: document/version/hash/status and a safe published-facts projection; marks affected profile stale.
- `ProjectAssetChanged.v1`: asset ID/hash/media type/object reference/rights/availability; no direct Media table read.
- `DealOutcomeChanged.v1`: deal/customer, Won/Lost, amount/currency, closed timestamp and attribution references.
- `BookingChanged.v1`: booking/customer, created/confirmed/cancelled status and timestamp.
- `BookingPaymentChanged.v1`: paid/refunded state, amount/currency, external payment ID and timestamp.
- `BookingAttendanceChanged.v1`: confirmed/attended/absent state and timestamp.
- `ConversationSalesClassificationChanged.v1`: conversation/customer, closed taxonomy, confidence, supporting message range and timestamp.
- `ProjectLifecycleChanged.v1`: archived/deleted/restored state; archive/delete suspends automation.

Advertising publishes:

- `AdvertisingTrackingIncidentChanged.v1`
- `AdvertisingDecisionExecuted.v1`
- `AdvertisingEmergencyStopActivated.v1`
- `AdvertisingManagedDeliveryChanged.v1`

Delivery is at least once. Consumers insert `(Consumer, EventId)` inbox receipt and projection updates in one transaction. Schema additions are backward-compatible; breaking changes require a new event name/version. Poison messages use bounded retry, then dead-letter plus an incident—never infinite requeue.
