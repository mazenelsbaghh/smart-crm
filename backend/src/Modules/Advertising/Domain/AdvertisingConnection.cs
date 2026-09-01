using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingConnection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "Meta";
    public string? AdAccountExternalId { get; set; }
    public string? PageExternalId { get; set; }
    public string? DatasetExternalId { get; set; }
    public string? WabaExternalId { get; set; }
    public string? ProtectedAccessToken { get; set; }
    public string GrantedCapabilitiesJson { get; set; } = "[]";
    public string GrantedPermissionsJson { get; set; } = "[]";
    public string? AccountCurrency { get; set; }
    public string? AccountTimezone { get; set; }
    public string? AccountTimezoneIana { get; set; }
    public string? TimezoneSource { get; set; }
    public DateTime? TimezoneValidatedAtUtc { get; set; }
    public string? AccountStatus { get; set; }
    public string? FundingStatus { get; set; }
    public string GraphApiVersion { get; set; } = "v26.0";
    public WhatsAppIntegrationMode? WhatsAppIntegrationMode { get; set; }
    public ReferralProofState ReferralProofState { get; set; } = ReferralProofState.Unverified;
    public DateTime? ReferralProofAtUtc { get; set; }
    public string? ReferralProofHash { get; set; }
    public AdvertisingConnectionState State { get; set; } = AdvertisingConnectionState.PendingSelection;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public string? LastProviderTraceId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class AuthorizedWhatsAppDestination : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? WhatsAppAccountId { get; set; }
    public Guid ConnectionId { get; set; }
    public string Provider { get; set; } = "MetaWhatsApp";
    public string WabaExternalId { get; set; } = string.Empty;
    public string PhoneNumberExternalId { get; set; } = string.Empty;
    public string? DisplayPhoneE164 { get; set; }
    public string PageExternalId { get; set; } = string.Empty;
    public string DatasetExternalId { get; set; } = string.Empty;
    public string ReceivingIdentityExternalId { get; set; } = string.Empty;
    public WhatsAppIntegrationMode WhatsAppIntegrationMode { get; set; }
    public string MessagingState { get; set; } = "Unknown";
    public string AdvertisingState { get; set; } = "Unknown";
    public string BusinessEventsState { get; set; } = "Unknown";
    public ReferralProofState ReferralCaptureState { get; set; } = ReferralProofState.Unverified;
    public DateTime? ReferralProofAtUtc { get; set; }
    public Guid? CapabilitySnapshotId { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public AuthorizedDestinationState State { get; set; } = AuthorizedDestinationState.Pending;
    public long Version { get; set; } = 1;
    public uint ConcurrencyToken { get; set; }
}

public sealed class AdvertisingCapabilitySnapshot : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid DestinationId { get; set; }
    public string GraphApiVersion { get; set; } = "v26.0";
    public string ProviderAccountStatus { get; set; } = "Unknown";
    public string PermissionStateJson { get; set; } = "{}";
    public string ObjectivesJson { get; set; } = "[]";
    public string OptimizationGoalsJson { get; set; } = "[]";
    public string BidStrategiesJson { get; set; } = "[]";
    public string PlacementEligibilityJson { get; set; } = "{}";
    public string AutomationFeaturesJson { get; set; } = "{}";
    public string ValidationSupportJson { get; set; } = "{}";
    public string ProductionAccessJson { get; set; } = "{}";
    public string ProbeEvidenceJson { get; set; } = "{}";
    public string SupportedValidationObjectsJson { get; set; } = "[]";
    public string ProviderFieldsVersion { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CheckedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public AdvertisingCapabilityState State { get; set; }
    public string? ProviderTraceId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }
}

public sealed class ConnectionDisconnectOperation : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? DestinationId { get; set; }
    public DisconnectMode Mode { get; set; } = DisconnectMode.PauseManaged;
    public DisconnectPhase Phase { get; set; } = DisconnectPhase.Requested;
    public Guid RequestedByUserId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ContinuingOrUnmonitoredSpendAcknowledgedAtUtc { get; set; }
    public Guid? EmergencyStopRecordId { get; set; }
    public DateTime? CredentialDisposedAtUtc { get; set; }
    public long? RouteTombstoneVersion { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? RecoveryInstruction { get; set; }
    public uint ConcurrencyToken { get; set; }
}

public sealed class ConnectionDisconnectTarget : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DisconnectOperationId { get; set; }
    public Guid OwnershipRecordId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string ProviderExternalId { get; set; } = string.Empty;
    public string DesiredState { get; set; } = "PAUSED";
    public Guid? ProviderOperationId { get; set; }
    public string? ReadBackState { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureCode { get; set; }
}

public sealed class AutonomyEnvelope : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? OfferId { get; set; }
    public decimal DailyCap { get; set; }
    public decimal? PeriodCap { get; set; }
    public string PeriodCapKind { get; set; } = "Monthly";
    public string Currency { get; set; } = "EGP";
    public decimal SafetyReservePercent { get; set; } = 15m;
    public decimal MaximumIncreasePercent { get; set; } = 20m;
    public int CooldownHours { get; set; } = 24;
    public string AllowedCountriesJson { get; set; } = "[]";
    public string HardIncludedGeoJson { get; set; } = "[]";
    public string HardExcludedGeoJson { get; set; } = "[]";
    public int HardMinimumAge { get; set; } = 18;
    public string HardRequiredLanguagesJson { get; set; } = "[]";
    public string HardCustomAudienceExclusionsJson { get; set; } = "[]";
    public string AudienceBoundaryHash { get; set; } = string.Empty;
    public string ReportingTimezoneIana { get; set; } = "Africa/Cairo";
    public string? TimezoneSource { get; set; }
    public DateTime? TimezoneSnapshotAtUtc { get; set; }
    public PlacementPolicy PlacementPolicy { get; set; } = PlacementPolicy.DynamicEligibleMeta;
    public int AttributionWindowDays { get; set; } = 7;
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public EnvelopeState State { get; set; } = EnvelopeState.Draft;
    public Guid AuthorizedByUserId { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public uint Version { get; set; }
    public string DefinitionHash { get; set; } = string.Empty;
    public uint ConcurrencyToken { get; set; }
}
