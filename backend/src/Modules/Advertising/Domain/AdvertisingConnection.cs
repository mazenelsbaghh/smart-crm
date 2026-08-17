using Shared.Domain;

namespace Modules.Advertising.Domain;

public sealed class AdvertisingConnection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "Facebook";
    public string? AdAccountExternalId { get; set; }
    public string? PageExternalId { get; set; }
    public string? DatasetExternalId { get; set; }
    public string? ProtectedAccessToken { get; set; }
    public string GrantedCapabilitiesJson { get; set; } = "[]";
    public string? AccountCurrency { get; set; }
    public string? AccountTimezone { get; set; }
    public AdvertisingConnectionState State { get; set; } = AdvertisingConnectionState.PendingSelection;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public uint Version { get; set; }
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
    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public EnvelopeState State { get; set; } = EnvelopeState.Draft;
    public Guid AuthorizedByUserId { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public uint Version { get; set; }
}
