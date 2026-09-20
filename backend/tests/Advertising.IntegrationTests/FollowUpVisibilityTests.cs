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
public sealed class FollowUpVisibilityTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("expired", "Nurturing", true)]
    [InlineData("expired", "AppointmentReminder", false)]
    [InlineData("cancelled", "Nurturing", false)]
    [InlineData("deleted", "Nurturing", false)]
    public async Task Post_session_follow_up_survives_expiry_but_not_explicit_cancellation_or_deleted_booking(
        string change, string type, bool canDispatch)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Post-session follow-up regression" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", City = "", PhoneNumber = "201012345678" };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "مجموعة", Capacity = 5,
            DateTime = DateTime.UtcNow.AddDays(-3) };
        var booking = new GroupAppointmentBooking { ProjectId = project.Id, CustomerId = customer.Id,
            GroupAppointmentId = group.Id, CustomerName = customer.Name, CustomerPhone = customer.PhoneNumber };
        var followUp = new FollowUp { ProjectId = project.Id, CustomerId = customer.Id,
            GroupAppointmentId = group.Id, GroupAppointmentBookingId = booking.Id, AppointmentTime = group.DateTime,
            DueDate = group.DateTime.AddDays(2), Status = "Processing", Type = type, Notes = "متابعة بعد السيشن" };
        db.AddRange(project, customer, group, booking, followUp);
        await db.SaveChangesAsync();
        var lifecycle = new GroupBookingFollowUpLifecycle(db);
        if (change == "cancelled") await lifecycle.CancelForGroupAsync(group);
        if (change == "deleted") db.GroupAppointmentBookings.Remove(booking);
        group.IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(canDispatch, await lifecycle.CanDispatchAsync(followUp));
    }

    [Theory]
    [InlineData("01012345678")]
    [InlineData("+20 10 1234 5678")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨")]
    [InlineData("01098765432")]
    [InlineData("+20 10 9876 5432")]
    [InlineData("01122223333")]
    public async Task Follow_up_search_finds_local_international_and_booking_phone_without_crossing_projects(string search)
    {
        var tenant = new TenantContext();
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Follow-up phone visibility regression" };
        var customer = new Customer { ProjectId = project.Id, Name = "عميل", City = "", PhoneNumber = "201012345678" };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "مجموعة", Capacity = 5, DateTime = DateTime.UtcNow.AddDays(2) };
        var booking = new GroupAppointmentBooking { ProjectId = project.Id, CustomerId = customer.Id,
            GroupAppointmentId = group.Id, CustomerName = customer.Name, CustomerPhone = "201098765432" };
        var pending = new FollowUp { ProjectId = project.Id, CustomerId = customer.Id, DueDate = DateTime.UtcNow, Notes = "متابعة" };
        var bypassed = new FollowUp { ProjectId = project.Id, CustomerId = customer.Id,
            DueDate = pending.DueDate, Status = "Bypassed", Notes = "متابعة سابقة" };
        var otherProject = new Project { Name = "Other project" };
        var otherCustomer = new Customer { ProjectId = otherProject.Id, Name = "عميل آخر", City = "", PhoneNumber = customer.PhoneNumber };
        var unrelated = new Customer { ProjectId = project.Id, Name = "عميل مختلف", City = "", PhoneNumber = "201011112222" };
        db.AddRange(project, customer, group, booking, pending, bypassed, otherProject, otherCustomer, unrelated,
            new WhatsAppPhoneCustomerIdentity { ProjectId = project.Id, CustomerId = customer.Id, NormalizedPhone = "201122223333" },
            new FollowUp { ProjectId = otherProject.Id, CustomerId = otherCustomer.Id, DueDate = pending.DueDate, Notes = "خارج المشروع" },
            new FollowUp { ProjectId = project.Id, CustomerId = unrelated.Id, DueDate = pending.DueDate, Notes = "رقم مختلف" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        tenant.SetProjectId(project.Id);
        var controller = new CRMController(db, null!, null!, null!, null!, tenant, null!, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("ProjectId", project.Id.ToString())], "test")) } }
        };

        var pendingPage = Assert.IsType<FollowUpPage>(
            Assert.IsType<OkObjectResult>(await controller.GetFollowUpsPage(project.Id, search: search)).Value);
        Assert.Equal(pending.Id, Assert.Single(pendingPage.Items).Id);
        Assert.Equal(1, pendingPage.FilteredCount);

        var allPage = Assert.IsType<FollowUpPage>(
            Assert.IsType<OkObjectResult>(await controller.GetFollowUpsPage(project.Id, status: "All", search: search)).Value);
        Assert.Equal(new[] { pending.Id, bypassed.Id }.Order(), allPage.Items.Select(followUp => followUp.Id).Order());
        Assert.Equal(2, allPage.FilteredCount);
    }
}
