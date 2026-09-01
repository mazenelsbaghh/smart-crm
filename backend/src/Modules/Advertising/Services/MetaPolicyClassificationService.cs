namespace Modules.Advertising.Services;

public sealed record MetaPolicyClassification(string? SpecialAdCategory, bool Resolved, IReadOnlyList<string> Reasons);

public static class MetaPolicyClassificationService
{
    public static MetaPolicyClassification Classify(string offerName, string allowedClaimsJson)
    {
        var text = $"{offerName} {allowedClaimsJson}";
        if (Contains(text, "وظيفة", "توظيف", "job", "employment")) return new("EMPLOYMENT", true, ["EMPLOYMENT_OFFER"]);
        if (Contains(text, "عقار", "سكن", "housing", "real estate")) return new("HOUSING", true, ["HOUSING_OFFER"]);
        if (Contains(text, "قرض", "ائتمان", "credit", "loan")) return new("CREDIT", true, ["CREDIT_OFFER"]);
        if (Contains(text, "انتخابات", "سياسي", "politic", "election")) return new("ISSUES_ELECTIONS_POLITICS", true, ["POLITICAL_OFFER"]);
        return new(null, true, ["NO_SPECIAL_CATEGORY_EVIDENCE"]);
    }

    private static bool Contains(string value, params string[] tokens) => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}
