using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingDecision : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? PromotionId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public DateTime EvidenceStartUtc { get; set; }
    public DateTime EvidenceEndUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string ProposedChangeJson { get; set; } = "{}";
    public string RiskClass { get; set; } = "Low";
    public DecisionState State { get; set; } = DecisionState.Proposed;
    public DateTime? EvaluateAfterUtc { get; set; }
}

public sealed class DecisionReview : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DecisionId { get; set; }
    public string ReviewerType { get; set; } = string.Empty;
    public DecisionVerdict Verdict { get; set; }
    public string ReasonsJson { get; set; } = "[]";
    public string EvidenceHash { get; set; } = string.Empty;
}

public sealed class ExecutionCommand : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DecisionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string? TargetExternalId { get; set; }
    public string? ExpectedStateHash { get; set; }
    public string DesiredStateJson { get; set; } = "{}";
    public string RequestFingerprint { get; set; } = string.Empty;
    public CommandState State { get; set; } = CommandState.Pending;
    public int AttemptCount { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? LastError { get; set; }
    public uint Version { get; set; }
}

public sealed class TrackingIncident : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Summary { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public IncidentState State { get; set; } = IncidentState.Open;
    public DateTime DetectedAtUtc { get; set; }
    public DateTime? RecoveredAtUtc { get; set; }
}

public sealed class EmergencyStopRecord : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public EmergencyTrigger Trigger { get; set; }
    public Guid? ActivatedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime ActivatedAtUtc { get; set; }
    public Guid? ResumedByUserId { get; set; }
    public DateTime? ResumedAtUtc { get; set; }
}

public sealed class AdvertisingCycleRun : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public DateTime BucketStartUtc { get; set; }
    public string State { get; set; } = "Running";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorType { get; set; }
}
