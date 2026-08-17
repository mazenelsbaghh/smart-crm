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
    public string BrandRulesJson { get; set; } = "{}";
    public string ProhibitedClaimsJson { get; set; } = "[]";
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? StaleAtUtc { get; set; }
}

public sealed class AdvertisingOffer : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Service";
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string DestinationsJson { get; set; } = "[]";
    public string MarketsJson { get; set; } = "[]";
    public string AllowedClaimsJson { get; set; } = "[]";
    public string RestrictionsJson { get; set; } = "[]";
    public string State { get; set; } = "Draft";
}

public sealed class AdvertisingFactSource : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid? OfferId { get; set; }
    public string FactName { get; set; } = string.Empty;
    public string FactValue { get; set; } = string.Empty;
    public Guid KnowledgeDocumentId { get; set; }
    public int KnowledgeVersion { get; set; }
    public string Citation { get; set; } = string.Empty;
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
