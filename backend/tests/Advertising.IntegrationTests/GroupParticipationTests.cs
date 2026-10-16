using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class GroupParticipationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Counts_only_provider_confirmed_group_invites_for_current_bookings_and_own_account()
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var tenant = new TenantContext();
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Participation" };
        var account = new WhatsAppAccount { ProjectId = project.Id, Name = "Group account" };
        var anotherAccount = new WhatsAppAccount { ProjectId = project.Id, Name = "Other account" };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "Evening", Capacity = 50,
            WhatsAppAccountId = account.Id, WhatsAppGroupJid = "120363123456789@g.us",
            WhatsAppGroupInviteLink = "https://chat.whatsapp.com/CurrentLink", DateTime = DateTime.UtcNow.AddDays(1) };
        db.AddRange(project, account, anotherAccount, group);
        var now = DateTime.UtcNow;
        var cases = new[] { "confirmed", "pending", "otherLink", "incoming", "otherAccount", "oldMessage", "linkedRotatedLink" };
        var customers = cases.Select((name, index) => new Customer {
            ProjectId = project.Id, Name = name, City = "", PhoneNumber = $"20101111111{index}" }).ToArray();
        foreach (var customer in customers)
        {
            var booking = new GroupAppointmentBooking { ProjectId = project.Id, GroupAppointmentId = group.Id,
                CustomerId = customer.Id, CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber };
            var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id,
                WhatsAppAccountId = customer.Name == "otherAccount" ? anotherAccount.Id : account.Id, Channel = "WhatsApp" };
            var message = new Message { ConversationId = conversation.Id,
                Direction = customer.Name == "incoming" ? "Incoming" : "Outgoing", SenderType = "System", MessageType = "Text",
                Content = customer.Name == "otherLink" ? group.WhatsAppGroupInviteLink + "Extra" :
                    customer.Name == "linkedRotatedLink" ? "https://chat.whatsapp.com/PreviousLink" : group.WhatsAppGroupInviteLink,
                ExternalMessageId = customer.Name == "pending" ? "" : Guid.NewGuid().ToString(),
                Timestamp = customer.Name == "oldMessage" ? now.AddDays(-2) : now.AddMinutes(1) };
            db.AddRange(customer, booking, conversation, message);
            if (customer.Name == "linkedRotatedLink")
                db.Add(new FollowUp { ProjectId = project.Id, CustomerId = customer.Id, GroupAppointmentId = group.Id,
                    GroupAppointmentBookingId = booking.Id, WhatsAppAccountId = account.Id,
                    Type = "AppointmentReminder", Status = "Completed", Notes = message.Content,
                    DueDate = now, SentMessageId = message.Id });
        }
        await db.SaveChangesAsync();
        tenant.SetProjectId(project.Id);
        await db.GroupAppointmentBookings.Where(b => b.GroupAppointmentId == group.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(b => b.CreatedAt, now.AddDays(-1)));

        var gateway = new GatewayHandler(HttpStatusCode.OK, new { checkedAtUtc = now,
            members = customers.Select(customer => new { phone = customer.PhoneNumber,
                status = customer.Name == "confirmed" ? "joined" : "not_joined" }).ToArray() });
        var service = new GroupParticipationService(db, gateway, new ConfigurationBuilder().Build());
        var report = await service.GetAsync(project.Id, group.Id, default);

        Assert.NotNull(report);
        Assert.Equal(new[] { "confirmed", "linkedRotatedLink" }, report.Rows.Where(r => r.SentAtUtc != null).Select(r => r.Name));
        Assert.Equal("joined", report.Rows.Single(r => r.Name == "confirmed").Membership);
        Assert.Equal("not_joined", report.Rows.Single(r => r.Name == "linkedRotatedLink").Membership);
        Assert.Equal("ready", report.MembershipAvailability);
        Assert.Contains(account.Id.ToString(), gateway.RequestBody);
        Assert.Contains(project.Id.ToString(), gateway.RequestBody);
        Assert.Null(await service.GetAsync(Guid.NewGuid(), group.Id, default));

        var unavailable = new GroupParticipationService(db,
            new GatewayHandler(HttpStatusCode.ServiceUnavailable, new { error = "offline" }), new ConfigurationBuilder().Build());
        var offline = await unavailable.GetAsync(project.Id, group.Id, default);
        Assert.Equal("unavailable", offline!.MembershipAvailability);
        Assert.All(offline.Rows, row => Assert.Equal("unknown", row.Membership));
        Assert.Equal(2, offline.Rows.Count(row => row.SentAtUtc != null));
    }

    private sealed class GatewayHandler(HttpStatusCode status, object response) : HttpMessageHandler, IHttpClientFactory
    {
        public string RequestBody { get; private set; } = "";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = JsonContent.Create(response) };
        }
    }
}
