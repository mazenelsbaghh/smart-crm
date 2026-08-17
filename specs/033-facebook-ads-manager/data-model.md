# Data Model: Autonomous Facebook Ads Manager

All timestamps are UTC. All tenant-owned rows implement `ITenantEntity` and contain a non-null `ProjectId`. Money is `decimal(18,4)` with ISO-4217 currency; provider minor-unit conversion happens only at the Meta adapter boundary. Mutable financial/control aggregates use optimistic concurrency tokens.

## Connections and verified project facts

### `AdvertisingConnection`

- `Id`, `ProjectId`, `Provider` (`Facebook` in V1)
- `AdAccountExternalId`, `PageExternalId`, `DatasetExternalId`
- `ProtectedAccessToken`, `GrantedCapabilitiesJson`, `AccountCurrency`, `AccountTimezone`
- `State`: `PendingSelection | Ready | Degraded | ReconnectRequired | Revoked`
- `ExpiresAtUtc`, `LastValidatedAtUtc`, `LastSyncAtUtc`, `LastErrorCode`, `LastErrorSummary`
- `CreatedByUserId`, `UpdatedAtUtc`, `ConcurrencyToken`

Indexes: unique `(ProjectId, Provider)`; unique active `(Provider, AdAccountExternalId, ProjectId)`. Tokens are Data-Protection ciphertext and never serialized.

### `AdvertisingProfile`

- `Id`, `ProjectId`, `KnowledgeRevisionHash`, `Status`
- `OfferType`, `FunnelJson`, `AudienceJson`, `BrandRulesJson`, `ProhibitedClaimsJson`
- `GeneratedAtUtc`, `StaleAtUtc`, `PromptVersion`, `ModelVersion`

State: `Building -> Ready -> Stale | Blocked`. Only published knowledge projections may contribute facts.

### `AdvertisingFactSource`

- `Id`, `ProjectId`, `ProfileId`, `FactKey`, `NormalizedValueJson`
- `SourceDocumentId`, `SourceVersion`, `Confidence`, `ObservedAtUtc`, `IsContradictory`

Unique `(ProfileId, FactKey, SourceDocumentId, SourceVersion)`.

### `AdvertisingOffer`

- `Id`, `ProjectId`, `ProfileId`, `Name`, `Type`
- `Price`, `Currency`, `DestinationsJson`, `MarketsJson`, `ScheduleJson`
- `AllowedClaimsJson`, `RestrictionsJson`, `State`

State: `Draft | Eligible | Blocked | Archived`. An eligible offer requires sourced destination, market, commercial terms and restrictions.

## Authorization and promotion

### `AutonomyEnvelope`

- `Id`, `ProjectId`, `ConnectionId`, `OfferId`
- `DailyCap`, `PeriodCap`, `PeriodCapKind` (`Total | Monthly`), `Currency`
- `SafetyReservePercent`, `MaximumIncreasePercent`, `CooldownHours`
- `AllowedCountriesJson`, `StartsAtUtc`, `EndsAtUtc`
- `State`: `Draft | Active | Suspended | Revoked | Expired`
- `AuthorizedByUserId`, `AuthorizedAtUtc`, `RevokedAtUtc`, `ConcurrencyToken`

Activation validates currency/resource/offer/readiness. The service transaction enforces one compatible active envelope per project. No action may widen any field implicitly.

### `AdvertisingPromotion`

- `Id`, `ProjectId`, `EnvelopeId`, `OfferId`, `Name`
- `Objective`, `DestinationType`, `DestinationUrl`, `OptimizationEvent`
- `FunnelJson`, `AudiencePlanJson`, `AllocationPlanJson`, `ReadinessJson`
- `State`: `Draft | Ready | Canary | Active | Paused | Completed | Blocked`
- `CreatedByUserId`, `ActivatedAtUtc`, `PausedAtUtc`

## Provider-managed delivery

### `ManagedCampaign`, `ManagedAdSet`, `ManagedAdvertisement`

Shared fields: `Id`, `ProjectId`, `PromotionId`, `ExternalId`, `Name`, `ConfiguredStatus`, `EffectiveStatus`, `ProviderStateHash`, `LastSyncedAtUtc`, `LastProviderError`, `ConcurrencyToken`.

Campaign adds objective/buying type. Ad Set adds targeting JSON, optimization event, bid strategy, placement JSON, daily/lifetime budget and attribution setting. Advertisement adds creative version, source post identity and review status.

Constraints:

- External ID is unique within `(ProjectId, ConnectionId, entity type)`.
- `publisher_platforms` must equal `facebook`; forbidden placement values fail validation.
- System never issues permanent delete. Local archive does not delete provider state.
- New provider structures are created paused, reconciled, then activated through a command.

## Creatives

### `AdvertisingCreative`

- `Id`, `ProjectId`, `OfferId`, `SourceType` (`ExistingPagePost | ProjectAsset`)
- `SourceExternalId`, `SourceAssetId`, `SourceHash`, `SourceVersion`
- `MediaType` (`Image | Carousel | Video`), `RightsState`, `PolicyState`, `EligibilityState`
- `RecommendationScore`, `RecommendationEvidenceJson`, `OrganicEvidenceJson`, `PaidEvidenceJson`
- `FatigueState`, `LastAnalyzedAtUtc`, `ArchivedAtUtc`

Unique source/version per project. Deleted/stale/right-restricted sources become ineligible and cannot create new ads.

### `CreativeVariant`

- `Id`, `ProjectId`, `CreativeId`, `PlacementFormat`, `Width`, `Height`, `DurationMs`
- `Headline`, `PrimaryText`, `Description`, `CallToAction`
- `StorageObjectKey`, `ThumbnailObjectKey`, `ContentHash`, `EligibilityState`
- `OfferFactHash`, `GeneratedAtUtc`

Variants cannot introduce facts absent from the cited offer profile. Storage keys retain project prefix and content hash.

## Budget, insights and attribution

### `BudgetPeriodLedger`

- `Id`, `ProjectId`, `EnvelopeId`, `PeriodKind`, `PeriodStartUtc`, `PeriodEndUtc`
- `AuthorizedCap`, `SafetyReserve`, `UsableCap`, `CommittedAmount`, `ObservedSpend`, `ReleasedAmount`
- `Currency`, `LastReconciledAtUtc`, `ConcurrencyToken`

Unique `(ProjectId, EnvelopeId, PeriodKind, PeriodStartUtc)`. Daily boundaries derive from ad-account timezone but are stored as exact UTC instants.

### `BudgetAllocation`

- `Id`, `ProjectId`, `LedgerId`, `TargetType`, `TargetId`
- `Purpose`: `Winner | CreativeTest | AudienceTest | Retargeting | Canary`
- `AllocatedAmount`, `StartsAtUtc`, `EndsAtUtc`, `State`, `DecisionId`

The transaction must prove sum of active allocations is at most usable cap.

### `InsightsSnapshot`

- `Id`, `ProjectId`, `TargetType`, `TargetId`, `IntervalStartUtc`, `IntervalEndUtc`
- `AttributionSetting`, `BreakdownHash`, `Spend`, `Impressions`, `Reach`, `Clicks`, `Frequency`
- `ProviderActionsJson`, `ProviderActionValuesJson`, `FetchedAtUtc`

Unique `(ProjectId, TargetType, TargetId, IntervalStartUtc, IntervalEndUtc, BreakdownHash)`. Raw short-window snapshots may be compacted after 90 days; daily aggregates are retained with audit links.

### `AttributionTouch`

- `Id`, `ProjectId`, `VisitorId`, `CustomerId`, `ConversationId`
- `Fbclid`, `Fbc`, `Fbp`, `ReferralId`, `SessionId`
- `CampaignId`, `AdSetId`, `AdvertisementId`, `CreativeId`, `OccurredAtUtc`, `ExpiresAtUtc`

Identifiers are protected/minimized according to classification. Index project + customer/visitor + occurred time.

## Conversion intake and delivery

### `WebhookSource`

- `Id`, `ProjectId`, `SourceKey`, `ProtectedSigningSecret`, `AllowedEventTypesJson`, `State`, `LastUsedAtUtc`

Unique `(ProjectId, SourceKey)`. Secret rotation supports current/previous keys for a bounded overlap.

### `ConversionSourceEvent`

- `Id`, `ProjectId`, `SourceSystem`, `ExternalEventId`, `SchemaVersion`
- `PayloadHash`, `SignatureTimestampUtc`, `ReceivedAtUtc`, `ProcessingState`, `FailureCode`

Unique `(ProjectId, SourceSystem, ExternalEventId)`; duplicate payloads return the original accepted identity, conflicting payloads create an incident.

### `CanonicalConversion`

- `Id`, `ProjectId`, `CanonicalKey`, `EventType`, `OccurredAtUtc`
- `CustomerReference`, `VisitorReference`, `Value`, `Currency`, `SourceStrength`
- `CampaignId`, `AdSetId`, `AdvertisementId`, `CreativeId`, `AttributionMethod`
- `ConsentState`, `LegalBasis`, `ProtectedMatchData`, `CurrentValue`, `State`

Unique `(ProjectId, CanonicalKey)`. States: `Observed -> Verified -> Attributed -> Delivered`; alternates `Unattributed`, `Suppressed`, `Corrected`, `DeliveryFailed`. Raw email/phone is never logged; protected matching data is omitted unless allowed.

### `ConversionAdjustment`

- `Id`, `ProjectId`, `ConversionId`, `ExternalEventId`, `Kind`
- `ValueDelta`, `Reason`, `OccurredAtUtc`

Kinds include `Refund`, `Chargeback`, `Cancellation`, `Absence`, `Churn`, `LostDeal`. Unique source adjustment identity prevents double reversal.

### `MetaConversionDelivery`

- `Id`, `ProjectId`, `ConversionId`, `MetaEventId`, `MetaEventName`
- `PayloadHash`, `AttemptCount`, `NextAttemptAtUtc`, `State`, `ProviderRequestId`, `LastError`

Unique `(ProjectId, MetaEventId, MetaEventName)`. Unknown results reconcile before resend.

## Decisions, commands and incidents

### `AdvertisingDecision`

- `Id`, `ProjectId`, `PromotionId`, `ActionType`, `TargetType`, `TargetId`
- `EvidenceStartUtc`, `EvidenceEndUtc`, `EvidenceJson`, `ProposedChangeJson`
- `RiskClass`, `StrategistResultJson`, `PromptVersion`, `ModelVersion`
- `State`: `Proposed | Reviewing | Waiting | Rejected | Approved | Executing | Executed | Failed | Superseded`
- `EvaluateAfterUtc`, `CreatedAtUtc`

### `DecisionReview`

- `Id`, `ProjectId`, `DecisionId`, `ReviewerType` (`Statistical | Auditor | Judge | Safety`)
- `Verdict` (`Approve | Reject | Wait | Escalate`), `ReasonsJson`, `EvidenceHash`, `CreatedAtUtc`

Unique reviewer type/version per decision. Safety is always final and fail-closed.

### `ExecutionCommand`

- `Id`, `ProjectId`, `DecisionId`, `IdempotencyKey`, `CommandType`, `TargetExternalId`
- `ExpectedStateHash`, `DesiredStateJson`, `RequestFingerprint`
- `State`: `Pending | Claimed | Sent | Succeeded | Failed | Unknown | Reconciling | Stale | Blocked | Cancelled`
- `LeaseOwner`, `LeaseExpiresAtUtc`, `AttemptCount`, `ProviderRequestId`, `LastError`, `ConcurrencyToken`

Unique `(ProjectId, IdempotencyKey)`. `Unknown` can transition only through reconciliation, never directly to blind resend.

### `DecisionImpact`

- `Id`, `ProjectId`, `DecisionId`, `BaselineJson`, `OutcomeJson`, `Verdict`, `EvaluatedAtUtc`

Verdict: `Positive | Negative | Inconclusive | Reverted`.

### `TrackingIncident` and `EmergencyStopRecord`

Incident stores category, severity, source, detected/recovered timestamps, evidence and state. Emergency record stores trigger (`Manual | AbnormalSpend | CapRisk | CrossProjectGuard | Provider`), actor, reason, affected managed entity IDs and resume actor/time. Active Emergency Stop blocks commands and decisions.

### `RecurringCycleRun`

- `Id`, `ProjectId`, `JobType`, `TimeBucket`, `State`, `StartedAtUtc`, `CompletedAtUtc`, `SummaryJson`

Unique `(ProjectId, JobType, TimeBucket)` gives durable cycle deduplication in addition to Redis leases.

## Reliable integration messaging

### `IntegrationOutboxMessage`

- `Id`, `ProjectId`, `EventType`, `SchemaVersion`, `AggregateType`, `AggregateId`
- `PayloadJson`, `OccurredAtUtc`, `PublishedAtUtc`, `AttemptCount`, `NextAttemptAtUtc`, `State`

Written in the same transaction as source-module changes. Publisher uses bounded retries and dead-letter/incident state.

### `IntegrationInboxReceipt`

- `Id`, `ProjectId`, `Consumer`, `EventId`, `ReceivedAtUtc`, `ProcessedAtUtc`, `State`, `FailureCode`

Unique `(Consumer, EventId)`. Consumer processing and projection changes commit atomically.

## Project archival/deletion

Archive/revocation immediately suspends the envelope and queues pauses for system-managed delivery. Credentials are revoked/protected, pending commands blocked and PII follows project retention policy. Financial/audit ledgers remain immutable for the legally configured retention window, isolated by ProjectId; permanent provider campaign deletion is never automated.
