using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class GroupStaffAutomationTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(GroupBookingOrigin.Manual)]
    [InlineData(GroupBookingOrigin.Public)]
    [InlineData(GroupBookingOrigin.Ai)]
    public async Task Confirmed_booking_queues_once_and_delivery_retries_with_same_key(GroupBookingOrigin origin)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Staff test" };
        tenant.SetProjectId(project.Id);
        var account = new WhatsAppAccount { ProjectId = project.Id, Name = "Test account" };
        var disconnected = new WhatsAppAccount { ProjectId = project.Id, Name = "Disconnected account" };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "Evening", Capacity = 5,
            SupervisorPhone = "201010000001", WhatsAppAccountId = disconnected.Id, DateTime = DateTime.UtcNow.AddDays(1) };
        db.AddRange(project, account, disconnected, group);
        await db.SaveChangesAsync();
        var command = new GroupBookingCommand { ProjectId = project.Id, GroupId = group.Id,
            CustomerName = "Test student", CustomerPhone = "01010000002", CustomerCity = "الإسكندرية", Origin = origin,
            ExistingBookingPolicy = ExistingGroupBookingPolicy.UpdateSameGroup };
        var coordinator = new GroupBookingCoordinator(db);
        Assert.True((await coordinator.BookAsync(command)).Changed);
        Assert.False((await coordinator.BookAsync(command)).Changed);
        var queued = Assert.Single(await db.GroupStaffNotifications.ToListAsync());
        Assert.Contains("201010000002", queued.Content);
        Assert.Contains("Test student", queued.Content);
        Assert.Equal(group.SupervisorPhone, queued.RecipientPhone);
        var gateway = new Gateway { DisconnectedAccountId = disconnected.Id };
        var config = new ConfigurationBuilder().Build();
        var worker = new GroupStaffAutomation(db, gateway, config,
            new WhatsAppGatewaySessionClient(gateway.CreateClient(""), config),
            new WhatsAppAccountService(db), NullLogger<GroupStaffAutomation>.Instance);
        await worker.RunAsync();
        Assert.Null(queued.SentAtUtc);
        Assert.NotNull(queued.LastError);
        await worker.RunAsync();
        Assert.Null(queued.SentAtUtc);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"GroupStaffNotifications\" SET \"UpdatedAt\" = {DateTime.UtcNow.AddMinutes(-6)} WHERE \"Id\" = {queued.Id}");
        await worker.RunAsync();
        Assert.NotNull(queued.SentAtUtc);
        Assert.Equal("provider-123", queued.ProviderMessageId);
        Assert.Equal(2, gateway.Sends.Count);
        Assert.Equal(gateway.Sends[0].GetProperty("idempotencyKey").GetString(), gateway.Sends[1].GetProperty("idempotencyKey").GetString());
        Assert.Equal(project.Id.ToString(), gateway.Sends[1].GetProperty("projectId").GetString());
        Assert.Equal(account.Id.ToString(), gateway.Sends[1].GetProperty("whatsappAccountId").GetString());
        tenant.SetProjectId(Guid.NewGuid());
        Assert.Empty(await db.GroupStaffNotifications.ToListAsync());
    }

    [Fact]
    public async Task Removing_supervisor_cancels_pending_notifications_and_new_bookings_do_not_queue()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Staff removal" };
        tenant.SetProjectId(project.Id);
        var group = new GroupAppointment { ProjectId = project.Id, Name = "Morning", Capacity = 5,
            SupervisorPhone = "201010000001", DateTime = DateTime.UtcNow.AddDays(1) };
        db.AddRange(project, group);
        await db.SaveChangesAsync();
        var coordinator = new GroupBookingCoordinator(db);
        await coordinator.BookAsync(new GroupBookingCommand { ProjectId = project.Id, GroupId = group.Id,
            CustomerName = "First", CustomerPhone = "201010000002" });
        group.SupervisorPhone = "";
        await db.SaveChangesAsync();
        await coordinator.BookAsync(new GroupBookingCommand { ProjectId = project.Id, GroupId = group.Id,
            CustomerName = "Second", CustomerPhone = "201010000003" });
        var gateway = new Gateway();
        var config = new ConfigurationBuilder().Build();
        var worker = new GroupStaffAutomation(db, gateway, config,
            new WhatsAppGatewaySessionClient(gateway.CreateClient(""), config),
            new WhatsAppAccountService(db), NullLogger<GroupStaffAutomation>.Instance);
        await worker.RunAsync();
        Assert.NotNull(Assert.Single(await db.GroupStaffNotifications.ToListAsync()).CancelledAtUtc);
        Assert.Empty(gateway.Sends);
    }

    [Fact]
    public async Task Transfer_back_cancels_old_unsent_alerts_instead_of_sending_stale_bookings()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Transfers" };
        tenant.SetProjectId(project.Id);
        var first = new GroupAppointment { ProjectId = project.Id, Name = "First", Capacity = 5,
            SupervisorPhone = "201010000001", DateTime = DateTime.UtcNow.AddDays(1) };
        var second = new GroupAppointment { ProjectId = project.Id, Name = "Second", Capacity = 5,
            SupervisorPhone = "201010000003", DateTime = DateTime.UtcNow.AddDays(1) };
        db.AddRange(project, first, second);
        await db.SaveChangesAsync();
        var coordinator = new GroupBookingCoordinator(db);
        foreach (var target in new[] { first, second, first })
            Assert.True((await coordinator.BookAsync(new GroupBookingCommand { ProjectId = project.Id,
                GroupId = target.Id, CustomerName = "Student", CustomerPhone = "201010000002",
                ExistingBookingPolicy = ExistingGroupBookingPolicy.Transfer })).Changed);
        var pending = await db.GroupStaffNotifications.Where(n => n.CancelledAtUtc == null).ToListAsync();
        Assert.Equal(first.Id, Assert.Single(pending).GroupAppointmentId);
        Assert.Equal(2, await db.GroupStaffNotifications.CountAsync(n => n.CancelledAtUtc != null));
    }

    private sealed class Gateway : HttpMessageHandler, IHttpClientFactory
    {
        public Guid? DisconnectedAccountId { get; init; }
        public List<JsonElement> Sends { get; } = [];
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && DisconnectedAccountId.HasValue
                && request.RequestUri!.Query.Contains(DisconnectedAccountId.Value.ToString()))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "Disconnected" }) };
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "Connected", phoneNumber = "201010000004", connectedAt = DateTime.UtcNow }) };
            Sends.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct)));
            return Sends.Count == 1 ? new(HttpStatusCode.ServiceUnavailable) :
                new(HttpStatusCode.OK) { Content = JsonContent.Create(new { messageId = "provider-123" }) };
        }
    }
}
