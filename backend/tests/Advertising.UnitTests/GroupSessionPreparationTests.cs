using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GroupSessionPreparationTests
{
    [Fact]
    public void Revising_the_free_session_restores_its_cancelled_invite_and_keeps_old_preparation_cancelled()
    {
        var group = new GroupAppointment { ProjectId = Guid.NewGuid(), IsActive = true,
            DateTime = DateTime.UtcNow.AddDays(10), FreeSessionDateTime = DateTime.UtcNow.AddDays(4) };
        var booking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id, CustomerId = Guid.NewGuid() };
        var invite = new FollowUp { ProjectId = group.ProjectId, GroupAppointmentBookingId = booking.Id,
            AppointmentTime = group.DateTime, Type = "AppointmentReminder", Status = "Cancelled" };
        var oldVideo = new FollowUp { ProjectId = group.ProjectId, GroupAppointmentBookingId = booking.Id,
            AppointmentTime = group.FreeSessionDateTime.Value.AddDays(-1), Type = GroupSessionPreparationScheduler.VideoType, Status = "Cancelled" };

        GroupBookingFollowUpLifecycle.RestoreScheduled(group, [booking], [invite, oldVideo]);

        Assert.Equal("Pending", invite.Status);
        Assert.Equal("Cancelled", oldVideo.Status);
    }
    [Fact]
    public async Task Cached_preparation_reconciliation_does_not_rescan_all_tracked_entities()
    {
        // 2026-10-04: checking DbSet.Local before the cached IDs rescanned every tracked booking.
        using var services = new ServiceCollection().BuildServiceProvider();
        var detectionLog = new ChangeDetectionLog();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Debug).AddProvider(detectionLog));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).UseLoggerFactory(loggerFactory).Options,
            new TenantContext(), services);
        var now = DateTime.UtcNow;
        var group = new GroupAppointment { ProjectId = Guid.NewGuid(), DateTime = now.AddDays(5) };
        var booking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id,
            CustomerId = Guid.NewGuid(), CustomerName = "عميل" };
        var templates = new FallbackMessageSettings { GroupPreparationVideo = "فيديو", GroupPreparationSite = "سايت" };
        var knownIds = new HashSet<Guid>();
        var scheduler = new GroupSessionPreparationScheduler(db, knownIds);
        var accountId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        await scheduler.ScheduleAsync(group, booking, accountId, templates, inviteId, now);
        await db.SaveChangesAsync();
        detectionLog.Detections = 0;

        await scheduler.ScheduleAsync(group, booking, accountId, templates, inviteId, now);

        Assert.Equal(0, detectionLog.Detections);
        Assert.Equal(2, await db.FollowUps.IgnoreQueryFilters().CountAsync());
    }

    private sealed class ChangeDetectionLog : ILoggerProvider, ILogger
    {
        public int Detections { get; set; }
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == CoreEventId.DetectChangesStarting.Id) Detections++;
        }
    }

    [Theory]
    [InlineData("online")]
    [InlineData("offline")]
    public async Task Both_modes_schedule_video_then_website_before_the_free_session_without_duplicates(string mode)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = CreateDb(services);
        var now = new DateTime(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);
        var session = now.AddDays(6);
        var group = new GroupAppointment { ProjectId = Guid.NewGuid(), Mode = mode, DateTime = session.AddDays(7), FreeSessionDateTime = session };
        var booking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id, CustomerId = Guid.NewGuid(), CustomerName = "عميل" };
        var templates = new FallbackMessageSettings { GroupPreparationVideo = "أهلاً يا {customerName}، فيديو التحضير: https://example.test/video", GroupPreparationSite = "أهلاً، السايت لتحديد المستوى: https://example.test/assessment" };
        var inviteId = Guid.NewGuid();
        var scheduler = new GroupSessionPreparationScheduler(db, []);

        await scheduler.ScheduleAsync(group, booking, Guid.NewGuid(), templates, inviteId, now);
        var secondBooking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id, CustomerId = Guid.NewGuid() };
        await scheduler.ScheduleAsync(group, secondBooking, Guid.NewGuid(), templates, inviteId, now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        scheduler = new GroupSessionPreparationScheduler(db,
            (await db.FollowUps.IgnoreQueryFilters().Select(followUp => followUp.Id).ToListAsync()).ToHashSet());
        await scheduler.ScheduleAsync(group, booking, Guid.NewGuid(), templates, inviteId, now);
        await db.SaveChangesAsync();

        var messages = await db.FollowUps.IgnoreQueryFilters().OrderBy(followUp => followUp.DueDate).ToListAsync();
        Assert.Equal(4, messages.Count);
        messages = messages.Where(followUp => followUp.CustomerId == booking.CustomerId).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(session.AddDays(-2), messages[0].DueDate);
        Assert.Equal(session.AddDays(-1), messages[1].DueDate);
        Assert.Equal(inviteId, messages[0].DependsOnFollowUpId);
        Assert.Equal(messages[0].Id, messages[1].DependsOnFollowUpId);
        Assert.All(messages, followUp => { Assert.Equal(session, followUp.AppointmentTime); Assert.Equal("Exact", followUp.Tone); });
        Assert.Contains("عميل", messages[0].Notes);
    }

    [Fact]
    public async Task Late_booking_sends_preparation_in_order_and_never_schedules_after_an_expired_session()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = CreateDb(services);
        var now = DateTime.UtcNow;
        var group = new GroupAppointment { ProjectId = Guid.NewGuid(), DateTime = now.AddHours(3) };
        var booking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id, CustomerId = Guid.NewGuid() };
        var templates = new FallbackMessageSettings { GroupPreparationVideo = "فيديو", GroupPreparationSite = "سايت" };
        var inviteId = Guid.NewGuid();
        var scheduler = new GroupSessionPreparationScheduler(db);
        await scheduler.ScheduleAsync(group, booking, Guid.NewGuid(), templates, inviteId, now);
        await db.SaveChangesAsync();
        Assert.All(await db.FollowUps.IgnoreQueryFilters().ToListAsync(), followUp => Assert.Equal(now, followUp.DueDate));
        group.DateTime = now.AddMinutes(-1);
        await scheduler.ScheduleAsync(group, booking, Guid.NewGuid(), templates, inviteId, now);
        Assert.Equal(2, await db.FollowUps.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void Customer_replies_or_payment_only_remove_sales_followups_and_preserve_session_preparation()
    {
        var bookingId = Guid.NewGuid();
        var invite = new FollowUp { GroupAppointmentBookingId = bookingId, Type = "AppointmentReminder" };
        var video = new FollowUp { GroupAppointmentBookingId = bookingId, Type = GroupSessionPreparationScheduler.VideoType };
        var site = new FollowUp { GroupAppointmentBookingId = bookingId, Type = GroupSessionPreparationScheduler.SiteType };
        var sales = new FollowUp { Type = "Nurturing" };

        Assert.Equal([sales], GroupSessionPreparationScheduler.ExcludePreparation(new[] { invite, video, site, sales }.AsQueryable()).ToArray());
        Assert.All(new[] { invite, video, site }, followUp => Assert.True(GroupSessionPreparationScheduler.IsPreparation(followUp)));
        Assert.False(GroupSessionPreparationScheduler.IsPreparation(sales));
    }

    [Theory]
    [InlineData("AppointmentReminder")]
    [InlineData("GroupPreparationVideo")]
    [InlineData("GroupPreparationSite")]
    public void Preparation_survives_disconnect_and_resumes_after_reconnection(string type)
    {
        var now = DateTime.UtcNow;
        var followUp = new FollowUp { Type = type, GroupAppointmentBookingId = Guid.NewGuid(),
            CreatedAt = now.AddDays(-3), DueDate = now.AddHours(-1), Status = "Processing" };
        var disconnected = new WhatsAppGatewaySessionStatus("Disconnected", null, null);
        Assert.False(FollowUpScheduler.CanDispatchInCurrentConnection(followUp, disconnected));
        FollowUpScheduler.HandleUnavailableFollowUpConnection(followUp, now);
        Assert.Equal("Pending", followUp.Status);

        var reconnected = new WhatsAppGatewaySessionStatus("Connected", "201000000001", null) { ConnectedAt = now };
        Assert.True(FollowUpScheduler.CanDispatchInCurrentConnection(followUp, reconnected));
    }

    [Fact]
    public void Ordinary_sales_followups_from_a_previous_connection_still_expire()
    {
        var now = DateTime.UtcNow;
        var followUp = new FollowUp { Type = "Nurturing", CreatedAt = now.AddDays(-3), DueDate = now.AddHours(-1) };
        var reconnected = new WhatsAppGatewaySessionStatus("Connected", "201000000001", null) { ConnectedAt = now };
        Assert.False(FollowUpScheduler.CanDispatchInCurrentConnection(followUp, reconnected));
        FollowUpScheduler.HandleUnavailableFollowUpConnection(followUp, now);
        Assert.Equal("Bypassed", followUp.Status);
    }

    private static AppDbContext CreateDb(IServiceProvider services) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new TenantContext(), services);
}
