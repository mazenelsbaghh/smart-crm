using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingSpendGuardTests
{
    [Fact]
    public void Observed_outstanding_and_delayed_spend_are_all_reserved_before_new_authority()
    {
        var ledger = new BudgetPeriodLedger
        {
            UsableCap = 90m, ObservedSpend = 40m, CommittedAmount = 60m,
            ReleasedAmount = 5m, DelayedSpendEstimate = 12m, ForecastSpend = 48m
        };

        Assert.Equal(67m, AdvertisingSpendGuard.Exposure(ledger));
        Assert.Equal(23m, AdvertisingSpendGuard.RemainingAuthority(ledger));
        Assert.False(AdvertisingSpendGuard.CanApply([ledger], 24m).Allowed);
        Assert.True(AdvertisingSpendGuard.CanApply([ledger], 23m).Allowed);
    }

    [Fact]
    public void Tightest_of_daily_and_total_periods_controls_new_spend()
    {
        var daily = new BudgetPeriodLedger { UsableCap = 90m, CommittedAmount = 20m };
        var total = new BudgetPeriodLedger { UsableCap = 400m, CommittedAmount = 390m };

        var decision = AdvertisingSpendGuard.CanApply([daily, total], 11m);

        Assert.False(decision.Allowed);
        Assert.Equal(10m, decision.Remaining);
        Assert.Equal("ADS_HARD_CAP_RISK", decision.Code);
    }

    [Fact]
    public void Delayed_provider_reporting_and_abnormal_forecast_fail_closed()
    {
        var ledger = new BudgetPeriodLedger { UsableCap = 100m, ObservedSpend = 80m,
            DelayedSpendEstimate = 15m, ForecastSpend = 106m };

        Assert.Equal(106m, AdvertisingSpendGuard.Exposure(ledger));
        Assert.True(AdvertisingOperationalPolicy.MustEmergencyStop(AdvertisingSpendGuard.Exposure(ledger), ledger.UsableCap));
        Assert.True(AdvertisingOperationalPolicy.IsAbnormalForecast(ledger.ForecastSpend, ledger.UsableCap));
        Assert.False(AdvertisingOperationalPolicy.IsAbnormalForecast(104.99m, ledger.UsableCap));
    }
}
