using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class CampaignPlan : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid EnvelopeId { get; set; }
    public long EnvelopeVersion { get; set; }
    public Guid OfferId { get; set; }
    public Guid DestinationId { get; set; }
    public Guid CapabilitySnapshotId { get; set; }
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string BusinessGoal { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string OptimizationGoal { get; set; } = string.Empty;
    public string OptimizationFallbackOrderJson { get; set; } = "[]";
    public string BidStrategy { get; set; } = "LOWEST_COST_WITHOUT_CAP";
    public string BudgetMode { get; set; } = "Campaign";
    public decimal DailyBudget { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public string? SpecialAdCategory { get; set; }
    public PlacementPolicy PlacementMode { get; set; } = PlacementPolicy.DynamicEligibleMeta;
    public Guid AudienceStrategyId { get; set; }
    public Guid? ExperimentId { get; set; }
    public string PlanJson { get; set; } = "{}";
    public string PlanHash { get; set; } = string.Empty;
    public string ReadinessJson { get; set; } = "{}";
    public string State { get; set; } = "Draft";
    public string CreatedBy { get; set; } = "AI";
}

public sealed class AudienceStrategy : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid OfferId { get; set; }
    public Guid EnvelopeId { get; set; }
    public int Version { get; set; } = 1;
    public string IncludedGeoJson { get; set; } = "[]";
    public string ExcludedGeoJson { get; set; } = "[]";
    public int MinimumAge { get; set; } = 18;
    public int? MaximumAgeSuggestion { get; set; }
    public string RequiredLanguagesJson { get; set; } = "[]";
    public string CustomAudienceExclusionsJson { get; set; } = "[]";
    public string AudienceSuggestionsJson { get; set; } = "{}";
    public string AuthorizedSourceGrantIdsJson { get; set; } = "[]";
    public string SpecialCategoryConstraintsJson { get; set; } = "{}";
    public string EstimatedReachJson { get; set; } = "{}";
    public string DefinitionHash { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string State { get; set; } = "Draft";
}

public sealed class CampaignPlanCreative : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public Guid CreativeId { get; set; }
    public Guid CreativeVariantId { get; set; }
    public string Role { get; set; } = "Variant";
    public string ConceptKey { get; set; } = string.Empty;
    public string HookKey { get; set; } = string.Empty;
    public string PlacementCompatibilityJson { get; set; } = "{}";
    public string State { get; set; } = "Selected";
}
