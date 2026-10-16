using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SubscriberAutoStopTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, true)]
    public async Task Only_subscribers_are_permanently_blocked_but_stop_requests_always_cancel_followups(
        bool subscribersOnly, bool paid, bool imported, bool expectedBlocked)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TenantContext(), services);
        var customer = new Customer { ProjectId = Guid.NewGuid(), PhoneNumber = "201011112222", Name = "عميل", City = "القاهرة",
            Tags = imported ? ["المحظورين للدفع"] : [] };
        var settingsService = new AIBehaviorSettingsService();
        db.AddRange(customer, new ProjectSettings { ProjectId = customer.ProjectId,
            AiBehaviorSettingsJson = settingsService.Serialize(new() { SubscribersOnlyAutoStop = subscribersOnly }) });
        var followUp = new FollowUp { ProjectId = customer.ProjectId, CustomerId = customer.Id, Status = "Pending", Notes = "متابعة" };
        var unrelated = new FollowUp { ProjectId = Guid.NewGuid(), CustomerId = customer.Id, Status = "Pending", Notes = "متابعة" };
        db.AddRange(followUp, unrelated);
        if (paid) db.Add(new GroupAppointmentBooking { ProjectId = customer.ProjectId,
            CustomerId = customer.Id, GroupAppointmentId = Guid.NewGuid(), IsPaid = true,
            CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber });
        await db.SaveChangesAsync();
        await new CustomerOptOutService(db, settingsService).ApplyIfRequestedAsync(customer, "مش عايز رسائل");
        await db.SaveChangesAsync();
        Assert.Equal(expectedBlocked, (await db.Customers.IgnoreQueryFilters().SingleAsync()).IsBlacklisted);
        Assert.Equal("Cancelled", followUp.Status);
        Assert.Equal("Pending", unrelated.Status);
    }
}
