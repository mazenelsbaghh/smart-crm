using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingExperiment : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid OfferId { get; set; }
    public Guid DestinationId { get; set; }
    public Guid EnvelopeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hypothesis { get; set; } = string.Empty;
    public string PrimaryVariable { get; set; } = string.Empty;
    public string BusinessOutcome { get; set; } = string.Empty;
    public int AttributionWindowDays { get; set; } = 7;
    public int MinimumElapsedHours { get; set; }
    public decimal MinimumSpend { get; set; }
    public int MinimumAttributedOutcomes { get; set; }
    public decimal MinimumAttributionCoverage { get; set; }
    public int CorrectionLagHours { get; set; }
    public string ConfidencePolicyJson { get; set; } = "{}";
    public decimal BudgetCap { get; set; }
    public string StopRuleJson { get; set; } = "{}";
    public string DefinitionHash { get; set; } = string.Empty;
    public string State { get; set; } = "Planned";
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? MaturedAtUtc { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public string ConclusionJson { get; set; } = "{}";
}

public sealed class AdvertisingExperimentArm : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ExperimentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsControl { get; set; }
    public string ChangedValueJson { get; set; } = "{}";
    public Guid PlanId { get; set; }
    public string ManagedTargetType { get; set; } = string.Empty;
    public Guid? ManagedTargetId { get; set; }
    public decimal AllocatedBudget { get; set; }
    public string State { get; set; } = "Planned";
    public string EvidenceJson { get; set; } = "{}";
}

public sealed class ExperimentEvaluation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ExperimentId { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public DateTime AttributionCutoffUtc { get; set; }
    public string Goal { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public decimal Coverage { get; set; }
    public int SampleSize { get; set; }
    public string Verdict { get; set; } = "Wait";
    public string ReasonCodesJson { get; set; } = "[]";
    public DateTime EvaluatedAtUtc { get; set; }
}
