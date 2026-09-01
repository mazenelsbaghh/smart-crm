using System.Text.Json;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public enum MetaOAuthStateDecision { Valid, Consumed, Expired }

public static class MetaOAuthStatePolicy
{
    public static MetaOAuthStateDecision Evaluate(DateTime createdAtUtc, bool consumed, DateTime nowUtc)
    {
        if (consumed) return MetaOAuthStateDecision.Consumed;
        return nowUtc - createdAtUtc > TimeSpan.FromMinutes(10) ? MetaOAuthStateDecision.Expired : MetaOAuthStateDecision.Valid;
    }
}

public sealed record CapabilityDecision(bool Ready, string Code)
{
    public static CapabilityDecision Pass() => new(true, "READY");
    public static CapabilityDecision Block(string code) => new(false, code);
}

public static class AdvertisingCapabilityPolicy
{
    public static CapabilityDecision CanProvisionWhatsApp(AdvertisingCapabilitySnapshot snapshot, DateTime nowUtc)
    {
        if (snapshot.State != AdvertisingCapabilityState.Healthy) return CapabilityDecision.Block("ADS_CAPABILITY_UNHEALTHY");
        if (snapshot.ExpiresAtUtc <= nowUtc) return CapabilityDecision.Block("ADS_CAPABILITY_STALE");
        var gatewayDestination = Contains(snapshot.PlacementEligibilityJson, "WHATSAPP_GATEWAY");
        if (!Contains(snapshot.OptimizationGoalsJson, "CONVERSATIONS") ||
            (!gatewayDestination && !Contains(snapshot.PlacementEligibilityJson, "whatsappDestinationEligible")) ||
            !Contains(snapshot.PlacementEligibilityJson, "automatic"))
            return CapabilityDecision.Block("ADS_WHATSAPP_CAPABILITY_UNPROVEN");
        return CapabilityDecision.Pass();
    }

    private static bool Contains(string json, string value) => json.Contains(value, StringComparison.OrdinalIgnoreCase);
}

public sealed record AutonomyEnvelopeDefinition(decimal DailyCap, decimal? PeriodCap, string PeriodCapKind, string Currency,
    string[] IncludedCountries, string[] ExcludedCountries, int MinimumAge, string[] RequiredLanguages,
    string[] CustomAudienceExclusions, string ReportingTimezoneIana);
public sealed record AutonomyEnvelopeValidation(bool IsValid, IReadOnlyList<string> Errors);
public sealed record AudienceAuthority(string[] Countries, int MinimumAge, string[] Languages);
public sealed record AudienceCandidate(string[] Countries, int MinimumAge, string[] Languages);

public static class AutonomyEnvelopePolicy
{
    public static AutonomyEnvelopeValidation Validate(AutonomyEnvelopeDefinition definition)
    {
        var errors = new List<string>();
        if (definition.DailyCap <= 0) errors.Add("ADS_DAILY_CAP_INVALID");
        if (definition.PeriodCap is not null && definition.PeriodCap < definition.DailyCap) errors.Add("ADS_PERIOD_CAP_BELOW_DAILY_CAP");
        if (definition.IncludedCountries.Length == 0) errors.Add("ADS_HARD_LOCATION_REQUIRED");
        if (definition.MinimumAge is < 18 or > 65) errors.Add("ADS_MINIMUM_AGE_INVALID");
        if (string.IsNullOrWhiteSpace(definition.Currency)
            || definition.Currency.Length != 3
            || !definition.Currency.All(character => character >= 'A' && character <= 'Z'))
            errors.Add("ADS_CURRENCY_INVALID");
        if (string.IsNullOrWhiteSpace(definition.ReportingTimezoneIana))
            errors.Add("ADS_TIMEZONE_INVALID");
        else
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(definition.ReportingTimezoneIana); }
            catch (TimeZoneNotFoundException) { errors.Add("ADS_TIMEZONE_INVALID"); }
            catch (InvalidTimeZoneException) { errors.Add("ADS_TIMEZONE_INVALID"); }
        }
        return new(errors.Count == 0, errors);
    }

    public static bool IsWithinAuthority(AudienceAuthority authority, AudienceCandidate candidate) =>
        candidate.MinimumAge >= authority.MinimumAge &&
        candidate.Countries.All(country => authority.Countries.Contains(country, StringComparer.OrdinalIgnoreCase)) &&
        candidate.Languages.All(language => authority.Languages.Contains(language, StringComparer.OrdinalIgnoreCase));

    public static string DefinitionHash(AutonomyEnvelopeDefinition definition) =>
        AdvertisingAuditService.HashState(JsonSerializer.Serialize(definition));
}

public static class AdvertisingDisconnectPolicy
{
    public static DisconnectMode NormalizeMode(DisconnectMode? mode) => mode ?? DisconnectMode.PauseManaged;

    public static bool CanLeaveRunning(DateTime? acknowledgedAtUtc, DateTime nowUtc) =>
        acknowledgedAtUtc is { } value && value <= nowUtc && nowUtc - value <= TimeSpan.FromMinutes(15);

    public static DisconnectPhase Next(DisconnectPhase current, bool allTargetsPaused) => current switch
    {
        DisconnectPhase.Requested => DisconnectPhase.AuthoritySuspended,
        DisconnectPhase.AuthoritySuspended => DisconnectPhase.ProtectiveStopQueued,
        DisconnectPhase.ProtectiveStopQueued => DisconnectPhase.ReconcilingPauses,
        DisconnectPhase.ReconcilingPauses when allTargetsPaused => DisconnectPhase.DisposingCredential,
        DisconnectPhase.ReconcilingPauses => DisconnectPhase.ReconcilingPauses,
        DisconnectPhase.DisposingCredential => DisconnectPhase.PublishingRouteTombstone,
        DisconnectPhase.PublishingRouteTombstone => DisconnectPhase.Completed,
        _ => current
    };
}
