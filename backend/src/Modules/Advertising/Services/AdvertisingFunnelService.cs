namespace Modules.Advertising.Services;

public sealed record AdvertisingFunnel(string BusinessType, IReadOnlyList<string> Stages, string PrimaryOptimization,
    IReadOnlyList<string> FallbackOptimizations);

public static class AdvertisingFunnelService
{
    public static AdvertisingFunnel Infer(string businessType, string primaryOutcome) => primaryOutcome switch
    {
        "Purchase" or "EnrollmentPaid" => new(businessType, AdvertisingProfileExtractor.Funnel(businessType),
            "MESSAGING_PURCHASE_CONVERSION", ["QUALITY_LEAD", "CONVERSATIONS"]),
        "QualifiedLead" => new(businessType, AdvertisingProfileExtractor.Funnel(businessType),
            "QUALITY_LEAD", ["CONVERSATIONS"]),
        _ => new(businessType, AdvertisingProfileExtractor.Funnel(businessType), "CONVERSATIONS", [])
    };
}
