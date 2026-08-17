namespace Modules.Advertising.Services;

public sealed class AdvertisingOptions
{
    public const string SectionName = "Advertising";
    public decimal SafetyReservePercent { get; init; } = 15m;
    public decimal AbnormalSpendPercent { get; init; } = 105m;
    public int TrackingStaleMinutes { get; init; } = 30;
    public MetaOptions Meta { get; init; } = new();
}

public sealed class MetaOptions
{
    public bool UseMock { get; init; }
    public string GraphApiVersion { get; init; } = "v25.0";
    public string AppId { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
    public string OAuthRedirectUri { get; init; } = string.Empty;
}
