using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class ProjectAdvertisingContextProjection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string LifecycleState { get; set; } = "Active";
    public string ReportingTimezoneIana { get; set; } = "Africa/Cairo";
    public long AiConfigurationVersion { get; set; }
    public string AllowedAiModel { get; set; } = string.Empty;
    public string AiSettingsHash { get; set; } = string.Empty;
    public Guid UpdatedFromEventId { get; set; }
    public long SourceVersion { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class AdvertisingKnowledgeProjection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DocumentId { get; set; }
    public long DocumentVersion { get; set; }
    public string RevisionHash { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string SafeFactsJson { get; set; } = "{}";
    public string AffectedOfferKeysJson { get; set; } = "[]";
    public Guid UpdatedFromEventId { get; set; }
    public bool IsTombstoned { get; set; }
}

public sealed class AdvertisingMediaProjection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid AssetId { get; set; }
    public long AssetVersion { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public string ObjectReference { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string RightsState { get; set; } = string.Empty;
    public string BrandMetadataJson { get; set; } = "{}";
    public Guid UpdatedFromEventId { get; set; }
    public bool IsTombstoned { get; set; }
}

public sealed class CustomerAdvertisingConsentProjection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public long ConsentVersion { get; set; }
    public string ConsentState { get; set; } = "Unknown";
    public string LegalBasis { get; set; } = string.Empty;
    public DateTime EffectiveAtUtc { get; set; }
    public Guid UpdatedFromEventId { get; set; }
    public bool IsTombstoned { get; set; }
}

public sealed class AdvertisingProjectionBackfillRun : AuditableEntity
{
    public string State { get; set; } = "Pending";
    public string Phase { get; set; } = "ProjectContext";
    public string CursorJson { get; set; } = "{}";
    public string ParityJson { get; set; } = "{}";
    public int AttemptCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastFailureCode { get; set; }
}
