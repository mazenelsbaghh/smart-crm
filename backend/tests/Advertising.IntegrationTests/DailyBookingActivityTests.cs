using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application.Services;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DailyBookingActivityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Booking_day_counts_include_older_customers_without_changing_entry_cohort_counts()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "Africa/Cairo" });
        var start = new DateTime(2026, 9, 6, 21, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(2);
        var datedEntities = new List<(AuditableEntity Entity, DateTime CreatedAt)>();
        var bookingScenarios = new[]
        {
            (Project: projectId, BookedAt: start.AddDays(1), EnteredAt: start.AddHours(10)),
            (Project: projectId, BookedAt: start.AddDays(1).AddMinutes(-1), EnteredAt: start.AddDays(-40)),
            (Project: projectId, BookedAt: end.AddMinutes(-1), EnteredAt: start.AddDays(-40)),
            (Project: projectId, BookedAt: end, EnteredAt: start.AddDays(-40)),
            (Project: Guid.NewGuid(), BookedAt: start.AddDays(1), EnteredAt: start.AddDays(-40))
        };
        foreach (var scenario in bookingScenarios)
        {
            var customer = new Customer { ProjectId = scenario.Project, Name = "طالب", City = "القاهرة",
                PhoneNumber = $"01{Random.Shared.NextInt64(100000000, 999999999)}" };
            var appointment = new GroupAppointment { ProjectId = scenario.Project, Name = "مجموعة",
                DateTime = end.AddDays(10), Capacity = 10 };
            var booking = new GroupAppointmentBooking { ProjectId = scenario.Project,
                GroupAppointmentId = appointment.Id, CustomerId = customer.Id,
                CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber };
            var conversation = new Conversation { ProjectId = scenario.Project, CustomerId = customer.Id,
                LastMessageTimestamp = scenario.EnteredAt };
            db.AddRange(customer, appointment, booking, conversation);
            datedEntities.Add((booking, scenario.BookedAt));
            datedEntities.Add((conversation, scenario.EnteredAt));
            if (scenario.EnteredAt > start)
            {
                var secondChannel = new Conversation { ProjectId = scenario.Project, CustomerId = customer.Id,
                    Channel = "Messenger", LastMessageTimestamp = scenario.EnteredAt };
                db.Add(secondChannel);
                datedEntities.Add((secondChannel, scenario.EnteredAt));
            }
        }
        await db.SaveChangesAsync();
        foreach (var (entity, createdAt) in datedEntities) entity.CreatedAt = createdAt;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new SalesIntelligenceService(db, null!, null!);

        var dashboard = await service.GetDashboardAsync(projectId, start, end, CancellationToken.None);

        Assert.Equal(2, dashboard.TotalConversations);
        Assert.Collection(dashboard.Daily,
            day =>
            {
                Assert.Equal("2026-09-07", day.Date);
                Assert.Equal(2, day.NewConversations);
                Assert.Equal(2, day.Booked);
                Assert.Equal(1, day.BookedOnDate);
            },
            day =>
            {
                Assert.Equal("2026-09-08", day.Date);
                Assert.Equal(0, day.NewConversations);
                Assert.Equal(0, day.Booked);
                Assert.Equal(2, day.BookedOnDate);
            });

        var bookingsOnly = await service.GetDashboardAsync(projectId, start.AddDays(1), end, CancellationToken.None);
        Assert.Equal(0, bookingsOnly.TotalConversations);
        var bookingDay = Assert.Single(bookingsOnly.Daily);
        Assert.Equal("2026-09-08", bookingDay.Date);
        Assert.Equal(2, bookingDay.BookedOnDate);
        Assert.Equal(0, bookingDay.Booked);
    }
}
