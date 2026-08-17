using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingWebhookSource : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public string ProtectedSigningSecret { get; set; } = string.Empty;
    public string AllowedEventTypesJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime? LastUsedAtUtc { get; set; }
}

public sealed class ConversionSourceEvent : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalEventId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string PayloadHash { get; set; } = string.Empty;
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
}

public sealed class ConversionAdjustment : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversionId { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal ValueDelta { get; set; }
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class AdvertisingAttributionTouch : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversionId { get; set; }
    public Guid? AdvertisementId { get; set; }
    public string Method { get; set; } = "Unattributed";
    public string? ExternalClickIdHash { get; set; }
    public DateTime TouchedAtUtc { get; set; }
}

public sealed class ConversionDeliveryAttempt : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversionId { get; set; }
    public string Provider { get; set; } = "Meta";
    public int AttemptNumber { get; set; }
    public string State { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
}
