using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Services;
using Modules.CRM.Domain;
using Modules.CRM.API;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Xunit;
using Shared.Security;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CustomerExportTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task Closed_conversation_activity_uses_inclusive_thirty_day_boundary(int seconds, bool included)
    {
        var tenant = new TenantContext();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var project = new Project { Name = "Export boundary" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", City = "", PhoneNumber = "201012345678" };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id,
            Status = "Closed", CreatedAt = cutoff.AddDays(-2), LastMessageTimestamp = cutoff.AddSeconds(seconds) };
        db.AddRange(project, customer, conversation);
        await db.SaveChangesAsync();

        tenant.SetProjectId(project.Id);
        await db.Conversations.Where(chat => chat.ProjectId == project.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(chat => chat.CreatedAt, cutoff.AddDays(-3)));
        var exported = await new CustomerExportQuery(db).GetCustomersAsync(project.Id, cutoff, default);

        Assert.Equal(included, exported.Any(customer => customer.PhoneNumber == "201012345678"));
    }

    [Fact]
    public async Task Export_for_another_project_is_forbidden()
    {
        await using var db = postgres.CreateContext();
        var controller = new CustomerExportController(db, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", Guid.NewGuid().ToString())], "test")) } }
        };
        Assert.IsType<ForbidResult>(await controller.GetExport(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Export_excludes_paid_blacklisted_and_recent_duplicate_phones_across_all_conversations()
    {
        var tenant = new TenantContext();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var project = new Project { Name = "Export exclusions" };
        var otherProject = new Project { Name = "Export isolation" };
        Customer Customer(string phone, Guid? projectId = null) => new() {
            ProjectId = projectId ?? project.Id, Name = "عميل", City = "", PhoneNumber = phone, CreatedAt = cutoff.AddDays(-10) };
        var eligible = Customer("01011111111");
        var duplicate = Customer("+20 10 1111 1111");
        var blocked = Customer("201022222222");
        blocked.IsBlacklisted = true;
        var paid = Customer("201033333333");
        var paidPhone = Customer("01044444444");
        var won = Customer("201066666666");
        var stage = new PipelineStage { ProjectId = project.Id, Name = "Won" };
        var recent = Customer("201055555555");
        var recentDuplicate = Customer("01055555555");
        var other = Customer(eligible.PhoneNumber, otherProject.Id);
        other.IsBlacklisted = true;
        var group = new GroupAppointment { ProjectId = project.Id, Name = "Group", Capacity = 5 };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = recent.Id,
            Status = "Closed", CreatedAt = cutoff.AddDays(-3), LastMessageTimestamp = cutoff.AddDays(-2) };
        db.AddRange(project, otherProject, eligible, duplicate, blocked, paid, paidPhone, recent, recentDuplicate, other, group, conversation, won, stage,
            new Deal { ProjectId = project.Id, CustomerId = won.Id, PipelineStageId = stage.Id, Title = "Subscription", Status = DealStatus.Won },
            new GroupAppointmentBooking { ProjectId = project.Id, CustomerId = paid.Id, GroupAppointmentId = group.Id,
                CustomerName = paid.Name, CustomerPhone = "+20 10 4444 4444", IsPaid = true },
            new Message { ConversationId = conversation.Id, Direction = "Outgoing", SenderType = "Agent",
                Content = "Reply", MessageType = "Text", ExternalMessageId = Guid.NewGuid().ToString(), Timestamp = cutoff.AddDays(1) });
        await db.SaveChangesAsync();

        tenant.SetProjectId(project.Id);
        await db.Conversations.Where(chat => chat.ProjectId == project.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(chat => chat.CreatedAt, cutoff.AddDays(-3)));
        var exported = await new CustomerExportQuery(db).GetCustomersAsync(project.Id, cutoff, default);
        Assert.Equal("201011111111", Assert.Single(exported).PhoneNumber);
        var allNonSubscribers = await new CustomerExportQuery(db).GetCustomersAsync(project.Id, null, default);
        Assert.Equal(new[] { "201011111111", "201055555555" }, allNonSubscribers.Select(customer => customer.PhoneNumber));
    }
}
