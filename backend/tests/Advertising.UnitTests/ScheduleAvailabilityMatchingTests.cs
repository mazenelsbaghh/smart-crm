using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ScheduleAvailabilityMatchingTests
{
    [Theory]
    [InlineData(ScheduleAvailabilityWindows.NoonToFour, 12, true)]
    [InlineData(ScheduleAvailabilityWindows.NoonToFour, 16, false)]
    [InlineData(ScheduleAvailabilityWindows.FourToEight, 16, true)]
    [InlineData(ScheduleAvailabilityWindows.FourToEight, 20, false)]
    [InlineData(ScheduleAvailabilityWindows.EightToMidnight, 20, true)]
    [InlineData(ScheduleAvailabilityWindows.EightToMidnight, 23, true)]
    public void Time_windows_use_non_overlapping_boundaries(string window, int hour, bool expected)
    {
        Assert.Equal(expected, ScheduleAvailabilityWindows.ContainsHour(window, hour));
    }

    [Theory]
    [InlineData(ScheduleAvailabilityHorizons.NextWeek, "2026-09-09")]
    [InlineData(ScheduleAvailabilityHorizons.NextMonth, "2026-10-02")]
    [InlineData(ScheduleAvailabilityHorizons.NextThreeMonths, "2026-12-02")]
    public void Relative_horizons_are_calculated_without_customer_dates(
        string horizon,
        string expectedDate)
    {
        var start = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);

        var deadline = ScheduleAvailabilityHorizons.DeadlineUtc(horizon, start);

        Assert.Equal(DateTime.Parse(expectedDate), deadline?.Date);
    }

    [Fact]
    public async Task Available_group_queues_one_notification_for_matching_customer_window()
    {
        var projectId = Guid.NewGuid();
        var customer = new Customer
        {
            ProjectId = projectId,
            PhoneNumber = "201000000001",
            Name = "عميل",
            City = "القاهرة"
        };
        var preference = new ScheduleAvailabilityPreference
        {
            ProjectId = projectId,
            CustomerId = customer.Id,
            Channel = "WhatsApp",
            TimeWindow = ScheduleAvailabilityWindows.FourToEight,
            AvailabilityHorizon = ScheduleAvailabilityHorizons.AnyTime
        };
        var group = new GroupAppointment
        {
            ProjectId = projectId,
            Name = "مجموعة مسائية",
            DateTime = DateTime.UtcNow.Date.AddDays(1).AddHours(14),
            Capacity = 10,
            IsActive = true
        };
        await using var db = Context(projectId);
        db.AddRange(customer, preference, group, new ProjectSettings
        {
            ProjectId = projectId,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var scheduler = new FollowUpScheduler(services);

        await scheduler.MatchScheduleAvailabilityPreferencesJobAsync();
        await scheduler.MatchScheduleAvailabilityPreferencesJobAsync();

        var notification = Assert.Single(db.FollowUps);
        Assert.Equal("Queued", preference.Status);
        Assert.Equal(group.Id, preference.MatchedGroupAppointmentId);
        Assert.Equal(notification.Id, preference.NotificationFollowUpId);
        Assert.Contains("من ٤ عصرًا إلى ٨ مساءً", notification.Notes);
    }

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }
}
