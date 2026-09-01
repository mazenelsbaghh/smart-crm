using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class BudgetPeriodLedger : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EnvelopeId { get; set; }
    public long EnvelopeVersion { get; set; }
    public string PeriodKind { get; set; } = "Daily";
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal AuthorizedCap { get; set; }
    public decimal SafetyReserve { get; set; }
    public decimal UsableCap { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ObservedSpend { get; set; }
    public decimal ReleasedAmount { get; set; }
    public decimal DelayedSpendEstimate { get; set; }
    public decimal ForecastSpend { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime? LastReconciledAtUtc { get; set; }
    public uint Version { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class BudgetAllocation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid LedgerId { get; set; }
    public Guid? PlanId { get; set; }
    public Guid? ExperimentId { get; set; }
    public string TargetType { get; set; } = "Ad";
    public Guid TargetId { get; set; }
    public string? ExternalBudgetOwnerId { get; set; }
    public BudgetPurpose Purpose { get; set; }
    public decimal AllocatedAmount { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string State { get; set; } = "Active";
    public Guid? DecisionId { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class BudgetAllocationLedgerDebit : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid AllocationId { get; set; }
    public Guid LedgerId { get; set; }
    public decimal ReservedAmount { get; set; }
    public string State { get; set; } = "Reserved";
    public DateTime? ReleasedAtUtc { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class InsightsSnapshot : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string TargetType { get; set; } = "Ad";
    public Guid TargetId { get; set; }
    public DateTime IntervalStartUtc { get; set; }
    public DateTime IntervalEndUtc { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Reach { get; set; }
    public long Clicks { get; set; }
    public decimal Frequency { get; set; }
    public string AccountTimezone { get; set; } = "UTC";
    public string AttributionSetting { get; set; } = string.Empty;
    public string BreakdownHash { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public string ProviderActionsJson { get; set; } = "{}";
    public string ProviderActionValuesJson { get; set; } = "{}";
    public string PlacementBreakdownJson { get; set; } = "{}";
    public string? LearningStatus { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public DateTime? SourceFreshnessUtc { get; set; }
    public Guid FetchRunId { get; set; }
    public int Revision { get; set; } = 1;
    public Guid? SupersedesSnapshotId { get; set; }
    public bool IsCurrent { get; set; } = true;
}
