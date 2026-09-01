using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingProfile : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string KnowledgeRevisionHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Building";
    public string? OfferType { get; set; }
    public string FunnelJson { get; set; } = "[]";
    public string AudienceJson { get; set; } = "{}";
    public string AudienceFactsJson { get; set; } = "{}";
    public string BrandRulesJson { get; set; } = "{}";
    public string ProhibitedClaimsJson { get; set; } = "[]";
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? StaleAtUtc { get; set; }
    public string PromptVersion { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
}

public sealed class AdvertisingOffer : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Service";
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? ContributionMargin { get; set; }
    public decimal? MaximumSustainableCost { get; set; }
    public string PrimaryOutcome { get; set; } = "QualifiedLead";
    public string FallbackOutcomeOrderJson { get; set; } = "[]";
    public int AttributionWindowDays { get; set; } = 7;
    public int? DailyCapacity { get; set; }
    public int? CurrentCapacity { get; set; }
    public DateTime? CapacityUpdatedAtUtc { get; set; }
    public string DestinationsJson { get; set; } = "[]";
    public string MarketsJson { get; set; } = "[]";
    public string AllowedClaimsJson { get; set; } = "[]";
    public string RestrictionsJson { get; set; } = "[]";
    public string ScheduleJson { get; set; } = "{}";
    public string? SpecialAdCategory { get; set; }
    public string PolicyEvidenceJson { get; set; } = "{}";
    public string PolicyState { get; set; } = "Unresolved";
    public string State { get; set; } = "Draft";
}

public sealed class AdvertisingFactSource : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid? OfferId { get; set; }
    public string FactName { get; set; } = string.Empty;
    public string FactValue { get; set; } = string.Empty;
    public string NormalizedValueJson { get; set; } = "{}";
    public Guid KnowledgeDocumentId { get; set; }
    public int KnowledgeVersion { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public bool IsContradictory { get; set; }
    public bool IsRequiredForLaunch { get; set; }
    public string Citation { get; set; } = string.Empty;
}

public sealed class EnvelopeOfferDestinationGrant : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EnvelopeId { get; set; }
    public Guid OfferId { get; set; }
    public Guid DestinationId { get; set; }
    public DateTime AllowedFromUtc { get; set; }
    public DateTime? AllowedUntilUtc { get; set; }
    public decimal? MaximumDailyAllocation { get; set; }
    public string State { get; set; } = "Active";
}

public sealed class EnvelopeAudienceSourceGrant : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EnvelopeId { get; set; }
    public AudienceSourceType SourceType { get; set; }
    public string SourceExternalId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string AllowedUsesJson { get; set; } = "[]";
    public ConsentState ConsentState { get; set; }
    public string? LegalBasis { get; set; }
    public DateTime? LegalBasisRecordedAtUtc { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public string State { get; set; } = "Active";
}

public sealed class AdvertisingPromotion : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EnvelopeId { get; set; }
    public Guid OfferId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string DestinationType { get; set; } = "Website";
    public string DestinationUrl { get; set; } = string.Empty;
    public string OptimizationEvent { get; set; } = "Lead";
    public string FunnelJson { get; set; } = "[]";
    public string AudiencePlanJson { get; set; } = "{}";
    public string AllocationPlanJson { get; set; } = "{}";
    public string ReadinessJson { get; set; } = "{}";
    public PromotionState State { get; set; } = PromotionState.Draft;
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? PausedAtUtc { get; set; }
}
