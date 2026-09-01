namespace Modules.Advertising.Services;

public sealed record AdvertisingFactValidation(bool Eligible, IReadOnlyList<string> BlockingReasons);

public static class AdvertisingFactValidator
{
    private static readonly string[] ProhibitedClaims = ["مضمون 100%", "نتيجة مضمونة", "guaranteed income", "cure guaranteed"];

    public static AdvertisingFactValidation Validate(IEnumerable<ExtractedAdvertisingFact> facts, string proposedCopy = "")
    {
        var values = facts.ToArray();
        var errors = new List<string>();
        if (values.Any(fact => fact.IsRequiredForLaunch && fact.Confidence < 0.8m)) errors.Add("ADS_REQUIRED_FACT_LOW_CONFIDENCE");
        if (values.Any(fact => fact.IsContradictory)) errors.Add("ADS_COMMERCIAL_FACT_CONTRADICTORY");
        if (values.Where(fact => fact.IsRequiredForLaunch).Any(fact => string.IsNullOrWhiteSpace(fact.Citation))) errors.Add("ADS_FACT_CITATION_REQUIRED");
        if (!values.Any(fact => fact.Name == "Destination")) errors.Add("ADS_WHATSAPP_DESTINATION_FACT_REQUIRED");
        if (ProhibitedClaims.Any(claim => proposedCopy.Contains(claim, StringComparison.OrdinalIgnoreCase))) errors.Add("ADS_PROHIBITED_CLAIM");
        return new(errors.Count == 0, errors.Distinct().ToArray());
    }

    public static bool ProposedValueMatchesSource(string name, string proposedValue, IEnumerable<ExtractedAdvertisingFact> facts) =>
        facts.Any(fact => string.Equals(fact.Name, name, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(fact.Value, proposedValue, StringComparison.OrdinalIgnoreCase) && !fact.IsContradictory);
}
