using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SalesFollowUpTimingTests
{
    [Theory]
    [InlineData("Completed", 60, true, "Pending")]
    [InlineData("Completed", 120, false, "Processing")]
    [InlineData("Processing", 60, true, "Pending")]
    [InlineData("DeliveryUnknown", 60, true, "DeliveryUnknown")]
    [InlineData("Cancelled", 0, false, "Processing")]
    public void Late_predecessor_never_collapses_the_selected_gap(string status, int secondsSinceSend,
        bool deferred, string expectedStatus)
    {
        var now = new DateTime(2026, 9, 9, 16, 0, 0, DateTimeKind.Utc);
        var predecessor = new FollowUp { Status = status, DueDate = now.AddMinutes(-10),
            SentAtUtc = status == "Completed" ? now.AddSeconds(-secondsSinceSend) : null,
            UpdatedAt = now.AddMinutes(-10) };
        var followUp = new FollowUp { Status = "Processing", DueDate = now.AddMinutes(-9),
            DependsOnFollowUpId = predecessor.Id, DispatchIntervalSeconds = 90 };

        Assert.Equal(deferred, PlannedFollowUpTiming.DeferUntilPredecessor(followUp, predecessor, now));
        Assert.Equal(expectedStatus, followUp.Status);
        if (status == "Completed" && deferred) Assert.Equal(now.AddSeconds(30), followUp.DueDate);
        if (status == "Processing") Assert.True(followUp.DueDate > now);
    }

    [Theory]
    [InlineData(0, 30, 60, 1)]
    [InlineData(11, 30, 60, 1)]
    [InlineData(10, 60, 30, 1)]
    [InlineData(10, 0, 60, 1)]
    [InlineData(10, 1, 3601, 1)]
    [InlineData(10, 30, 60, 11)]
    [InlineData(10, 30, 60, 0)]
    public void Invalid_options_cannot_create_a_dispatch_plan(int count, int min, int max, int days)
    {
        Assert.NotNull(SalesFollowUpSchedule.ValidationError(new(count, min, max, days), 10, FollowUpPlanAction.Schedule));
    }

    [Fact]
    public void Daily_distribution_preserves_local_start_time_when_daylight_saving_ends()
    {
        var now = new DateTime(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc);
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var slots = SalesFollowUpSchedule.Slots(new(3, 60, 60, 3), FollowUpPlanAction.Schedule, now, timezone.Id);

        Assert.Equal(new[] { 1, 2, 3 }, slots.Select(s => TimeZoneInfo.ConvertTimeFromUtc(s.DueAtUtc, timezone).Day));
        Assert.All(slots, s => Assert.Equal(10, TimeZoneInfo.ConvertTimeFromUtc(s.DueAtUtc, timezone).Hour));
        Assert.Equal(now.AddHours(25), slots[0].DueAtUtc);
    }
}
