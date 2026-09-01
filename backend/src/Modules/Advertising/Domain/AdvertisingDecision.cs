using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingDecision : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? PromotionId { get; set; }
    public Guid? PlanId { get; set; }
    public Guid? EnvelopeId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public DateTime EvidenceStartUtc { get; set; }
    public DateTime EvidenceEndUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string EvidenceHash { get; set; } = string.Empty;
    public string ReasonCodesJson { get; set; } = "[]";
    public string ProposedChangeJson { get; set; } = "{}";
    public string RiskClass { get; set; } = "Low";
    public DecisionState State { get; set; } = DecisionState.Proposed;
    public DateTime? EvaluateAfterUtc { get; set; }
    public Guid? ExecutionCommandId { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class AdvertisingAiWorkItem : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string InputVersion { get; set; } = string.Empty;
    public string InputHash { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public Guid OwnerId { get; set; }
    public long OwnerVersion { get; set; }
    public AiWorkState State { get; set; } = AiWorkState.Pending;
    public DateTime DeadlineUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResultJson { get; set; }
    public string? ModelVersion { get; set; }
    public string? FailureCode { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class DecisionReview : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DecisionId { get; set; }
    public string ReviewerType { get; set; } = string.Empty;
    public DecisionVerdict Verdict { get; set; }
    public string ReasonsJson { get; set; } = "[]";
    public string EvidenceHash { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public string? PromptVersion { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
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
    public DateTime? ClaimedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public string? ReconciliationEvidenceJson { get; set; }
    public uint Version { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class DecisionImpact : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DecisionId { get; set; }
    public DateTime BaselineWindowStartUtc { get; set; }
    public DateTime BaselineWindowEndUtc { get; set; }
    public DateTime EvaluationWindowStartUtc { get; set; }
    public DateTime EvaluationWindowEndUtc { get; set; }
    public string Goal { get; set; } = string.Empty;
    public string BaselineEvidenceJson { get; set; } = "{}";
    public string EvaluationEvidenceJson { get; set; } = "{}";
    public DecisionImpactLabel Label { get; set; } = DecisionImpactLabel.Inconclusive;
    public DateTime EvaluatedAtUtc { get; set; }
    public Guid? RollbackCommandId { get; set; }
}

public sealed class TrackingHealthPolicy : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public int Version { get; set; }
    public string Goal { get; set; } = string.Empty;
    public int MinimumDenominator { get; set; }
    public decimal MinimumReferralCoverage { get; set; }
    public decimal MinimumExactMatchRate { get; set; }
    public decimal MinimumDeliveryAcceptanceRate { get; set; }
    public decimal MaximumCorrectionRate { get; set; }
    public int MaximumEventDelayMinutes { get; set; }
    public string DefinitionHash { get; set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; set; }
}

public sealed class TrackingHealthSnapshot : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid DestinationId { get; set; }
    public Guid TrackingHealthPolicyId { get; set; }
    public int TrackingHealthPolicyVersion { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public int InboundConversationCount { get; set; }
    public int ReferralObservationCount { get; set; }
    public int ValidReferralCount { get; set; }
    public decimal? ReferralCoverage { get; set; }
    public decimal? ExactMatchRate { get; set; }
    public decimal? ProviderMatchQuality { get; set; }
    public decimal? DeliveryAcceptanceRate { get; set; }
    public decimal? CorrectionRate { get; set; }
    public decimal? MissingReferralRate { get; set; }
    public double? EventDelayMinutesP95 { get; set; }
    public DateTime? SourceFreshnessUtc { get; set; }
    public TrackingHealthState State { get; set; } = TrackingHealthState.Unknown;
    public string ReasonCodesJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "{}";
    public DateTime EvaluatedAtUtc { get; set; }
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
    public string State { get; set; } = "Active";
    public string ProgressJson { get; set; } = "{}";
    public uint ConcurrencyToken { get; set; }
}

public sealed class AutopilotDisableRequest : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public AutopilotDisableMode Mode { get; set; } = AutopilotDisableMode.PauseManaged;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ContinuingSpendAcknowledgedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string State { get; set; } = "Requested";
    public string ProgressJson { get; set; } = "{}";
    public DateTime? CompletedAtUtc { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class AdvertisingAuditRecord : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string ActorType { get; set; } = "System";
    public string SafeEvidenceJson { get; set; } = "{}";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string IndexState { get; set; } = "Pending";
    public int IndexAttemptCount { get; set; }
    public DateTime? NextIndexAttemptAtUtc { get; set; }
    public string? LastIndexErrorCode { get; set; }
    public DateTime? IndexedAtUtc { get; set; }
}

public sealed class AdvertisingCycleRun : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public DateTime BucketStartUtc { get; set; }
    public string ReportingTimezoneIana { get; set; } = "UTC";
    public DateTime BucketEndUtc { get; set; }
    public string LeaseOwner { get; set; } = string.Empty;
    public string State { get; set; } = "Running";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorType { get; set; }
}
