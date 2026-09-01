namespace Modules.Advertising.Services;

public sealed record CreativePeriodEvidence(long Impressions, decimal Frequency, decimal Ctr, decimal ConversionRate, decimal Cpa);
public sealed record CreativeFatigueDecision(string State, IReadOnlyList<string> Reasons);

public static class CreativeFatigueService
{
    public static CreativeFatigueDecision Evaluate(CreativePeriodEvidence prior, CreativePeriodEvidence current)
    {
        var reasons = new List<string>();
        if (current.Impressions < 3000 || prior.Impressions < 3000) return new("WAIT", ["ADS_WAIT_INSUFFICIENT_IMPRESSIONS"]);
        if (current.Frequency < 3m) reasons.Add("FREQUENCY_NOT_HIGH");
        if (!Declined(prior.Ctr, current.Ctr, .20m)) reasons.Add("CTR_NOT_DECLINING");
        if (!Declined(prior.ConversionRate, current.ConversionRate, .20m)) reasons.Add("CONVERSION_NOT_DECLINING");
        if (!Increased(prior.Cpa, current.Cpa, .20m)) reasons.Add("CPA_NOT_INCREASING");
        return reasons.Count == 0 ? new("FATIGUED", ["HIGH_FREQUENCY", "CTR_DECLINE", "CONVERSION_DECLINE", "CPA_INCREASE"]) : new("FRESH", reasons);
    }

    private static bool Declined(decimal prior, decimal current, decimal threshold) => prior > 0 && current <= prior * (1m - threshold);
    private static bool Increased(decimal prior, decimal current, decimal threshold) => prior > 0 && current >= prior * (1m + threshold);
}
