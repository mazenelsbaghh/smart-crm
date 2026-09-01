using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class ManagedOwnershipRecord : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? RootManagedCampaignId { get; set; }
    public string ProviderCampaignExternalId { get; set; } = string.Empty;
    public ManagedOwnershipKind OwnershipKind { get; set; } = ManagedOwnershipKind.ManualUnowned;
    public Guid? AuthorizedByUserId { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public string ImportEvidenceJson { get; set; } = "{}";
    public string AllowedMutationScopeJson { get; set; } = "[]";
    public DateTime? RevokedAtUtc { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ManagedCampaign : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid OwnershipRecordId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConfiguredStatus { get; set; } = "PAUSED";
    public string EffectiveStatus { get; set; } = "UNKNOWN";
    public string? ReviewStatus { get; set; }
    public ProviderReconciliationState ReconciliationState { get; set; } = ProviderReconciliationState.Draft;
    public string PlannedStateHash { get; set; } = string.Empty;
    public string? EffectiveStateHash { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public string? LastProviderErrorCode { get; set; }
    public string? LastProviderErrorSummary { get; set; }
    public string Objective { get; set; } = string.Empty;
    public string BuyingType { get; set; } = "AUCTION";
    public string? SpecialAdCategory { get; set; }
    public string BudgetMode { get; set; } = "Campaign";
    public decimal? DailyBudget { get; set; }
    public decimal? LifetimeBudget { get; set; }
    public string BidStrategy { get; set; } = "LOWEST_COST_WITHOUT_CAP";
    public uint ConcurrencyToken { get; set; }
}

public sealed class ManagedAdSet : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid OwnershipRecordId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid AudienceStrategyId { get; set; }
    public Guid? ExperimentArmId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConfiguredStatus { get; set; } = "PAUSED";
    public string EffectiveStatus { get; set; } = "UNKNOWN";
    public string? ReviewStatus { get; set; }
    public ProviderReconciliationState ReconciliationState { get; set; } = ProviderReconciliationState.Draft;
    public string PlannedStateHash { get; set; } = string.Empty;
    public string? EffectiveStateHash { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public string? LastProviderErrorCode { get; set; }
    public string? LastProviderErrorSummary { get; set; }
    public string OptimizationGoal { get; set; } = string.Empty;
    public string DestinationType { get; set; } = "WHATSAPP";
    public string PromotedPageExternalId { get; set; } = string.Empty;
    public string PromotedWhatsAppPhoneExternalId { get; set; } = string.Empty;
    public string AttributionSetting { get; set; } = string.Empty;
    public PlacementPolicy PlacementMode { get; set; } = PlacementPolicy.DynamicEligibleMeta;
    public decimal? DailyBudget { get; set; }
    public decimal? LifetimeBudget { get; set; }
    public string? BudgetOwnerExternalId { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ManagedProviderCreative : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid OwnershipRecordId { get; set; }
    public Guid AdvertisingCreativeId { get; set; }
    public Guid CreativeVariantId { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? ObjectStoryExternalId { get; set; }
    public string ProviderCreativeType { get; set; } = string.Empty;
    public string PageExternalId { get; set; } = string.Empty;
    public string WhatsAppPhoneExternalId { get; set; } = string.Empty;
    public string CallToAction { get; set; } = "WHATSAPP_MESSAGE";
    public ProviderCreativeVerificationState VerificationState { get; set; }
    public string PlannedStateHash { get; set; } = string.Empty;
    public string? EffectiveStateHash { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public string? LastProviderErrorCode { get; set; }
    public string? LastProviderErrorSummary { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ProviderOperation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? PlanId { get; set; }
    public Guid? CommandId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? LocalTargetId { get; set; }
    public string? ProviderTargetId { get; set; }
    public Guid? DependsOnOperationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string GraphApiVersion { get; set; } = "v26.0";
    public string PlannedPayloadJson { get; set; } = "{}";
    public string? ResponseFingerprint { get; set; }
    public ProviderOperationState State { get; set; } = ProviderOperationState.Pending;
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ProviderTraceId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorSubcode { get; set; }
    public string? ErrorSummary { get; set; }
    public bool Retryable { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ProviderObjectSnapshot : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? PlanId { get; set; }
    public Guid OperationId { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public Guid? LocalObjectId { get; set; }
    public string? ProviderObjectId { get; set; }
    public string SnapshotType { get; set; } = string.Empty;
    public string NormalizedStateJson { get; set; } = "{}";
    public string StateHash { get; set; } = string.Empty;
    public DateTime CapturedAtUtc { get; set; }
    public string GraphApiVersion { get; set; } = "v26.0";
    public string? ProviderTraceId { get; set; }
}

public sealed class ProviderValidationFinding : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid PlanId { get; set; }
    public Guid? OperationId { get; set; }
    public InvariantSeverity Severity { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string? ObjectId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ProviderCode { get; set; }
    public string? ProviderSubcode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NextSafeAction { get; set; } = string.Empty;
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolutionOperationId { get; set; }
}
