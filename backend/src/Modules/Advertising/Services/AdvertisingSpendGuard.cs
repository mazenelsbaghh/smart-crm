using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record SpendAuthorityDecision(bool Allowed, decimal Remaining, decimal Exposure, string Code);

public static class AdvertisingSpendGuard
{
    public static decimal Exposure(BudgetPeriodLedger ledger)
    {
        var outstanding = Math.Max(0m, ledger.CommittedAmount - ledger.ReleasedAmount - ledger.ObservedSpend);
        var delayed = Math.Max(ledger.DelayedSpendEstimate, Math.Max(0m, ledger.ForecastSpend - ledger.ObservedSpend));
        return ledger.ObservedSpend + outstanding + delayed;
    }

    public static decimal RemainingAuthority(BudgetPeriodLedger ledger) => Math.Max(0m, ledger.UsableCap - Exposure(ledger));

    public static SpendAuthorityDecision CanApply(IEnumerable<BudgetPeriodLedger> ledgers, decimal requestedAmount)
    {
        var periods = ledgers.ToArray();
        if (requestedAmount <= 0 || periods.Length == 0) return new(false, 0m, 0m, "ADS_SPEND_AUTHORITY_MISSING");
        var remaining = periods.Min(RemainingAuthority);
        var exposure = periods.Max(Exposure);
        return requestedAmount <= remaining
            ? new(true, remaining - requestedAmount, exposure, "ADS_SPEND_AUTHORIZED")
            : new(false, remaining, exposure, "ADS_HARD_CAP_RISK");
    }
}
