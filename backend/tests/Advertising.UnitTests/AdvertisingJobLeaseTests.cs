using Modules.Advertising.Jobs;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingJobLeaseTests
{
    [Fact]
    public void Redis_release_and_execution_require_the_exact_owner_token()
    {
        Assert.True(AdvertisingLeasePolicy.CanRelease("worker-a", "worker-a"));
        Assert.False(AdvertisingLeasePolicy.CanRelease("worker-b", "worker-a"));
        Assert.False(AdvertisingLeasePolicy.CanRelease(null, "worker-a"));
    }

    [Fact]
    public void Duplicate_dispatch_cannot_take_a_live_or_completed_database_bucket()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var lease = TimeSpan.FromMinutes(10);

        Assert.False(AdvertisingLeasePolicy.CanTakeCycle("Running", now.AddMinutes(-9), now, lease));
        Assert.False(AdvertisingLeasePolicy.CanTakeCycle("Completed", now.AddHours(-1), now, lease));
    }

    [Fact]
    public void Restart_can_recover_failed_or_expired_running_bucket_but_not_a_live_one()
    {
        var now = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);
        var lease = TimeSpan.FromMinutes(10);

        Assert.True(AdvertisingLeasePolicy.CanTakeCycle("Failed", now.AddMinutes(-1), now, lease));
        Assert.True(AdvertisingLeasePolicy.CanTakeCycle("Running", now.AddMinutes(-10), now, lease));
        Assert.True(AdvertisingLeasePolicy.CanTakeCycle(null, null, now, lease));
    }
}
