using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingCreative : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? OfferId { get; set; }
    public CreativeSourceType SourceType { get; set; }
    public string? SourceExternalId { get; set; }
    public Guid? SourceAssetId { get; set; }
    public string? SourceStoragePath { get; set; }
    public string? SourceContentType { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public int SourceVersion { get; set; } = 1;
    public CreativeMediaType MediaType { get; set; }
    public string RightsState { get; set; } = "Unknown";
    public string PolicyState { get; set; } = "Pending";
    public CreativeEligibility EligibilityState { get; set; } = CreativeEligibility.Pending;
    public decimal RecommendationScore { get; set; }
    public string RecommendationEvidenceJson { get; set; } = "{}";
    public string FatigueState { get; set; } = "Fresh";
    public DateTime? LastAnalyzedAtUtc { get; set; }
}

public sealed class AdvertisingCreativeVariant : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CreativeId { get; set; }
    public string Placement { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSize { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public string State { get; set; } = "Ready";
}

public sealed class ManagedAdvertisement : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid CreativeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CampaignExternalId { get; set; }
    public string? AdSetExternalId { get; set; }
    public string? AdExternalId { get; set; }
    public string? BudgetOwnerExternalId { get; set; }
    public string BudgetOwnerType { get; set; } = "AdSet";
    public string PublisherPlatform { get; set; } = "facebook";
    public string ManagementSource { get; set; } = "CreatedBySystem";
    public string PositionsJson { get; set; } = "[]";
    public decimal DailyBudget { get; set; }
    public ManagedDeliveryState ConfiguredStatus { get; set; } = ManagedDeliveryState.Paused;
    public string EffectiveStatus { get; set; } = "PENDING";
    public string? ProviderStateHash { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime? ImportedAtUtc { get; set; }
    public uint Version { get; set; }
}
