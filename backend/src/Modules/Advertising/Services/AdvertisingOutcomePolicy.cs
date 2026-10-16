using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public static class AdvertisingOutcomePolicy
{
    public static readonly string[] RevenueEvents =
        ["Purchase", "SubscriptionStarted", "SubscriptionRenewed", "EnrollmentPaid", "DealWon"];

    public static bool IsVerified(CanonicalConversion outcome) =>
        outcome.TruthState is "Verified" or "Corrected"
        && outcome.State != ConversionState.Suppressed && outcome.CorrectionState != CorrectionState.PendingBase;
}
