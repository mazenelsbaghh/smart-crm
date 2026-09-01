using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingWebhookSource : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string ProtectedSigningSecret { get; set; } = string.Empty;
    public string? PreviousProtectedSigningSecret { get; set; }
    public string AllowedEventTypesJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    public WebhookSourceState State { get; set; } = WebhookSourceState.Active;
    public DateTime? RotatedAtUtc { get; set; }
    public DateTime? OverlapEndsAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplayEvidenceJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAtUtc { get; set; }
}

public sealed class WhatsAppAttributionObservation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid CustomerId { get; set; }
    public string JourneyKey { get; set; } = string.Empty;
    public string MessageExternalId { get; set; } = string.Empty;
    public DateTime MessageOccurredAtUtc { get; set; }
    public Guid DestinationId { get; set; }
    public long DestinationVersion { get; set; }
    public string ReceivingIdentityExternalId { get; set; } = string.Empty;
    public WhatsAppIntegrationMode IntegrationMode { get; set; }
    public ReferralIdentifierState IdentifierState { get; set; }
    public string? ProtectedCtwaClid { get; set; }
    public string? ProtectionPurpose { get; set; }
    public string? CtwaClidHash { get; set; }
    public string? OpaquePayloadHash { get; set; }
    public string? ProviderAdExternalId { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string GatewayType { get; set; } = string.Empty;
}

public sealed class WhatsAppAttributionContext : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid CustomerId { get; set; }
    public string JourneyKey { get; set; } = string.Empty;
    public Guid DestinationId { get; set; }
    public DateTime FirstObservedAtUtc { get; set; }
    public DateTime LastObservedAtUtc { get; set; }
    public int ObservationCount { get; set; }
    public int ValidReferralCount { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ConversionSourceEvent : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string PayloadHash { get; set; } = string.Empty;
    public string NormalizedPayloadJson { get; set; } = "{}";
    public string EventType { get; set; } = string.Empty;
    public string BusinessAggregateType { get; set; } = string.Empty;
    public string BusinessAggregateId { get; set; } = string.Empty;
    public string JourneyLocation { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public string? ConsentEvidenceJson { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string ProcessingState { get; set; } = "Accepted";
}

public sealed class CanonicalConversion : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string CanonicalKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? CustomerReference { get; set; }
    public string? VisitorReference { get; set; }
    public decimal? Value { get; set; }
    public decimal? CurrentValue { get; set; }
    public string? Currency { get; set; }
    public Guid? AdvertisementId { get; set; }
    public Guid? CreativeId { get; set; }
    public string AttributionMethod { get; set; } = "Unattributed";
    public ConsentState ConsentState { get; set; }
    public string? LegalBasis { get; set; }
    public string? ProtectedMatchData { get; set; }
    public ConversionState State { get; set; } = ConversionState.Observed;
    public string TruthState { get; set; } = "Observed";
    public AttributionState AttributionState { get; set; } = AttributionState.Pending;
    public CorrectionState CorrectionState { get; set; }
    public string SourceHistoryJson { get; set; } = "[]";
    public Guid? AttributionTouchId { get; set; }
    public DateTime? AttributionWindowEndsAtUtc { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ConversionAdjustment : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversionId { get; set; }
    public Guid? SourceEventId { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal ValueDelta { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class AdvertisingAttributionTouch : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ConversionId { get; set; }
    public Guid? AttributionContextId { get; set; }
    public Guid? ObservationId { get; set; }
    public Guid? ConversationId { get; set; }
    public string? JourneyKey { get; set; }
    public Guid? DestinationId { get; set; }
    public Guid? AdvertisementId { get; set; }
    public string Method { get; set; } = "Unattributed";
    public string? ExternalClickIdHash { get; set; }
    public string? ProtectedCtwaClid { get; set; }
    public string? ProviderAdExternalId { get; set; }
    public string EligibilityEvidenceJson { get; set; } = "{}";
    public DateTime TouchedAtUtc { get; set; }
}

public sealed class ConversionDelivery : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversionId { get; set; }
    public string Provider { get; set; } = "MetaBusinessMessaging";
    public string EventIdentity { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public ConversionDeliveryState State { get; set; } = ConversionDeliveryState.Pending;
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public string? SuppressionReason { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ConversionDeliveryAttempt : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? DeliveryId { get; set; }
    public Guid ConversionId { get; set; }
    public string Provider { get; set; } = "Meta";
    public int AttemptNumber { get; set; }
    public string State { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderTraceId { get; set; }
    public int? EventsReceived { get; set; }
    public string? WarningsJson { get; set; }
    public string? ResponseHash { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}
