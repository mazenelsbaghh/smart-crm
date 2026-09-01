using System.Text.Json;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaCampaignPayload(string Name, string Objective, string BuyingType, string Status,
    string SpecialAdCategoriesJson, string BidStrategy, decimal DailyBudget);
public sealed record MetaAdSetPayload(string Name, string OptimizationGoal, string BillingEvent, string BidStrategy,
    string DestinationType, string PromotedObjectJson, string TargetingJson, string Status, DateTime StartsAtUtc, DateTime? EndsAtUtc);
public sealed record MetaCreativePayload(string Name, string PageId, string WhatsAppPhoneNumber, string CallToAction,
    string AppDestination, string SourceType, string SourceExternalId, string PrimaryText, string? Headline, string? Description,
    string? MediaUrl = null);
public sealed record MetaPlanPayload(MetaCampaignPayload Campaign, MetaAdSetPayload AdSet, MetaCreativePayload Creative);

public static class MetaCampaignPlanMapper
{
    private static readonly string[] AllowedObjectives = ["OUTCOME_ENGAGEMENT", "OUTCOME_LEADS", "OUTCOME_SALES"];
    private static readonly string[] AllowedOptimizations = ["CONVERSATIONS", "QUALITY_LEAD", "MESSAGING_PURCHASE_CONVERSION"];

    public static MetaPlanPayload Map(CampaignPlan plan, AudienceStrategy audience, AuthorizedWhatsAppDestination destination,
        AdvertisingCreative creative, AdvertisingCreativeVariant? variant = null)
    {
        if (!AllowedObjectives.Contains(plan.Objective, StringComparer.Ordinal))
            throw new AdvertisingException("ADS_OBJECTIVE_NOT_MESSAGING_COMPATIBLE", "The objective is not eligible for WhatsApp messaging.");
        if (!AllowedOptimizations.Contains(plan.OptimizationGoal, StringComparer.Ordinal))
            throw new AdvertisingException("ADS_OPTIMIZATION_NOT_MESSAGING_COMPATIBLE", "The optimization is not eligible for WhatsApp messaging.");
        if (plan.DestinationId != destination.Id || destination.State != AuthorizedDestinationState.Eligible)
            throw new AdvertisingException("ADS_WHATSAPP_DESTINATION_DRIFT", "Plan destination is not the authorized WhatsApp destination.");
        if (plan.PlacementMode != PlacementPolicy.DynamicEligibleMeta)
            throw new AdvertisingException("ADS_ADVANTAGE_PLUS_REQUIRED", "Dynamic Advantage+ placement mode is required.");
        if (creative.EligibilityState != CreativeEligibility.Eligible)
            throw new AdvertisingException("ADS_CREATIVE_NOT_ELIGIBLE", "Creative source is not eligible.");

        var targeting = JsonSerializer.Serialize(new
        {
            geo_locations = new { countries = ParseArray(audience.IncludedGeoJson) },
            excluded_geo_locations = new { countries = ParseArray(audience.ExcludedGeoJson) },
            age_min = audience.MinimumAge,
            locales = ParseArray(audience.RequiredLanguagesJson),
            exclusions = ParseArray(audience.CustomAudienceExclusionsJson),
            targeting_automation = new { advantage_audience = 1 }
        });
        var promotedObject = JsonSerializer.Serialize(new
        {
            page_id = destination.PageExternalId,
            whatsapp_phone_number = destination.PhoneNumberExternalId
        });
        return new(
            new(plan.Name, plan.Objective, "AUCTION", "PAUSED",
                JsonSerializer.Serialize(plan.SpecialAdCategory is null ? Array.Empty<string>() : new[] { plan.SpecialAdCategory }),
                "LOWEST_COST_WITHOUT_CAP", plan.DailyBudget),
            new($"{plan.Name} · Audience", plan.OptimizationGoal, "IMPRESSIONS", "LOWEST_COST_WITHOUT_CAP",
                "WHATSAPP", promotedObject, targeting, "PAUSED", plan.StartsAtUtc, plan.EndsAtUtc),
            new($"{plan.Name} · Creative", destination.PageExternalId, destination.PhoneNumberExternalId,
                "WHATSAPP_MESSAGE", "WHATSAPP", creative.SourceType.ToString(), creative.SourceExternalId ?? creative.SourceStoragePath ?? string.Empty,
                variant?.PrimaryText ?? string.Empty, variant?.Headline, variant?.Description));
    }

    private static string[] ParseArray(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
}

public static class MetaProviderEquivalence
{
    public static IReadOnlyList<InvariantViolation> CompareWhatsAppAdSet(MetaAdSetPayload planned, IReadOnlyDictionary<string, string> effective)
    {
        var violations = new List<InvariantViolation>();
        Compare("destination_type", planned.DestinationType);
        Compare("optimization_goal", planned.OptimizationGoal);
        Compare("status", "PAUSED");
        if (!effective.TryGetValue("promoted_object", out var promoted) ||
            !ContainsEveryPromotedIdentity(planned.PromotedObjectJson, promoted))
            violations.Add(new("promoted_object", "ADS_WHATSAPP_IDENTITY_DRIFT", InvariantSeverity.Blocking,
                "Effective Page or WhatsApp phone differs from the approved destination."));
        if (effective.TryGetValue("targeting", out var targeting) &&
            (targeting.Contains("publisher_platforms", StringComparison.OrdinalIgnoreCase) || targeting.Contains("facebook_positions", StringComparison.OrdinalIgnoreCase)))
            violations.Add(new("targeting", "ADS_MANUAL_PLACEMENT_DRIFT", InvariantSeverity.Blocking, "Effective targeting contains stale manual placement fields."));
        return violations;

        void Compare(string field, string expected)
        {
            if (!effective.TryGetValue(field, out var value) || !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                violations.Add(new(field, "ADS_PROVIDER_FIELD_DRIFT", InvariantSeverity.Blocking, $"Effective {field} differs from the approved plan."));
        }
    }

    private static bool ContainsEveryPromotedIdentity(string plannedJson, string effectiveJson)
    {
        using var planned = JsonDocument.Parse(plannedJson);
        return planned.RootElement.EnumerateObject().All(property =>
            effectiveJson.Contains(property.Value.ToString(), StringComparison.Ordinal));
    }
}
