using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.API;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class GroupBookingStatisticsTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("2026-10-16T12:00:00Z", "2026-10-15T21:00:00Z", "2026-10-16T21:00:00Z")]
    [InlineData("2026-04-24T12:00:00Z", "2026-04-23T22:00:00Z", "2026-04-24T21:00:00Z")]
    [InlineData("2026-10-29T12:00:00Z", "2026-10-28T21:00:00Z", "2026-10-29T22:00:00Z")]
    public async Task Counts_current_people_by_booking_date_with_Cairo_boundaries_and_course_tenant_isolation(
        string nowText, string startText, string endText)
    {
        var now = DateTime.Parse(nowText).ToUniversalTime();
        var start = DateTime.Parse(startText).ToUniversalTime();
        var end = DateTime.Parse(endText).ToUniversalTime();
        var projectId = Guid.NewGuid();
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext(Tenant(projectId));
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var regular = Group(projectId);
        var inactive = Group(projectId); inactive.IsActive = false;
        var intensive = Group(projectId); intensive.CourseType = CourseTypes.Intensive;
        var otherProject = Group(Guid.NewGuid());
        db.AddRange(regular, inactive, intensive, otherProject);
        var datedBookings = new[]
        {
            (Booking: AddBooking(db, regular, 1), At: start.AddTicks(-10)),
            (Booking: AddBooking(db, regular, 2), At: start),
            (Booking: AddBooking(db, regular, 3), At: end.AddTicks(-10)),
            (Booking: AddBooking(db, regular, 4), At: end),
            (Booking: AddBooking(db, inactive, 5), At: start.AddHours(2)),
            (Booking: AddBooking(db, intensive, 6), At: start.AddHours(2)),
            (Booking: AddBooking(db, otherProject, 7), At: start.AddHours(2))
        };
        datedBookings[4].Booking.IsPaid = true;
        datedBookings[4].Booking.IsAttended = true;
        var cancelled = AddBooking(db, regular, 8);
        await db.SaveChangesAsync();
        foreach (var (booking, at) in datedBookings) booking.CreatedAt = at;
        db.GroupAppointmentBookings.Remove(cancelled);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var query = new GroupBookingStatisticsQuery(db);
        var regularStats = await query.ReadAsync(projectId, CourseTypes.Regular, now);
        var allCourses = await query.ReadAsync(projectId, null, now);

        Assert.Equal(5, regularStats.TotalBooked);
        Assert.Equal(3, regularStats.BookedToday);
        Assert.Equal(6, allCourses.TotalBooked);
        Assert.Equal(4, allCourses.BookedToday);
        Assert.Equal(start, regularStats.DayStartUtc);
        Assert.Equal(end, regularStats.DayEndUtc);
        Assert.Equal(now, regularStats.AsOfUtc);
        Assert.Equal("Africa/Cairo", regularStats.Timezone);
        Assert.True(regularStats.TodayIncludesTransfers);
        Assert.Equal(0, (await query.ReadAsync(Guid.NewGuid(), null, now)).TotalBooked);
    }

    [Fact]
    public async Task Lightweight_list_omits_people_preserves_counts_and_expires_many_groups_in_a_bounded_batch()
    {
        var projectId = Guid.NewGuid();
        var tenant = Tenant(projectId);
        var commands = new CommandCounter();
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext(tenant, commands);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "Africa/Cairo" });
        var groups = Enumerable.Range(0, 20).Select(_ => Group(projectId)).ToArray();
        foreach (var group in groups)
        {
            group.FreeSessionDateTime = DateTime.UtcNow.AddDays(-3);
            db.Add(group);
            for (var index = 0; index < 10; index++) AddBooking(db, group, Array.IndexOf(groups, group) * 10 + index);
        }
        var foreignGroup = Group(Guid.NewGuid()); db.Add(foreignGroup);
        AddBooking(db, foreignGroup, 999);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Count = 0;
        var controller = Controller(db, tenant, projectId);

        var light = Assert.IsType<OkObjectResult>(await controller.GetGroups(CourseTypes.Regular, includeBookings: false));
        var lightJson = JsonSerializer.SerializeToElement(light.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(20, lightJson.GetArrayLength());
        Assert.All(lightJson.EnumerateArray(), group =>
        {
            Assert.Equal(10, group.GetProperty("bookedCount").GetInt32());
            Assert.Empty(group.GetProperty("bookings").EnumerateArray());
            Assert.False(group.GetProperty("isActive").GetBoolean());
        });
        Assert.InRange(commands.Count, 1, 5);
        var bookings = Assert.IsType<OkObjectResult>(await controller.GetGroupBookings(groups[0].Id));
        Assert.Equal(10, JsonSerializer.SerializeToElement(bookings.Value).GetArrayLength());
        Assert.IsType<NotFoundResult>(await controller.GetGroupBookings(foreignGroup.Id));
        Assert.IsType<ForbidResult>(await Controller(db, tenant, Guid.NewGuid()).GetGroupBookings(groups[0].Id));
        Assert.IsType<ForbidResult>(await Controller(db, tenant, Guid.NewGuid()).GetBookingStats());
        var legacy = Assert.IsType<OkObjectResult>(await controller.GetGroups());
        var legacyJson = JsonSerializer.SerializeToElement(legacy.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.All(legacyJson.EnumerateArray(), group => Assert.Equal(10, group.GetProperty("bookings").GetArrayLength()));
        Assert.True(lightJson.GetRawText().Length < legacyJson.GetRawText().Length / 3);
        Assert.Equal(0, await db.GroupAppointments.CountAsync(group => group.ProjectId == projectId && group.IsActive));
    }

    private static GroupAppointment Group(Guid projectId) => new()
    {
        ProjectId = projectId, Name = "مجموعة اختبار", Capacity = 1000,
        DateTime = DateTime.UtcNow.AddDays(30), CourseType = CourseTypes.Regular
    };

    private static GroupAppointmentBooking AddBooking(AppDbContext db, GroupAppointment group, int index)
    {
        var customer = new Customer { ProjectId = group.ProjectId, Name = $"طالب أو صديق {index}",
            PhoneNumber = $"2010{index:D8}", City = "الإسكندرية" };
        var booking = new GroupAppointmentBooking { ProjectId = group.ProjectId, GroupAppointmentId = group.Id,
            CustomerId = customer.Id, CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber };
        db.AddRange(customer, booking);
        return booking;
    }

    private static TenantContext Tenant(Guid projectId)
    {
        var tenant = new TenantContext(); tenant.SetProjectId(projectId); return tenant;
    }

    private static GroupAppointmentsController Controller(AppDbContext db, TenantContext tenant, Guid claimedProjectId) =>
        new(db, tenant, new ProjectAuthorizationService(), new GroupBookingCoordinator(db),
            DispatchProxy.Create<IHubContext<NotificationHub>, UnusedBoundary>(),
            DispatchProxy.Create<IConnectionMultiplexer, RedisBoundary>(), new WhatsAppAccountService(db))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("ProjectId", claimedProjectId.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test")) } }
        };

    private class UnusedBoundary : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Read should not call an external boundary.");
    }
    private class RedisBoundary : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IConnectionMultiplexer.GetDatabase)
            ? DispatchProxy.Create<IDatabase, UnusedBoundary>() : throw new InvalidOperationException("Unexpected Redis call.");
    }
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Count++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Count++; return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }
}
