using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Conversations.API;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MessagingBookingIntegrityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Equal_timestamp_messages_are_reachable_once_and_preserve_the_persisted_sender()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Message pagination regression" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", PhoneNumber = "201012345678", City = "" };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id, Channel = "WhatsApp", Status = "Open" };
        db.AddRange(project, customer, conversation);
        var timestamp = DateTime.UtcNow.Date.AddHours(10);
        var expected = Enumerable.Range(0, 11).Select(index => new Message
        {
            ConversationId = conversation.Id, ExternalMessageId = $"provider-{index}",
            Direction = "Outgoing", SenderType = "AI", Content = $"رسالة {index}",
            MessageType = "Text", Timestamp = timestamp
        }).ToArray();
        db.Messages.AddRange(expected);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new ConversationController(db, null!, null!, null!, new ConfigurationBuilder().Build(),
            DispatchProxy.Create<IConnectionMultiplexer, UnusedRedis>(), null!, null!, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", project.Id.ToString())], "test")) } }
        };

        var first = Body(await controller.ListMessages(conversation.Id, limit: 10));
        var second = Body(await controller.ListMessages(conversation.Id,
            first[0].GetProperty("timestamp").GetDateTime(), 10, first[0].GetProperty("id").GetGuid()));
        var all = first.EnumerateArray().Concat(second.EnumerateArray()).ToArray();

        Assert.Equal(10, first.GetArrayLength());
        Assert.Equal(1, second.GetArrayLength());
        Assert.Equal(expected.Select(message => message.Id).Order(), all.Select(message => message.GetProperty("id").GetGuid()).Order());
        Assert.All(all, message => Assert.Equal("AI", message.GetProperty("senderType").GetString()));
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("reschedule")]
    [InlineData("deactivate")]
    public async Task A_claimed_reminder_rechecks_current_booking_before_delivery(string change)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Booking reminder regression" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", PhoneNumber = "201012345678", City = "" };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "مجموعة", Capacity = 5, DateTime = DateTime.UtcNow.Date.AddDays(1) };
        var booking = new GroupAppointmentBooking { ProjectId = project.Id, GroupAppointmentId = group.Id,
            CustomerId = customer.Id, CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber };
        var followUp = new FollowUp { ProjectId = project.Id, CustomerId = customer.Id,
            GroupAppointmentId = group.Id, GroupAppointmentBookingId = booking.Id,
            AppointmentTime = group.DateTime, DueDate = DateTime.UtcNow, Status = "Processing", Notes = "تذكير" };
        db.AddRange(project, customer, group, booking, followUp);
        await db.SaveChangesAsync();
        var lifecycle = new GroupBookingFollowUpLifecycle(db);
        Assert.True(await lifecycle.CanDispatchAsync(followUp));

        await using (var concurrent = postgres.CreateContext())
        {
            if (change == "delete")
                await concurrent.GroupAppointmentBookings.IgnoreQueryFilters().Where(item => item.Id == booking.Id).ExecuteDeleteAsync();
            else if (change == "reschedule")
                await concurrent.GroupAppointments.IgnoreQueryFilters().Where(item => item.Id == group.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.DateTime, group.DateTime.AddDays(1)));
            else
                await concurrent.GroupAppointments.IgnoreQueryFilters().Where(item => item.Id == group.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        }

        Assert.False(await lifecycle.CanDispatchAsync(followUp));
    }

    private static JsonElement Body(IActionResult response) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(response).Value);

    public class UnusedRedis : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }
}
