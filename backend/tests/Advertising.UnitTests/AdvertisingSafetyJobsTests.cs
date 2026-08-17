using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingSafetyJobsTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Finance_freezes_on_connection_tracking_or_emergency_failure(bool connection, bool tracking, bool stop) =>
        Assert.True(AdvertisingOperationalPolicy.MustFreezeFinance(connection, tracking, stop));

    [Fact]
    public void Spend_at_cap_triggers_stop_but_normal_spend_does_not()
    {
        Assert.True(AdvertisingOperationalPolicy.MustEmergencyStop(300m, 300m));
        Assert.False(AdvertisingOperationalPolicy.MustEmergencyStop(299.99m, 300m));
    }
}
