# Data Model: Autonomous WhatsApp AI Media Buyer

All persisted timestamps are UTC. User-facing day boundaries use the selected ad-account/project timezone and are stored as exact UTC ranges. All tenant-owned rows implement `ITenantEntity`, contain non-null `ProjectId`, and are queried with both tenant filters and explicit project predicates in background work. Money uses `decimal(18,4)` plus ISO-4217 currency. Provider minor units exist only at the Meta adapter boundary.

Mutable financial/control aggregates use real optimistic-concurrency tokens. JSON columns store versioned provider/domain snapshots, not unvalidated request blobs. Protected secrets, customer match data and `ctwa_clid` are never logged or returned raw.

## 1. Connection, destinations and live capabilities

### `AdvertisingConnection`

- `Id`, `ProjectId`, `Provider` (`Meta`)
- `AdAccountExternalId`, `PageExternalId`, `DatasetExternalId`, `WabaExternalId`
- `ProtectedAccessToken`, `GrantedPermissionsJson`
- `AccountCurrency`, `AccountTimezoneIana`, `TimezoneSource`, `TimezoneValidatedAtUtc`
- `AccountStatus`, `FundingStatus`
- `GraphApiVersion`
- `WhatsAppIntegrationMode`: `CloudApi | CloudApiCoexistence | BaileysObservedExperimental`
- `ReferralProofState`: `Unverified | CtwaClidObserved | Missing | Unsupported`
- `ReferralProofAtUtc`, `ReferralProofHash`
- `State`: `PendingSelection | Ready | Degraded | ReconnectRequired | Disconnecting | Revoked`
- `ExpiresAtUtc`, `LastValidatedAtUtc`, `LastSyncAtUtc`
- `LastErrorCode`, `LastErrorSummary`, `LastProviderTraceId`
- `CreatedByUserId`, `UpdatedAtUtc`, `ConcurrencyToken`

Constraints/indexes:

- Unique active `(ProjectId, Provider)`.
- External ad account cannot be shared across projects unless an explicit platform rule is later introduced; current unique key is `(Provider, AdAccountExternalId)` for non-revoked rows.
- Graph version must be in the code-owned supported range.
- Protected token is ciphertext and never serialized.

### `AuthorizedWhatsAppDestination`

- `Id`, `ProjectId`, `ConnectionId`
- `Provider` (`MetaWhatsApp`)
- `WabaExternalId`, `PhoneNumberExternalId`, `DisplayPhoneE164`, `PageExternalId`
- `DatasetExternalId`
- `ReceivingIdentityExternalId`, `WhatsAppIntegrationMode`
- `MessagingState`, `AdvertisingState`, `BusinessEventsState`
- `ReferralCaptureState`, `ReferralProofAtUtc`
- `CapabilitySnapshotId`, `LastValidatedAtUtc`, `LastErrorCode`
- `State`: `Pending | Eligible | Degraded | Ineligible | Revoking | Revoked`
- `ConcurrencyToken`

Unique `(ProjectId, ConnectionId, PhoneNumberExternalId)` plus a filtered global unique active `(Provider, WabaExternalId, PhoneNumberExternalId)` across projects. Ambiguity fails connection activation rather than guessing a tenant. An eligible row requires mutually validated Page, WABA, phone and Dataset relationships. Business Messaging CAPI readiness additionally requires a supported WhatsApp Business Platform mode and an observed official webhook referral fixture. A Baileys-only observation may support explicitly labeled internal attribution, but never proves Cloud API/coexistence eligibility. Display phone is normalized E.164; it is not used as the provider identity when a phone-number ID exists.

### `WhatsAppInboundRouteProjection` (WhatsApp-owned)

- `Id`, `ProjectId`, `DestinationId`, `DestinationVersion`
- `Provider`, `WabaExternalId`, `PhoneNumberExternalId`, `IntegrationMode`
- `SourceEventId`, `SourceAggregateVersion`, `State`, `UpdatedAtUtc`

Filtered global unique active `(Provider, WabaExternalId, PhoneNumberExternalId)`. It is populated from versioned destination events, not by reading Advertising tables. The public webhook resolves routing only through this projection.

### `AdvertisingCapabilitySnapshot`

- `Id`, `ProjectId`, `ConnectionId`, `DestinationId`
- `GraphApiVersion`, `ProviderAccountStatus`, `PermissionStateJson`
- `ObjectivesJson`, `OptimizationGoalsJson`, `BidStrategiesJson`
- `PlacementEligibilityJson`, `AutomationFeaturesJson`
- `ValidationSupportJson`, `ProductionAccessJson`
- `ProbeEvidenceJson`, `SupportedValidationObjectsJson`, `ProviderFieldsVersion`
- `PayloadHash`, `CheckedAtUtc`, `ExpiresAtUtc`
- `State`: `Healthy | Partial | Unsupported | Failed`
- `ProviderTraceId`, `FailureCode`, `FailureSummary`

Snapshots are append-only. Plans and commands reference the exact snapshot used. Expired snapshots cannot authorize activation.

### `ConnectionDisconnectOperation`

- `Id`, `ProjectId`, `ConnectionId`, optional `DestinationId`
- `Mode`: `PauseManaged | LeaveRunning | ForceRevoke`
- `Phase`: `Requested | AuthoritySuspended | ProtectiveStopQueued | ReconcilingPauses | DisposingCredential | PublishingRouteTombstone | Completed | ManualActionRequired | Failed`
- `RequestedByUserId`, `RequestedAtUtc`, `ContinuingOrUnmonitoredSpendAcknowledgedAtUtc`
- `EmergencyStopRecordId`, `CredentialDisposedAtUtc`, `RouteTombstoneVersion`, `CompletedAtUtc`
- `LastErrorCode`, `RecoveryInstruction`, `ConcurrencyToken`

### `ConnectionDisconnectTarget`

- `Id`, `ProjectId`, `DisconnectOperationId`, `OwnershipRecordId`
- `TargetType`, `TargetId`, `ProviderExternalId`, `DesiredState`
- `ProviderOperationId`, `ReadBackState`, `CompletedAtUtc`, `FailureCode`

Unique `(DisconnectOperationId, TargetType, TargetId)`. The operation and targets are durable/resumable. `PauseManaged` cannot enter credential disposal until every required target is read back paused or the operation enters `ManualActionRequired`. `LeaveRunning` retains monitoring authority; `ForceRevoke` may dispose immediately but can complete only as `ManualActionRequired` while delivery is unverified.

## 2. Sourced offers and bounded authority

### `AdvertisingProfile`

- `Id`, `ProjectId`, `KnowledgeRevisionHash`, `Status`
- `OfferType`, `FunnelJson`, `AudienceFactsJson`, `BrandRulesJson`, `ProhibitedClaimsJson`
- `GeneratedAtUtc`, `StaleAtUtc`, `PromptVersion`, `ModelVersion`

State: `Building -> Ready -> Stale | Blocked`. Only published knowledge projection facts may contribute.

### `AdvertisingFactSource`

- `Id`, `ProjectId`, `ProfileId`, `FactKey`, `NormalizedValueJson`
- `SourceDocumentId`, `SourceVersion`, `Confidence`, `ObservedAtUtc`
- `IsContradictory`, `IsRequiredForLaunch`

Unique `(ProfileId, FactKey, SourceDocumentId, SourceVersion)`.

### `AdvertisingOffer`

- `Id`, `ProjectId`, `ProfileId`, `Name`, `Type`
- `Price`, `Currency`, `UnitCost`, `ContributionMargin`, `MaximumSustainableCost`
- `PrimaryOutcome`, `FallbackOutcomeOrderJson`, `AttributionWindowDays`
- `DailyCapacity`, `CurrentCapacity`, `CapacityUpdatedAtUtc`
- `MarketsJson`, `ScheduleJson`, `AllowedClaimsJson`, `RestrictionsJson`
- `SpecialAdCategory`, `PolicyEvidenceJson`, `PolicyState`
- `State`: `Draft | Eligible | Blocked | Archived`

Economics may be null when unknown, but the AI may not invent them. `MaximumSustainableCost` must be positive and currency-compatible when present. Eligible requires sourced destination-independent commercial facts, markets, capacity policy and claim restrictions.

### `AutonomyEnvelope`

- `Id`, `ProjectId`, `ConnectionId`
- `DailyCap`, `PeriodCap`, `PeriodCapKind` (`Total | Monthly`), `Currency`
- `SafetyReservePercent`, `MaximumIncreasePercent`, `CooldownHours`
- `StartsAtUtc`, `EndsAtUtc`, `AttributionWindowDays`
- `ReportingTimezoneIana`, `TimezoneSource`, `TimezoneSnapshotAtUtc`
- `PlacementPolicy`: `DynamicEligibleMeta`
- `HardIncludedGeoJson`, `HardExcludedGeoJson`, `HardMinimumAge`
- `HardRequiredLanguagesJson`, `HardCustomAudienceExclusionsJson`
- `AudienceBoundaryHash`
- `State`: `Draft | Active | Suspended | Revoked | Expired`
- `AuthorizedByUserId`, `AuthorizedAtUtc`, `RevokedAtUtc`
- `Version`, `DefinitionHash`, `ConcurrencyToken`

Only one compatible active envelope exists per project/connection/currency. The IANA timezone is validated and snapshotted when the envelope is activated; local day/month boundaries are converted with the timezone rules that apply to each date. No plan or command may widen its referenced envelope version or its normalized audience boundary.

### `EnvelopeOfferDestinationGrant`

- `Id`, `ProjectId`, `EnvelopeId`, `OfferId`, `DestinationId`
- `AllowedFromUtc`, `AllowedUntilUtc`, `MaximumDailyAllocation`
- `State`: `Active | Suspended | Revoked`

Unique active `(EnvelopeId, OfferId, DestinationId)`. Offer and destination must belong to the same project and use the envelope currency where value is configured.

### `EnvelopeAudienceSourceGrant`

- `Id`, `ProjectId`, `EnvelopeId`
- `SourceType`: `CustomAudience | CustomerList | Engagement | Retargeting | LookalikeSeed`
- `SourceExternalId`, `SourceLabel`
- `AllowedUsesJson`, `ConsentState`, `LegalBasis`, `LegalBasisRecordedAtUtc`
- `StartsAtUtc`, `EndsAtUtc`, `State`

No custom/list/lookalike source enters a plan without an active matching grant and applicable consent/legal basis.

## 3. Immutable campaign strategy and targeting

### `CampaignPlan`

- `Id`, `ProjectId`, `ConnectionId`, `EnvelopeId`, `EnvelopeVersion`
- `OfferId`, `DestinationId`, `CapabilitySnapshotId`
- `Version`, `Name`, `BusinessGoal`, `Objective`, `OptimizationGoal`
- `OptimizationFallbackOrderJson`, `BidStrategy`, `BudgetMode`
- `DailyBudget`, `Currency`, `StartsAtUtc`, `EndsAtUtc`
- `SpecialAdCategory`, `PlacementMode` (`AdvantagePlusDynamic`)
- `AudienceStrategyId`, `ExperimentId`
- `PlanJson`, `PlanHash`, `ReadinessJson`
- `State`: `Draft | Ready | Blocked | Superseded | Provisioning | Provisioned`
- `CreatedBy`: `AI | UserImport`, `CreatedAtUtc`

Unique `(ProjectId, PlanHash)`. The plan becomes immutable after `Provisioning`; a changed fact, envelope, destination, capability or creative produces a new version.

### `AudienceStrategy`

- `Id`, `ProjectId`, `OfferId`, `EnvelopeId`, `Version`
- `IncludedGeoJson`, `ExcludedGeoJson`, `MinimumAge`, `MaximumAgeSuggestion`
- `RequiredLanguagesJson`, `CustomAudienceExclusionsJson`
- `AudienceSuggestionsJson`, `AuthorizedSourceGrantIdsJson`
- `SpecialCategoryConstraintsJson`, `EstimatedReachJson`
- `DefinitionHash`, `EvidenceJson`
- `State`: `Draft | Eligible | Blocked | Superseded`

Validation rules:

- Included location cannot be empty.
- Minimum age cannot violate provider/jurisdiction/special-category rules.
- Detailed-targeting exclusions are unsupported.
- Hard controls cannot appear only in suggestions.
- Every customer-derived source references an active grant.
- Normalized hard controls MUST be equal to or narrower than the referenced envelope boundary; provider defaults and AI suggestions cannot widen them.

### `CampaignPlanCreative`

- `Id`, `ProjectId`, `PlanId`, `CreativeId`, `CreativeVariantId`
- `Role`: `Control | Variant | Winner | Replacement`
- `ConceptKey`, `HookKey`, `PlacementCompatibilityJson`
- `State`: `Selected | Ineligible | Superseded`

Unique `(PlanId, CreativeVariantId, Role)`.

## 4. Experiments

### `AdvertisingExperiment`

- `Id`, `ProjectId`, `OfferId`, `DestinationId`, `EnvelopeId`
- `Name`, `Hypothesis`, `PrimaryVariable`
- `BusinessOutcome`, `AttributionWindowDays`
- `MinimumElapsedHours`, `MinimumSpend`, `MinimumAttributedOutcomes`
- `MinimumAttributionCoverage`, `CorrectionLagHours`, `ConfidencePolicyJson`
- `BudgetCap`, `StopRuleJson`, `DefinitionHash`
- `State`: `Planned | Validating | Active | Learning | Mature | Winner | Loser | Inconclusive | Stopped | Invalid`
- `StartedAtUtc`, `MaturedAtUtc`, `StoppedAtUtc`, `ConclusionJson`

Only one primary variable is allowed. A plan cannot reference an `Invalid` experiment. `Winner`, `Loser` and `Inconclusive` require a persisted maturity evaluation.

### `AdvertisingExperimentArm`

- `Id`, `ProjectId`, `ExperimentId`, `Name`, `IsControl`
- `ChangedValueJson`, `PlanId`, `ManagedTargetType`, `ManagedTargetId`
- `AllocatedBudget`, `State`, `EvidenceJson`

Exactly one active control arm per experiment. Arm budgets sum to at most experiment budget cap and the envelope ledger reservation.

### `ExperimentEvaluation`

- `Id`, `ProjectId`, `ExperimentId`
- `WindowStartUtc`, `WindowEndUtc`, `AttributionCutoffUtc`
- `Goal`, `EvidenceJson`, `Coverage`, `SampleSize`
- `Verdict`: `Wait | Winner | Loser | Inconclusive | StopSafety`
- `ReasonCodesJson`, `EvaluatedAtUtc`

Append-only. The latest mature evaluation determines experiment conclusion.

## 5. Provider-managed hierarchy

Shared managed fields: `Id`, `ProjectId`, `PlanId`, `ConnectionId`, `OwnershipRecordId`, `ExternalId`, `Name`, `ConfiguredStatus`, `EffectiveStatus`, `ReviewStatus`, `ReconciliationState`, `PlannedStateHash`, `EffectiveStateHash`, `LastSyncedAtUtc`, `LastProviderErrorCode`, `LastProviderErrorSummary`, `ConcurrencyToken`.

`ReconciliationState`:

`Draft | Creating | Partial | PausedUnverified | VerifiedPaused | ActivationQueued | Active | Rejected | Unknown | Drifted | Paused | LegacyUnverified | Archived`

### `ManagedCampaign`

- Shared fields
- `Objective`, `BuyingType`, `SpecialAdCategory`
- `BudgetMode`, `DailyBudget`, `LifetimeBudget`, `BidStrategy`

### `ManagedOwnershipRecord`

- `Id`, `ProjectId`, `ConnectionId`, `RootManagedCampaignId`, `ProviderCampaignExternalId`
- `OwnershipKind`: `AutopilotCreated | ImportedWithAuthority | ManualUnowned`
- `AuthorizedByUserId`, `AuthorizedAtUtc`, `ImportEvidenceJson`
- `AllowedMutationScopeJson`, `RevokedAtUtc`, `ConcurrencyToken`

Only `AutopilotCreated` or an active `ImportedWithAuthority` record permits status/budget mutation. Discovery/backfill creates `ManualUnowned`; it never changes the provider status.

### `ManagedAdSet`

- Shared fields
- `CampaignId`, `AudienceStrategyId`, `ExperimentArmId`
- `OptimizationGoal`, `DestinationType` (`WhatsApp`)
- `PromotedPageExternalId`, `PromotedWhatsAppPhoneExternalId`
- `AttributionSetting`, `PlacementMode`
- `DailyBudget`, `LifetimeBudget`, `BudgetOwnerExternalId`

### `ManagedProviderCreative`

- Shared fields without delivery status where not applicable
- `AdvertisingCreativeId`, `CreativeVariantId`
- `SourceType`, `ObjectStoryExternalId`, `ProviderCreativeType`
- `PageExternalId`, `WhatsAppPhoneExternalId`, `CallToAction`
- `VerificationState`: `Unverified | Verified | Rejected | Drifted`

### `ManagedAdvertisement`

- Shared fields
- `AdSetId`, `ManagedProviderCreativeId`, `ExperimentArmId`
- `DestinationType` (`WhatsApp`), `DestinationId`

Constraints:

- External ID unique within `(ProjectId, ConnectionId, managed type)`.
- Parent records must share ProjectId, PlanId and ConnectionId.
- Every ad set and ad destination is WhatsApp and matches the plan destination.
- Activation requires campaign, ad set and ad in `VerifiedPaused`, creative in `Verified`, eligible ownership and capability snapshots, and no blocking field-level equivalence finding. Full snapshot-hash equality is diagnostic only; documented provider defaults and dynamic placement resolution may differ without widening destination, audience, spend or policy authority.
- System issues no permanent delete. Local `Archived` preserves provider identity and history.

## 6. Provider validation and reliable operations

### `ProviderOperation`

- `Id`, `ProjectId`, `ConnectionId`, `PlanId`, `CommandId`
- `OperationType`: `Validate | CreateCampaign | CreateAdSet | CreateCreative | CreateAd | ReadBack | Activate | Pause | UpdateBudget | Reconcile`
- `TargetType`, `LocalTargetId`, `ProviderTargetId`, `DependsOnOperationId`
- `IdempotencyKey`, `RequestFingerprint`, `GraphApiVersion`
- `PlannedPayloadJson`, `ResponseFingerprint`
- `State`: `Pending | Claimed | Sent | Succeeded | Failed | Unknown | Reconciling | Stale | Blocked | Cancelled`
- `AttemptCount`, `LeaseOwner`, `LeaseExpiresAtUtc`
- `SentAtUtc`, `CompletedAtUtc`, `NextAttemptAtUtc`
- `ProviderRequestId`, `ProviderTraceId`, `ErrorCode`, `ErrorSubcode`, `ErrorSummary`, `Retryable`
- `ConcurrencyToken`

Unique `(ProjectId, IdempotencyKey)`. `Unknown` transitions only to `Reconciling`; no blind resend.

### `ProviderObjectSnapshot`

- `Id`, `ProjectId`, `ConnectionId`, `PlanId`, `OperationId`
- `ObjectType`, `LocalObjectId`, `ProviderObjectId`
- `SnapshotType`: `Planned | Validation | Effective`
- `NormalizedStateJson`, `StateHash`, `CapturedAtUtc`
- `GraphApiVersion`, `ProviderTraceId`

Unique `(OperationId, ObjectType, SnapshotType, StateHash)`.

### `ProviderValidationFinding`

- `Id`, `ProjectId`, `PlanId`, `OperationId`
- `Severity`: `Info | Warning | Blocking`
- `Stage`, `ObjectType`, `ObjectId`, `Field`
- `Code`, `ProviderCode`, `ProviderSubcode`, `Message`, `NextSafeAction`
- `ResolvedAtUtc`, `ResolutionOperationId`

Activation requires no unresolved blocking finding.

## 7. Creatives

### `AdvertisingCreative`

- `Id`, `ProjectId`, `OfferId`, `SourceType` (`ExistingPagePost | ProjectAsset`)
- `SourceExternalId`, `SourceAssetId`, `SourceHash`, `SourceVersion`
- `MediaType` (`Image | Carousel | Video`), `ConceptKey`, `HookKey`
- `RightsState`, `PolicyState`, `EligibilityState`
- `RecommendationBand`, `RecommendationEvidenceJson`
- `OrganicEvidenceJson`, `PaidEvidenceJson`
- `FatigueState`, `LastAnalyzedAtUtc`, `ArchivedAtUtc`

Recommendation is `Preferred | Eligible | NeedsReview | Ineligible`, not a fabricated precise win percentage.

### `AdvertisingCreativeVariant`

- `Id`, `ProjectId`, `CreativeId`, `PlacementFormat`
- `Width`, `Height`, `DurationMs`
- `Headline`, `PrimaryText`, `Description`, `CallToAction`
- `StorageObjectKey`, `ThumbnailObjectKey`, `ContentHash`
- `PageCompatibilityJson`, `WhatsAppDestinationCompatibilityJson`
- `EligibilityState`, `OfferFactHash`, `GeneratedAtUtc`

Variants cannot introduce facts absent from the cited offer profile. Storage keys retain project prefix and content hash.

## 8. Budget ledger and insights

### `BudgetPeriodLedger`

- `Id`, `ProjectId`, `EnvelopeId`, `EnvelopeVersion`
- `PeriodKind`, `PeriodStartUtc`, `PeriodEndUtc`
- `AuthorizedCap`, `SafetyReserve`, `UsableCap`
- `CommittedAmount`, `ObservedSpend`, `ReleasedAmount`, `DelayedSpendEstimate`, `ForecastSpend`
- `Currency`, `LastReconciledAtUtc`, `ConcurrencyToken`

Unique `(ProjectId, EnvelopeId, PeriodKind, PeriodStartUtc)`. `GuardedExposure = max(ObservedSpend + DelayedSpendEstimate, CommittedAmount)`. A reservation of delta is valid only when `max(ObservedSpend + DelayedSpendEstimate, CommittedAmount + delta) <= UsableCap` for every applicable daily and monthly/total ledger.

### `BudgetAllocation`

- `Id`, `ProjectId`, `PlanId`, `ExperimentId`
- `TargetType`, `TargetId`, `ExternalBudgetOwnerId`
- `Purpose`: `Winner | CreativeTest | AudienceTest | Retargeting | Canary | Baseline`
- `AllocatedAmount`, `StartsAtUtc`, `EndsAtUtc`
- `State`: `Reserved | Applied | Released | Expired | Cancelled`
- `DecisionId`, `ConcurrencyToken`

One active allocation per target/purpose/authorization window. Release precedes reallocation in the same database transaction.

### `BudgetAllocationLedgerDebit`

- `Id`, `ProjectId`, `AllocationId`, `LedgerId`, `ReservedAmount`
- `State`: `Reserved | Applied | Released | Cancelled`
- `CreatedAtUtc`, `ReleasedAtUtc`, `ConcurrencyToken`

Unique `(AllocationId, LedgerId)`. Reservation locks every applicable ledger in deterministic `(PeriodKind, PeriodStartUtc, Id)` order, validates guarded exposure on all of them, inserts every debit and increments every committed amount in one database transaction. Failure on any period rolls back all debits.

### `InsightsSnapshot`

- `Id`, `ProjectId`, `ConnectionId`, `TargetType`, `TargetId`
- `IntervalStartUtc`, `IntervalEndUtc`, `AccountTimezone`
- `AttributionSetting`, `BreakdownHash`, `Currency`
- `Spend`, `Impressions`, `Reach`, `Clicks`, `Frequency`
- `ProviderActionsJson`, `ProviderActionValuesJson`, `PlacementBreakdownJson`
- `LearningStatus`, `FetchedAtUtc`, `SourceFreshnessUtc`
- `FetchRunId`, `Revision`, `SupersedesSnapshotId`, `IsCurrent`

Only one current row exists for `(ProjectId, TargetType, TargetId, IntervalStartUtc, IntervalEndUtc, BreakdownHash)`. Each overlapping pull appends a revision and atomically marks the previous row non-current. Reporting aggregates current canonical intervals once and preserves revision history for provider corrections.

## 9. WhatsApp attribution and canonical outcomes

### `WhatsAppAttributionObservation`

- `Id`, `ProjectId`, `DestinationId`, `DestinationVersion`
- `ConversationId`, `CustomerId`, `MessageExternalId`, `ReceivingIdentityExternalId`
- `ObservedAtUtc`, `IntegrationMode`, `Source`
- `IdentifierState`: `CtwaClid | OpaquePayloadOnly | Missing | Invalid`
- `ReferralPayloadHash`, `OpaquePayloadHash`, `FailureCode`

Unique `(ProjectId, MessageExternalId)`. The first inbound message for every new conversation creates an observation, including missing/opaque cases, so coverage has a truthful denominator. Only `CtwaClid` may create an attribution touch; opaque payloads are never decrypted or interpreted.

### `WhatsAppAttributionContext`

- `Id`, `ProjectId`, `ConversationId`, `CustomerId`
- `JourneyStartedAtUtc`, `LastTouchAtUtc`, `AttributionWindowEndsAtUtc`
- `JourneyKey`, `LastConversationId`
- `SelectedTouchId`, `AttributionModel` (`LastEligibleWhatsAppTouch`)
- `AttributionModelVersion`, `State`: `Attributed | Unattributed | Expired | Conflicted`
- `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`

Unique active `(ProjectId, JourneyKey)`, where the journey key is a protected stable customer/contact reference supplied by a source event. This is Advertising-owned and stores only foreign identifiers from events, not direct Conversation navigation. Multiple conversations may belong to one journey.

### `AdvertisingAttributionTouch`

- `Id`, `ProjectId`, `ContextId`, `ConversationId`, `CustomerId`, `MessageExternalId`
- `ProtectedCtwaClid`, `ProtectionPurpose`, `ProtectionKeyVersion`, `CtwaClidHash`, `ReferralPayloadHash`
- `ProviderAdExternalId`, `CampaignId`, `AdSetId`, `AdvertisementId`, `CreativeId`
- `TouchedAtUtc`, `ExpiresAtUtc`, `EligibilityState`, `IneligibilityReason`
- `Source`: `BaileysReferral | CloudWebhook | ExternalConversion`

Unique `(ProjectId, MessageExternalId, CtwaClidHash)`. All touches are preserved; only an eligible non-expired touch may be selected. Selection searches the same project and journey/customer, prefers an eligible touch inside the outcome window with greatest `TouchedAtUtc`, then breaks exact timestamp ties by stable touch ID. Advertising may unprotect the identifier only inside the CAPI adapter using the fixed purpose `Advertising.BusinessMessaging.CtwaClid.v1`; plaintext is never persisted or logged.

### `ConversionSourceEvent`

- `Id`, `ProjectId`, `SourceSystem`, `ExternalEventId`, `SchemaVersion`
- `BusinessAggregateType`, `BusinessAggregateId`, `CanonicalBusinessKey`
- `EventType`, `OccurredAtUtc`, `Value`, `Currency`
- `NormalizedPayloadJson`, `PayloadHash`, `SignatureTimestampUtc`, `ReceivedAtUtc`
- `ConsentState`, `LegalBasis`, `ConsentVersion`
- `ProcessingState`, `FailureCode`

Unique `(ProjectId, SourceSystem, ExternalEventId)`. A conflicting payload with the same identity opens an incident.

### `CanonicalConversion`

- `Id`, `ProjectId`, `CanonicalBusinessKey`, `EventType`, `OccurredAtUtc`
- `CustomerReference`, `ConversationId`, `BusinessAggregateType`, `BusinessAggregateId`
- `Value`, `Currency`, `CurrentValue`, `ContributionValue`, `SourceStrength`
- `AttributionContextId`, `AttributionTouchId`
- `CampaignId`, `AdSetId`, `AdvertisementId`, `CreativeId`
- `InternalAttributionMethod`, `InternalAttributionWindowDays`
- `MetaReportedAttributionJson`
- `ConsentState`, `LegalBasis`, `ProtectedMatchData`
- `JourneyLocation`: `WhatsAppThread | Website | App | Offline | Unknown`
- `TruthState`: `Observed | Verified | Rejected | Conflicted`
- `AttributionState`: `Pending | Attributed | Unattributed | Expired | Conflicted`
- `CorrectionState`: `None | Adjusted | FullyReversed`

Unique `(ProjectId, CanonicalBusinessKey)`. Cross-source events merge only when the canonical business key is proven. A conversation start cannot be mutated into a purchase without a stronger source event.

### `ConversionAdjustment`

- `Id`, `ProjectId`, `ConversionId`, `SourceEventId`, `ExternalEventId`
- `Kind`: `Refund | Chargeback | Cancellation | Absence | Churn | LostDeal | ValueCorrection`
- `ValueDelta`, `ContributionDelta`, `Reason`, `OccurredAtUtc`

Unique `(ProjectId, SourceEventId)`. Adjustments are append-only.

### `ConversionDelivery`

- `Id`, `ProjectId`, `ConversionId`, `DestinationType`
- `MetaEventId`, `MetaEventName`, `PayloadHash`
- `ActionSource`, `MessagingChannel`, `WabaExternalId`, `CtwaClidHash`
- `State`: `Pending | Delivered | FailedRetryable | FailedTerminal | Suppressed`
- `NextAttemptAtUtc`, `LastProviderRequestId`, `LastProviderTraceId`, `AcceptedEventCount`, `WarningJson`, `ErrorCode`

Unique `(ProjectId, MetaEventId, MetaEventName, DestinationType)`. A WhatsApp-thread delivery requires WABA plus eligible attribution touch.

### `ConversionDeliveryAttempt`

- `Id`, `ProjectId`, `DeliveryId`, `AttemptNumber`, `AttemptedAtUtc`, `CompletedAtUtc`
- `PayloadHash`, `ProviderRequestId`, `ProviderTraceId`
- `ConsentVersionChecked`, `PrivacyDecision`, `CapabilitySnapshotId`
- `HttpStatus`, `AcceptedEventCount`, `WarningJson`, `ErrorCode`, `Retryable`
- `Result`: `Accepted | Warning | FailedRetryable | FailedTerminal | Unknown`

Unique `(DeliveryId, AttemptNumber)`. Every retry rechecks current consent/privacy state and capability before constructing a new attempt.

### `AdvertisingWebhookSource`

- `Id`, `ProjectId`, `Name`, `SourceSystem`, `AllowedEventTypesJson`
- `ProtectedSigningSecret`, `SecretVersion`, `RevealNonceHash`
- `ReplayWindowSeconds`, `LastAcceptedAtUtc`, `State`: `Active | Rotating | Revoked`
- `CreatedByUserId`, `CreatedAtUtc`, `RotatedAtUtc`, `RevokedAtUtc`

Unique active `(ProjectId, SourceSystem, Name)`. Secret reveal is one-time. Signature timestamp/nonces and source event IDs provide replay evidence; rotation supports an explicit bounded overlap.

## 10. Tracking health

### `TrackingHealthSnapshot`

- `Id`, `ProjectId`, `ConnectionId`, `DestinationId`
- `WindowStartUtc`, `WindowEndUtc`, `GeneratedAtUtc`
- `InboundConversationCount`, `CtwaIdentifierCount`, `OpaqueReferralCount`, `MissingReferralCount`, `AttributableConversationCount`
- `ReferralCoverage`, `OutcomeAttributionCoverage`, `MissingReferralRate`
- `ExactMatchRate`, `TrackingHealthPolicyId`, `TrackingHealthPolicyVersion`
- `ProviderReportedMatchQuality`, `ProviderMatchQualitySource`
- `MedianEventDelaySeconds`, `DedupeConflictRate`, `CorrectionRate`
- `BusinessMessagingAcceptedRate`, `LastAcceptedEventAtUtc`
- `DatasetState`, `WabaState`, `SourceFreshnessJson`
- `State`: `Healthy | Degraded | Unsafe | Unknown`
- `ReasonCodesJson`, `EvidenceJson`

Unique `(ProjectId, DestinationId, WindowStartUtc, WindowEndUtc)`. Recent insight rows alone cannot produce `Healthy`.

### `TrackingHealthPolicy`

- `Id`, `ProjectId`, `EnvelopeId`, `Version`, `OutcomeGoal`
- `MinimumReferralCoverage`, `MinimumExactMatchRate`, `MinimumOutcomeAttributionCoverage`
- `MaximumMissingReferralRate`, `MaximumMedianDelaySeconds`, `MaximumDedupeConflictRate`, `MaximumCorrectionRate`
- `MinimumBusinessMessagingAcceptedRate`, `StartsAtUtc`, `SupersededAtUtc`

`ExactMatchRate = eligible in-thread outcomes with an eligible exact ctwa/WABA/destination touch / all eligible in-thread outcomes in the window`. Provider event-match quality, when exposed, is stored separately as provider evidence and never fabricated. `Healthy` requires every applicable policy threshold; insufficient denominator is `Unknown`, not healthy.

## 11. Decisions, commands and impact

### `AdvertisingDecision`

- `Id`, `ProjectId`, `PlanId`, `ExperimentId`
- `ActionType`, `TargetType`, `TargetId`
- `EvidenceStartUtc`, `EvidenceEndUtc`, `AttributionCutoffUtc`
- `EvidenceJson`, `EvidenceHash`, `ProposedChangeJson`
- `ReasonCodesJson`, `RiskClass`, `StrategistResultJson`
- `PromptVersion`, `ModelVersion`
- `State`: `Proposed | Reviewing | Waiting | Rejected | Approved | Executing | Executed | Failed | Superseded`
- `EvaluateAfterUtc`, `CreatedAtUtc`

Closed action types: create, validate, activate, reconcile, quarantine, start/stop experiment, replace creative, adjust audience suggestion, reserve/release/reallocate/adjust budget, pause/resume, promote/retire and change optimization goal.

### `AdvertisingAiWorkItem`

- `Id`, `ProjectId`, `Purpose`, `InputVersion`, `InputPayloadJson`, `InputObjectReferencesJson`, `InputHash`, `CandidateIdsJson`
- `PromptVersion`, `RequestedAtUtc`, `CompletedAtUtc`
- `State`: `Pending | Completed | Failed | Expired`
- `ResultJson`, `ModelVersion`, `FailureCode`, `CorrelationId`

Advertising writes the work item plus `AdvertisingAiWorkRequested.v1` atomically and consumes `AdvertisingAiWorkCompleted.v1`. It stores no Gemini credential and never calls AI module internals synchronously.

### `DecisionReview`

- `Id`, `ProjectId`, `DecisionId`
- `ReviewerType`: `Statistical | Strategist | Auditor | Judge | Safety`
- `Verdict`: `Approve | Reject | Wait | Escalate`
- `ReasonCodesJson`, `ReasonsJson`, `EvidenceHash`, `CreatedAtUtc`

Unique `(DecisionId, ReviewerType, EvidenceHash)`. Safety is final and fail-closed.

### `ExecutionCommand`

- `Id`, `ProjectId`, `DecisionId`, `PlanId`
- `IdempotencyKey`, `CommandType`, `TargetType`, `TargetId`, `TargetExternalId`
- `EnvelopeId`, `EnvelopeVersion`, `ExpectedStateHash`
- `DesiredStateJson`, `RequestFingerprint`
- `State`: `Pending | Claimed | Sent | Succeeded | Failed | Unknown | Reconciling | Stale | Blocked | Cancelled`
- `LeaseOwner`, `LeaseExpiresAtUtc`, `AttemptCount`
- `ProviderRequestId`, `LastErrorCode`, `LastErrorSummary`, `ConcurrencyToken`

Unique `(ProjectId, IdempotencyKey)`.

### `DecisionImpact`

- `Id`, `ProjectId`, `DecisionId`
- `BaselineStartUtc`, `BaselineEndUtc`, `OutcomeStartUtc`, `OutcomeEndUtc`
- `AttributionCutoffUtc`, `Goal`, `BaselineJson`, `OutcomeJson`
- `Coverage`, `SampleSize`, `Verdict`: `Positive | Negative | Inconclusive | Reverted`
- `ReasonCodesJson`, `EvaluatedAtUtc`

Unique mature impact per decision/window; later correction can append a re-evaluation.

### `TrackingIncident` and `EmergencyStopRecord`

Incident stores category, severity, affected destination/plan, detected/recovered timestamps, reason codes, evidence and provider trace. Recovery requires a healthy measured snapshot.

Emergency record stores trigger (`Manual | AbnormalSpend | CapRisk | CrossProjectGuard | Provider | TrackingUnsafe | RepeatedFinancialCommand | LostAuthorization`), actor, reason, affected managed IDs, command progress and resume actor/time. An active record blocks new commands.

### `AutopilotDisableRequest`

- `Id`, `ProjectId`, `RequestedByUserId`, `RequestedAtUtc`
- `Mode`: `PauseManaged | LeaveRunning`
- `ContinuingSpendAcknowledgedAtUtc`, `Reason`, `AffectedOwnershipIdsJson`
- `State`: `Requested | Executing | Completed | PartiallyFailed`

Every normal disable creates a request. `PauseManaged` is always the default presented and executed when no explicit mode is supplied; `LeaveRunning` requires an authorized actor and a per-request continuing-spend acknowledgement.

### `AdvertisingAuditRecord`

- `Id`, `ProjectId`, `ActorType`, `ActorId`, `Action`, `TargetType`, `TargetId`
- `BeforeHash`, `AfterHash`, `CorrelationId`, `ReasonCodesJson`, `OccurredAtUtc`
- `IndexState`, `IndexedAtUtc`, `IndexFailureCode`

Append-only coverage includes connection/envelope/grant changes, ownership transfer, plans, provider operations, decisions, budget debits, CAPI deliveries/consent suppression, disable and Emergency Stop.

### `AdvertisingCycleRun`

- `Id`, `ProjectId`, `JobType`, `TimeBucket`, `State`
- `StartedAtUtc`, `CompletedAtUtc`, `SummaryJson`, `ErrorType`

Unique `(ProjectId, JobType, TimeBucket)` in addition to the Redis lease.

## 12. Reliable integration messaging

### `IntegrationOutboxMessage`

- `Id`, `ProjectId`, `EventType`, `SchemaVersion`, `AggregateType`, `AggregateId`
- `PayloadJson`, `OccurredAtUtc`, `PublishedAtUtc`
- `AttemptCount`, `NextAttemptAtUtc`, `State`

Written in the same transaction as the source-module aggregate change.

### `IntegrationInboxReceipt`

- `Id`, `ProjectId`, `Consumer`, `EventId`
- `ReceivedAtUtc`, `ProcessedAtUtc`, `State`, `FailureCode`

Unique `(Consumer, EventId)`. Inbox receipt and Advertising projection changes commit atomically.

### `ProjectAdvertisingContextProjection`

- `ProjectId`, `LifecycleState`, `ReportingTimezoneIana`, `AiConfigurationVersion`
- `UpdatedFromEventId`, `UpdatedAtUtc`, `Version`

Advertising consumes project lifecycle/timezone/AI configuration events into this projection and never reads Project module tables directly.

## 13. State transitions and invariants

### Provider hierarchy

```text
Draft -> Creating -> PausedUnverified -> VerifiedPaused -> ActivationQueued -> Active
             |              |                 |                  |
             v              v                 v                  v
          Partial        Unknown           Rejected           Drifted
             \______________|_________________|__________________/
                                  |
                              Reconciling
                                  |
                     VerifiedPaused | Paused | Blocked
```

No state reaches `Active` without current capability, connection, envelope, tracking and complete hierarchy validation.

### Normal disable

- Default `PauseManaged`: block new decisions/commands, enqueue idempotent pauses, keep monitoring/reconciliation.
- Explicit `LeaveRunning`: block autonomous mutations but retain spend/health monitoring and prominently record continuing spend.
- Emergency Stop always blocks and pauses managed delivery regardless of normal-disable preference.

### Migration/backfill

- Legacy flattened ads become local `LegacyUnverified`/`ManualUnowned` records and stay read-only for autonomy until explicitly imported. Backfill never pauses, activates or otherwise changes their provider status.
- Existing conversions without an ad referral become `Unattributed`; no synthetic touch is created.
- Existing provider IDs and audit history are preserved.
- Obsolete JSON columns remain for one compatibility migration and are removed only by a later cleanup after production verification.

## 14. Retention and deletion

Project archive/revocation immediately suspends envelopes, blocks new commands and queues pauses for system-managed delivery. Protected credentials and matching/referral data follow the project privacy policy. Financial, decision and provider-operation audit remains immutable for the configured legal retention period. Autopilot never permanently deletes a provider campaign.
