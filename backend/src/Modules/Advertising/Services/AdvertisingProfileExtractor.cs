using System.Text.RegularExpressions;

namespace Modules.Advertising.Services;

public sealed record ExtractedAdvertisingProfile(string OfferType, decimal? Price, string? Currency, string? Destination,
    bool Eligible, string BlockReason, IReadOnlyList<string> SourceCitations);
public sealed record ExtractedAdvertisingFact(string Name, string Value, Guid DocumentId, int DocumentVersion,
    decimal Confidence, DateTime ObservedAtUtc, bool IsContradictory, bool IsRequiredForLaunch, string Citation);

public static partial class AdvertisingProfileExtractor
{
    public static ExtractedAdvertisingProfile Extract(Guid documentId, int version, string content)
    {
        var prices = PriceRegex().Matches(content).Select(x => (Value: decimal.TryParse(x.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null, Currency: NormalizeCurrency(x.Groups[2].Value))).Where(x => x.Value is not null).Distinct().ToList();
        var destination = WhatsAppDestination(content) ?? UrlRegex().Match(content).Value; var type = InferType(content);
        var contradictory = prices.Select(x => new { x.Value, x.Currency }).Distinct().Count() > 1 && !HasTieredPricing(content);
        var eligible = !contradictory && !string.IsNullOrWhiteSpace(destination) && (prices.Count > 0 || type is "Service" or "Event");
        var reason = contradictory ? "ContradictoryPriceFacts" : string.IsNullOrWhiteSpace(destination) ? "MissingDestination" : !eligible ? "MissingPrice" : string.Empty;
        var citations = new List<string> { $"knowledge:{documentId:N}:v{version}" };
        return new(type, prices.FirstOrDefault().Value, prices.FirstOrDefault().Currency, string.IsNullOrWhiteSpace(destination) ? null : destination, eligible, reason, citations);
    }

    public static string[] Funnel(string type) => type switch
    {
        "SaaS" => ["Visit", "Signup", "TrialStarted", "SubscriptionStarted", "SubscriptionRenewed"],
        "Course" => ["Visit", "Lead", "EnrollmentPaid", "AttendanceConfirmed", "Completed"],
        "Product" => ["ViewContent", "AddToCart", "Checkout", "Purchase", "RepeatPurchase"],
        "Event" => ["Registration", "BookingConfirmed", "AttendanceConfirmed", "Purchase"],
        _ => ["Lead", "QualifiedLead", "BookingConfirmed", "AttendanceConfirmed", "Purchase"]
    };

    public static IReadOnlyList<ExtractedAdvertisingFact> ExtractFacts(Guid documentId, int version, string content, DateTime observedAtUtc)
    {
        var profile = Extract(documentId, version, content);
        var citation = profile.SourceCitations[0];
        var facts = new List<ExtractedAdvertisingFact>
        {
            new("OfferType", profile.OfferType, documentId, version, 0.95m, observedAtUtc, false, true, citation)
        };
        if (profile.Price is { } price) facts.Add(new("Price", price.ToString(System.Globalization.CultureInfo.InvariantCulture), documentId, version, 0.98m, observedAtUtc,
            profile.BlockReason == "ContradictoryPriceFacts", true, citation));
        if (profile.Currency is { } currency) facts.Add(new("Currency", currency, documentId, version, 0.98m, observedAtUtc, false, true, citation));
        if (profile.Destination is { } destination) facts.Add(new("Destination", destination, documentId, version, 0.99m, observedAtUtc, false, true, citation));
        return facts;
    }

    private static string InferType(string value) => value.Contains("كورس", StringComparison.OrdinalIgnoreCase) || value.Contains("دورة", StringComparison.OrdinalIgnoreCase) ? "Course"
        : value.Contains("اشتراك", StringComparison.OrdinalIgnoreCase) || value.Contains("SaaS", StringComparison.OrdinalIgnoreCase) ? "SaaS"
        : value.Contains("منتج", StringComparison.OrdinalIgnoreCase) ? "Product"
        : value.Contains("ندوة", StringComparison.OrdinalIgnoreCase) || value.Contains("حدث", StringComparison.OrdinalIgnoreCase) ? "Event" : "Service";
    private static string NormalizeCurrency(string value) => value.Contains('$') || value.Contains("USD", StringComparison.OrdinalIgnoreCase) ? "USD" : "EGP";
    private static bool HasTieredPricing(string content) => content.Contains("شهري", StringComparison.OrdinalIgnoreCase)
        || content.Contains("كاش", StringComparison.OrdinalIgnoreCase)
        || content.Contains("باقات", StringComparison.OrdinalIgnoreCase);
    private static string? WhatsAppDestination(string content)
    {
        if (!content.Contains("واتساب", StringComparison.OrdinalIgnoreCase)) return null;
        var phone = EgyptianPhoneRegex().Match(content).Groups[1].Value;
        return string.IsNullOrWhiteSpace(phone) ? null : $"https://wa.me/20{phone[1..]}";
    }
    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(جنيه|ج\.م|EGP|USD|\$)", RegexOptions.IgnoreCase)] private static partial Regex PriceRegex();
    [GeneratedRegex(@"(?<!\d)(01[0125]\d{8})(?!\d)")] private static partial Regex EgyptianPhoneRegex();
    [GeneratedRegex(@"https?://[^\s\]\)]+", RegexOptions.IgnoreCase)] private static partial Regex UrlRegex();
}
