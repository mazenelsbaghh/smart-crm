namespace Modules.Advertising.Services;

public sealed class AdvertisingOptions
{
    public const string SectionName = "Advertising";

    public bool Enabled { get; init; }
    public bool AllowRealActivation { get; init; }
    public decimal SafetyReservePercent { get; init; } = 15m;
    public decimal AbnormalSpendPercent { get; init; } = 105m;
    public TrackingOptions Tracking { get; init; } = new();
    public WhatsAppCloudOptions WhatsAppCloud { get; init; } = new();
    public MetaOptions Meta { get; init; } = new();
}

public sealed class MetaOptions
{
    public bool UseMock { get; init; }
    public string GraphApiVersion { get; init; } = "v26.0";
    public string AppId { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
    public string OAuthRedirectUri { get; init; } = string.Empty;
}

public sealed class TrackingOptions
{
    public int PolicyVersion { get; init; } = 1;
    public int StaleMinutes { get; init; } = 30;
    public int MinimumConversationDenominator { get; init; } = 20;
    public decimal MinimumReferralCoverage { get; init; } = 0.95m;
    public decimal MinimumExactMatchRate { get; init; } = 0.90m;
    public decimal MinimumDeliveryAcceptanceRate { get; init; } = 0.95m;
    public decimal MaximumCorrectionRate { get; init; } = 0.20m;
}

public sealed class WhatsAppCloudOptions
{
    public string VerifyToken { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
    public int MaximumWebhookBodyBytes { get; init; } = 1_048_576;
}

public sealed record AdvertisingStartupValidationError(string Code, string Message);

public static class AdvertisingStartupValidator
{
    public const string SupportedGraphApiVersion = "v26.0";

    public static IReadOnlyList<AdvertisingStartupValidationError> Validate(
        AdvertisingOptions options,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            return [];

        var errors = new List<AdvertisingStartupValidationError>();
        if (!string.Equals(options.Meta.GraphApiVersion, SupportedGraphApiVersion, StringComparison.Ordinal))
        {
            errors.Add(new(
                "ADS_GRAPH_VERSION_UNSUPPORTED",
                $"Advertising supports only Meta Graph {SupportedGraphApiVersion}."));
        }

        var permitsMock = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);

        if (options.Meta.UseMock)
        {
            if (!permitsMock)
            {
                errors.Add(new(
                    "ADS_META_MOCK_NOT_ALLOWED",
                    "The fake Meta provider is allowed only in Development or Test."));
            }

            return errors;
        }

        if (string.IsNullOrWhiteSpace(options.Meta.AppId))
            errors.Add(new("ADS_META_APP_ID_MISSING", "Meta App ID is required when Advertising is enabled."));

        if (string.IsNullOrWhiteSpace(options.Meta.AppSecret))
            errors.Add(new("ADS_META_APP_SECRET_MISSING", "Meta App Secret is required when Advertising is enabled."));

        if (!IsValidProductionCallback(options.Meta.OAuthRedirectUri))
        {
            errors.Add(new(
                "ADS_META_OAUTH_REDIRECT_INVALID",
                "Meta OAuth redirect must be an absolute HTTPS URL ending in /api/ad-manager/meta/oauth/callback."));
        }

        return errors;
    }

    private static bool IsValidProductionCallback(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath, "/api/ad-manager/meta/oauth/callback", StringComparison.Ordinal);
}
