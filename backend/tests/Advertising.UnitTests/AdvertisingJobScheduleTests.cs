using Modules.Advertising.Jobs;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingJobScheduleTests
{
    [Fact]
    public void Cairo_day_boundary_uses_the_offset_effective_on_that_date()
    {
        var summer = AdvertisingSchedulePolicy.DayStartUtc(
            new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc), "Africa/Cairo");
        var winter = AdvertisingSchedulePolicy.DayStartUtc(
            new DateTime(2026, 12, 19, 12, 0, 0, DateTimeKind.Utc), "Africa/Cairo");

        Assert.Equal(new DateTime(2026, 8, 18, 21, 0, 0, DateTimeKind.Utc), summer);
        Assert.Equal(new DateTime(2026, 12, 18, 22, 0, 0, DateTimeKind.Utc), winter);
    }

    [Fact]
    public void Ambiguous_fall_back_hour_maps_to_one_durable_bucket()
    {
        var firstOccurrence = AdvertisingSchedulePolicy.BucketStartUtc(
            new DateTime(2026, 10, 25, 0, 35, 0, DateTimeKind.Utc), "Europe/Berlin", TimeSpan.FromHours(1));
        var secondOccurrence = AdvertisingSchedulePolicy.BucketStartUtc(
            new DateTime(2026, 10, 25, 1, 35, 0, DateTimeKind.Utc), "Europe/Berlin", TimeSpan.FromHours(1));

        Assert.Equal(firstOccurrence, secondOccurrence);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), firstOccurrence);
    }

    [Fact]
    public void Project_local_daily_and_weekly_windows_do_not_depend_on_server_timezone()
    {
        var utc = new DateTime(2026, 8, 17, 3, 30, 0, DateTimeKind.Utc);

        Assert.True(AdvertisingSchedulePolicy.IsLocalHour(utc, "Africa/Cairo", 6));
        Assert.True(AdvertisingSchedulePolicy.IsLocalWeeklyHour(utc, "Africa/Cairo", DayOfWeek.Monday, 6));
        Assert.False(AdvertisingSchedulePolicy.IsLocalHour(utc, "UTC", 6));
    }

    [Fact]
    public void Empty_timezone_is_blocked_instead_of_fabricating_a_reporting_zone()
    {
        var utc = new DateTime(2026, 8, 17, 3, 30, 0, DateTimeKind.Utc);

        var error = Assert.Throws<Modules.Advertising.Services.AdvertisingException>(
            () => AdvertisingSchedulePolicy.IsLocalHour(utc, "", 6));
        Assert.Equal("ADS_REPORTING_TIMEZONE_UNKNOWN", error.Code);
    }

    [Fact]
    public void Lease_release_requires_the_exact_owner_token()
    {
        Assert.True(AdvertisingLeasePolicy.CanRelease("worker-a", "worker-a"));
        Assert.False(AdvertisingLeasePolicy.CanRelease("worker-b", "worker-a"));
        Assert.False(AdvertisingLeasePolicy.CanRelease(null, "worker-a"));
    }
}
