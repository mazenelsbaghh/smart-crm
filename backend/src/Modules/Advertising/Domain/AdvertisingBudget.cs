using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class BudgetPeriodLedger : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EnvelopeId { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal AuthorizedCap { get; set; }
    public decimal SafetyReserve { get; set; }
    public decimal UsableCap { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ObservedSpend { get; set; }
    public decimal ReleasedAmount { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime? LastReconciledAtUtc { get; set; }
    public uint Version { get; set; }
}

public sealed class BudgetAllocation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid LedgerId { get; set; }
    public Guid TargetId { get; set; }
    public BudgetPurpose Purpose { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string State { get; set; } = "Active";
    public Guid? DecisionId { get; set; }
}

public sealed class InsightsSnapshot : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string TargetType { get; set; } = "Ad";
    public Guid TargetId { get; set; }
    public DateTime IntervalStartUtc { get; set; }
    public DateTime IntervalEndUtc { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Frequency { get; set; }
    public string ProviderActionsJson { get; set; } = "{}";
    public DateTime FetchedAtUtc { get; set; }
}
