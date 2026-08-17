using System.Text.RegularExpressions;

namespace Modules.Advertising.Services;

public sealed record ExtractedAdvertisingProfile(string OfferType, decimal? Price, string? Currency, string? Destination,
    bool Eligible, string BlockReason, IReadOnlyList<string> SourceCitations);

public static partial class AdvertisingProfileExtractor
{
    public static ExtractedAdvertisingProfile Extract(Guid documentId, int version, string content)
    {
        var prices = PriceRegex().Matches(content).Select(x => (Value: decimal.TryParse(x.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : (decimal?)null, Currency: NormalizeCurrency(x.Groups[2].Value))).Where(x => x.Value is not null).Distinct().ToList();
        var destination = UrlRegex().Match(content).Value; var type = InferType(content);
        var contradictory = prices.Select(x => new { x.Value, x.Currency }).Distinct().Count() > 1;
        var eligible = !contradictory && !string.IsNullOrWhiteSpace(destination) && (prices.Count == 1 || type is "Service" or "Event");
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

    private static string InferType(string value) => value.Contains("كورس", StringComparison.OrdinalIgnoreCase) || value.Contains("دورة", StringComparison.OrdinalIgnoreCase) ? "Course"
        : value.Contains("اشتراك", StringComparison.OrdinalIgnoreCase) || value.Contains("SaaS", StringComparison.OrdinalIgnoreCase) ? "SaaS"
        : value.Contains("منتج", StringComparison.OrdinalIgnoreCase) ? "Product"
        : value.Contains("ندوة", StringComparison.OrdinalIgnoreCase) || value.Contains("حدث", StringComparison.OrdinalIgnoreCase) ? "Event" : "Service";
    private static string NormalizeCurrency(string value) => value.Contains('$') || value.Contains("USD", StringComparison.OrdinalIgnoreCase) ? "USD" : "EGP";
    [GeneratedRegex(@"(?<!\d)(\d+(?:[.,]\d+)?)\s*(جنيه|ج\.م|EGP|USD|\$)", RegexOptions.IgnoreCase)] private static partial Regex PriceRegex();
    [GeneratedRegex(@"https?://[^\s\]\)]+", RegexOptions.IgnoreCase)] private static partial Regex UrlRegex();
}
