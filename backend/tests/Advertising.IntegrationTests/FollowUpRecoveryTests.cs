using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.API;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class FollowUpRecoveryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_phone_formats_create_one_scheduled_follow_up_and_preserve_history_and_other_tenants()
    {
        var (projectId, customer, conversation) = await SeedCustomerAsync();
        var due = DateTime.UtcNow.AddHours(1);
        await using (var seed = postgres.CreateContext())
        {
            seed.FollowUps.Add(new FollowUp { ProjectId = projectId, CustomerId = customer.Id,
                ConversationId = conversation.Id, Status = "Missed", Notes = "old notes", DueDate = due.AddDays(-1) });
            seed.Customers.Add(new Customer { ProjectId = Guid.NewGuid(), PhoneNumber = customer.PhoneNumber,
                Name = "Other tenant", City = "" });
            seed.WhatsAppPhoneCustomerIdentities.Add(new WhatsAppPhoneCustomerIdentity {
                ProjectId = projectId, CustomerId = customer.Id, NormalizedPhone = "201123456789" });
            await seed.SaveChangesAsync();
        }

        var recoveries = await Task.WhenAll(new[] { "01012345678", "+20 101 234 5678", "٠١٠١٢٣٤٥٦٧٨", "01123456789" }
            .Select(async phone =>
            {
                await using var db = postgres.CreateContext();
                return await new FollowUpRecoveryService(db).RecoverAsync(projectId, new(phone, due));
            }));

        Assert.Single(recoveries, recovery => recovery.Code == "CREATED");
        Assert.Equal(3, recoveries.Count(recovery => recovery.Code == "ALREADY_ACTIVE"));
        Assert.Single(recoveries.Select(recovery => recovery.FollowUp!.Id).Distinct());
        await using var verify = postgres.CreateContext();
        var history = await verify.FollowUps.IgnoreQueryFilters().Where(f => f.ProjectId == projectId).ToListAsync();
        Assert.Contains(history, f => f.Status == "Missed" && f.Notes == "old notes");
        var pending = Assert.Single(history, f => f.Status == "Pending");
        Assert.Equal(due, pending.DueDate, TimeSpan.FromMilliseconds(1));
        Assert.Equal(conversation.Id, pending.ConversationId);
        Assert.Equal(conversation.WhatsAppAccountId, pending.WhatsAppAccountId);
        Assert.Equal("WhatsApp", pending.Channel);
        Assert.Null(pending.SentAtUtc);
        Assert.Null(pending.SentMessageId);
    }

    [Theory]
    [InlineData("Pending", "ALREADY_ACTIVE")]
    [InlineData("Processing", "ALREADY_ACTIVE")]
    [InlineData("DeliveryUnknown", "DELIVERY_UNKNOWN")]
    public async Task Active_delivery_is_preserved_without_rescheduling_or_creating_a_duplicate(string status, string code)
    {
        var (projectId, customer, conversation) = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        var originalDue = DateTime.UtcNow.AddHours(2);
        var original = new FollowUp { ProjectId = projectId, CustomerId = customer.Id, Channel = "WhatsApp",
            ConversationId = conversation.Id, Status = status, Notes = "original message", DueDate = originalDue };
        db.Add(original);
        await db.SaveChangesAsync();

        var recovery = await new FollowUpRecoveryService(db).RecoverAsync(
            projectId, new("01012345678", DateTime.UtcNow.AddHours(1)));

        Assert.Equal(code, recovery.Code);
        Assert.Equal(original.Id, recovery.FollowUp!.Id);
        await db.Entry(original).ReloadAsync();
        Assert.Equal(status, original.Status);
        Assert.Equal(originalDue, original.DueDate, TimeSpan.FromMilliseconds(1));
        Assert.Equal("original message", original.Notes);
        Assert.Equal(1, await db.FollowUps.IgnoreQueryFilters().CountAsync(f => f.ProjectId == projectId));
    }

    [Theory]
    [InlineData("blacklisted", "CUSTOMER_BLOCKED")]
    [InlineData("disabled", "REMINDERS_DISABLED")]
    [InlineData("broken-rules", "REMINDERS_DISABLED")]
    [InlineData("paid", "CUSTOMER_PAID")]
    [InlineData("closed", "NO_SUPPORTED_CONVERSATION")]
    [InlineData("cloud", "NO_SUPPORTED_CONVERSATION")]
    [InlineData("unrouted", "NO_SUPPORTED_CONVERSATION")]
    [InlineData("handoff", "HUMAN_HANDOFF_PENDING")]
    [InlineData("unknown-delivery", "DELIVERY_UNKNOWN")]
    public async Task Recovery_does_not_override_customer_or_delivery_stops(string scenario, string code)
    {
        var (projectId, customer, conversation) = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        db.AttachRange(customer, conversation);
        switch (scenario)
        {
            case "blacklisted": customer.IsBlacklisted = true; break;
            case "disabled": customer.AutomationRules = """{"whatsappReminder24h":false}"""; break;
            case "broken-rules": customer.AutomationRules = "{"; break;
            case "closed": conversation.Status = "Closed"; break;
            case "cloud": conversation.WhatsAppDestinationId = Guid.NewGuid(); break;
            case "unrouted": conversation.WhatsAppAccountId = null; break;
            case "handoff": conversation.HumanHandoffReplyId = Guid.NewGuid(); break;
            case "unknown-delivery": conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow; break;
            case "paid":
                var group = new GroupAppointment { ProjectId = projectId, Name = "Paid group", Capacity = 5,
                    DateTime = DateTime.UtcNow.AddDays(5) };
                db.Add(group);
                db.Add(new GroupAppointmentBooking { ProjectId = projectId, CustomerId = customer.Id,
                    GroupAppointmentId = group.Id, CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber, IsPaid = true });
                break;
        }
        await db.SaveChangesAsync();

        var recovery = await new FollowUpRecoveryService(db).RecoverAsync(
            projectId, new("01012345678", DateTime.UtcNow.AddHours(1)));

        Assert.Equal(code, recovery.Code);
        Assert.False(await db.FollowUps.IgnoreQueryFilters().AnyAsync(f => f.ProjectId == projectId));
    }

    [Fact]
    public async Task Phone_lookup_requires_an_unambiguous_customer_in_the_requested_project()
    {
        var (projectId, customer, _) = await SeedCustomerAsync();
        await using var db = postgres.CreateContext();
        var otherProject = Guid.NewGuid();
        var service = new FollowUpRecoveryService(db);
        var request = new RecoverFollowUpRequest("01012345678", DateTime.UtcNow.AddHours(1));
        Assert.Equal("CUSTOMER_NOT_FOUND", (await service.RecoverAsync(otherProject, request)).Code);
        db.Customers.Add(new Customer { ProjectId = projectId, PhoneNumber = "+201012345678", Name = "Duplicate", City = "" });
        await db.SaveChangesAsync();
        Assert.Equal("AMBIGUOUS_CUSTOMER", (await service.RecoverAsync(projectId, request)).Code);
        Assert.False(await db.FollowUps.IgnoreQueryFilters().AnyAsync(f => f.ProjectId == projectId));
    }

    [Theory]
    [InlineData("not a number", 1, "INVALID_PHONE")]
    [InlineData("01012345678", -1, "INVALID_DUE_DATE")]
    public async Task Invalid_phone_or_past_schedule_cannot_create_follow_ups(string phone, int hours, string code)
    {
        await using var db = postgres.CreateContext();
        var recovery = await new FollowUpRecoveryService(db).RecoverAsync(
            Guid.NewGuid(), new(phone, DateTime.UtcNow.AddHours(hours)));
        Assert.Equal(code, recovery.Code);
        Assert.Null(recovery.FollowUp);
    }

    [Theory]
    [InlineData("Viewer", true)]
    [InlineData("Owner", false)]
    public async Task Recovery_endpoint_requires_write_permission_in_the_requested_project(string role, bool sameProject)
    {
        await using var db = postgres.CreateContext();
        var projectId = Guid.NewGuid();
        var controller = new FollowUpRecoveryController(db, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                    new Claim(ClaimTypes.Role, role),
                    new Claim("ProjectId", (sameProject ? projectId : Guid.NewGuid()).ToString())
                }, "test"))
            } }
        };
        Assert.IsType<ForbidResult>(await controller.Recover(projectId,
            new("01012345678", DateTime.UtcNow.AddHours(1)), CancellationToken.None));
    }

    private async Task<(Guid ProjectId, Customer Customer, Conversation Conversation)> SeedCustomerAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var customer = new Customer { ProjectId = projectId, PhoneNumber = "201012345678", Name = "عميل للمتابعة", City = "" };
        var conversation = new Conversation { ProjectId = projectId, CustomerId = customer.Id,
            WhatsAppAccountId = projectId, Channel = "WhatsApp" };
        db.AddRange(new Project { Id = projectId, Name = "Follow-up recovery" },
            new WhatsAppAccount { Id = projectId, ProjectId = projectId, IsDefault = true }, customer, conversation);
        await db.SaveChangesAsync();
        return (projectId, customer, conversation);
    }
}
