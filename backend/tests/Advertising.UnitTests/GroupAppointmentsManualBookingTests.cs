using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.CRM.Domain;
using Modules.GroupAppointments.API;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GroupAppointmentsManualBookingTests
{
    [Fact]
    public async Task Arabic_digits_are_canonicalized_and_a_new_manual_booking_is_persisted()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity: 2);
        db.GroupAppointments.Add(group);
        await db.SaveChangesAsync();

        var response = Assert.IsType<ObjectResult>(await controller.CreateManualBooking(group.Id, new ManualGroupBookingRequest
        {
            CustomerName = "  أحمد علي  ",
            CustomerPhone = "٠١٠-١٢٣٤-٥٦٧٨",
            IsPaid = true,
            IsAttended = true,
            Notes = "  حضر الحصة التجريبية  "
        }));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var booking = await db.GroupAppointmentBookings.SingleAsync();
        var customer = await db.Customers.SingleAsync();
        Assert.Equal("أحمد علي", booking.CustomerName);
        Assert.Equal("201012345678", booking.CustomerPhone);
        Assert.True(booking.IsPaid);
        Assert.True(booking.IsAttended);
        Assert.Equal(customer.Id, booking.CustomerId);
        Assert.Contains("حجز مجموعة", customer.Tags);
        Assert.Contains("تمت إضافته يدويًا", customer.Notes);
        Assert.Contains("حضر الحصة التجريبية", customer.Notes);
        Assert.Single(db.IntegrationOutboxMessages);

        var body = Body(response);
        Assert.Equal("201012345678", body.GetProperty("booking").GetProperty("customerPhone").GetString());
        Assert.Equal(1, body.GetProperty("group").GetProperty("bookedCount").GetInt32());
        Assert.Equal(1, body.GetProperty("group").GetProperty("slotsLeft").GetInt32());
    }

    [Fact]
    public async Task Existing_customer_is_reused_without_erasing_notes_and_paid_booking_cancels_pending_follow_up()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Admin", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity: 3);
        var customer = Customer(projectId, "+201001112233", "اسم قديم", "ملاحظة قديمة");
        var followUp = new FollowUp
        {
            ProjectId = projectId,
            CustomerId = customer.Id,
            DueDate = DateTime.UtcNow.AddDays(1),
            Status = "Pending",
            Notes = "اتصال متابعة"
        };
        db.AddRange(group, customer, followUp);
        await db.SaveChangesAsync();

        var response = Assert.IsType<ObjectResult>(await controller.CreateManualBooking(group.Id, new ManualGroupBookingRequest
        {
            CustomerName = "اسم محدث",
            CustomerPhone = "01001112233",
            IsPaid = true
        }));

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.Single(await db.Customers.ToListAsync());
        Assert.Equal(customer.Id, (await db.GroupAppointmentBookings.SingleAsync()).CustomerId);
        Assert.Equal("اسم محدث", customer.Name);
        Assert.Equal("201001112233", customer.PhoneNumber);
        Assert.Contains("ملاحظة قديمة", customer.Notes);
        Assert.Contains("تمت إضافته يدويًا", customer.Notes);
        Assert.Equal("Cancelled", followUp.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Existing_booking_in_any_group_is_rejected_without_a_silent_transfer(bool sameGroup)
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var target = ActiveGroup(projectId, capacity: 3, name: "المجموعة الجديدة");
        var existingGroup = sameGroup ? target : ActiveGroup(projectId, capacity: 3, name: "المجموعة الحالية");
        var customer = Customer(projectId, "201055566677", "عميل حالي");
        var existingBooking = Booking(projectId, existingGroup.Id, customer, "201055566677");
        db.AddRange(target, customer, existingBooking);
        if (!sameGroup)
        {
            db.GroupAppointments.Add(existingGroup);
        }
        await db.SaveChangesAsync();

        var response = Assert.IsType<ConflictObjectResult>(await controller.CreateManualBooking(target.Id, new ManualGroupBookingRequest
        {
            CustomerName = "اسم جديد",
            CustomerPhone = "+20 10 555 666 77"
        }));

        var body = Body(response);
        Assert.Equal("BOOKING_ALREADY_EXISTS", body.GetProperty("code").GetString());
        Assert.Equal(existingGroup.Id, body.GetProperty("existingGroupId").GetGuid());
        Assert.Equal(existingGroup.Name, body.GetProperty("existingGroupName").GetString());
        Assert.Single(await db.GroupAppointmentBookings.ToListAsync());
        Assert.Equal(existingGroup.Id, existingBooking.GroupAppointmentId);
        Assert.Equal("عميل حالي", customer.Name);
        Assert.Empty(db.IntegrationOutboxMessages);
    }

    [Theory]
    [InlineData(false, 2, false, "GROUP_INACTIVE")]
    [InlineData(true, 1, true, "GROUP_FULL")]
    public async Task Unavailable_group_rejects_the_booking_before_creating_a_customer(
        bool isActive,
        int capacity,
        bool fillGroup,
        string expectedCode)
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity);
        group.IsActive = isActive;
        db.GroupAppointments.Add(group);
        if (fillGroup)
        {
            var occupant = Customer(projectId, "201099900011", "مشترك موجود");
            db.AddRange(occupant, Booking(projectId, group.Id, occupant, occupant.PhoneNumber));
        }
        await db.SaveChangesAsync();
        var customerCount = await db.Customers.CountAsync();

        var response = Assert.IsType<ConflictObjectResult>(await controller.CreateManualBooking(group.Id, new ManualGroupBookingRequest
        {
            CustomerName = "مشترك جديد",
            CustomerPhone = "01022223333"
        }));

        Assert.Equal(expectedCode, Body(response).GetProperty("code").GetString());
        Assert.Equal(customerCount, await db.Customers.CountAsync());
        Assert.Empty(db.IntegrationOutboxMessages);
    }

    [Theory]
    [InlineData("Agent", true)]
    [InlineData("Owner", false)]
    public async Task Non_manager_or_spoofed_tenant_context_is_forbidden_before_any_write(string role, bool claimMatchesTenant)
    {
        var tenantProjectId = Guid.NewGuid();
        var claimedProjectId = claimMatchesTenant ? tenantProjectId : Guid.NewGuid();
        var (controller, db) = CreateController(tenantProjectId, role, claimedProjectId);
        await using var ownedDb = db;

        var response = await controller.CreateManualBooking(Guid.NewGuid(), new ManualGroupBookingRequest
        {
            CustomerName = "غير مصرح",
            CustomerPhone = "01012345678"
        });

        Assert.IsType<ForbidResult>(response);
        Assert.Empty(await db.Customers.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.GroupAppointmentBookings.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(db.IntegrationOutboxMessages);
    }

    [Theory]
    [InlineData("phone: 01012345678")]
    [InlineData("01234567")]
    public async Task Noncanonical_phone_is_rejected_before_any_write(string phone)
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;

        var response = Assert.IsType<BadRequestObjectResult>(await controller.CreateManualBooking(Guid.NewGuid(), new ManualGroupBookingRequest
        {
            CustomerName = "عميل",
            CustomerPhone = phone
        }));

        Assert.Equal("PHONE_INVALID", Body(response).GetProperty("code").GetString());
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.GroupAppointmentBookings.ToListAsync());
    }

    [Fact]
    public async Task Public_booking_canonicalizes_arabic_phone_and_formatted_retry_does_not_duplicate()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity: 1);
        db.AddRange(group, new ProjectSettings
        {
            ProjectId = projectId,
            IsGroupAppointmentsEnabled = true,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var firstResponse = await controller.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = group.Id,
            CustomerName = "عميل الحجز العام",
            CustomerPhone = "٠١٠-١٢٣٤-٥٦٧٨"
        });
        var retryResponse = await controller.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = group.Id,
            CustomerName = "عميل الحجز العام",
            CustomerPhone = "+20 (10) 1234-5678"
        });

        Assert.IsType<OkObjectResult>(firstResponse);
        Assert.IsType<OkObjectResult>(retryResponse);
        var customer = await db.Customers.SingleAsync();
        var booking = await db.GroupAppointmentBookings.SingleAsync();
        Assert.Equal("201012345678", customer.PhoneNumber);
        Assert.Equal("201012345678", booking.CustomerPhone);
        Assert.Equal(customer.Id, booking.CustomerId);
        Assert.Single(await db.NotificationAlerts.ToListAsync());
        Assert.Single(db.IntegrationOutboxMessages);
    }

    [Fact]
    public async Task Public_booking_transfers_existing_booking_without_losing_payment_status()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var originalGroup = ActiveGroup(projectId, capacity: 2, name: "المجموعة القديمة");
        var targetGroup = ActiveGroup(projectId, capacity: 1, name: "المجموعة الجديدة");
        var customer = Customer(projectId, "+201088877766", "اسم قديم");
        var booking = Booking(projectId, originalGroup.Id, customer, customer.PhoneNumber);
        booking.IsPaid = true;
        booking.IsAttended = true;
        db.AddRange(originalGroup, targetGroup, customer, booking, new ProjectSettings
        {
            ProjectId = projectId,
            IsGroupAppointmentsEnabled = true,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();

        var response = await controller.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = targetGroup.Id,
            CustomerName = "اسم محدث",
            CustomerPhone = "010 888 777 66"
        });

        Assert.IsType<OkObjectResult>(response);
        db.ChangeTracker.Clear();
        var persistedBooking = Assert.Single(await db.GroupAppointmentBookings.AsNoTracking().ToListAsync());
        Assert.Equal(targetGroup.Id, persistedBooking.GroupAppointmentId);
        Assert.True(persistedBooking.IsPaid);
        Assert.False(persistedBooking.IsAttended);
        Assert.Equal("201088877766", persistedBooking.CustomerPhone);
        Assert.Empty(await db.IntegrationOutboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Public_booking_with_blank_customer_name_returns_bad_request_without_writes()
    {
        var projectId = Guid.NewGuid();
        var (controller, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;

        var response = await controller.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = Guid.NewGuid(),
            CustomerName = "   ",
            CustomerPhone = "01012345678"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.GroupAppointmentBookings.ToListAsync());
    }

    [Fact]
    public async Task Ai_orchestrator_full_group_returns_a_truthful_failure_reply_without_writes()
    {
        var projectId = Guid.NewGuid();
        var (_, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity: 1);
        var occupant = Customer(projectId, "201011112222", "مشترك موجود");
        db.AddRange(group, occupant, Booking(projectId, group.Id, occupant, occupant.PhoneNumber));
        await db.SaveChangesAsync();
        var orchestrator = new AiGroupBookingOrchestrator(
            db,
            new GroupBookingCoordinator(db),
            NoOpHubContext(),
            NullLogger<AiGroupBookingOrchestrator>.Instance);

        var result = await orchestrator.BookSuggestedPeopleAsync(new AiGroupBookingRequest
        {
            ProjectId = projectId,
            GroupId = group.Id,
            SuggestedPeople =
            [
                new SuggestedGroupBookingPerson
                {
                    Name = "مشترك جديد",
                    PhoneNumber = "01033334444"
                }
            ],
            Timezone = "Africa/Cairo"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(AiGroupBookingFailure.GroupFull, result.Failure);
        Assert.Contains("اكتملت", result.CustomerReplyOverride, StringComparison.Ordinal);
        Assert.Single(await db.Customers.AsNoTracking().ToListAsync());
        Assert.Single(await db.GroupAppointmentBookings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Ai_orchestrator_preserves_actual_remaining_capacity_while_bounding_customer_facing_availability()
    {
        var projectId = Guid.NewGuid();
        var (_, db) = CreateController(projectId, "Owner", projectId);
        await using var ownedDb = db;
        var group = ActiveGroup(projectId, capacity: 20);
        db.GroupAppointments.Add(group);
        await db.SaveChangesAsync();
        var orchestrator = new AiGroupBookingOrchestrator(
            db,
            new GroupBookingCoordinator(db),
            NoOpHubContext(),
            NullLogger<AiGroupBookingOrchestrator>.Instance);

        var result = await orchestrator.BookSuggestedPeopleAsync(new AiGroupBookingRequest
        {
            ProjectId = projectId,
            GroupId = group.Id,
            SuggestedPeople =
            [
                new SuggestedGroupBookingPerson
                {
                    Name = "مشترك جديد",
                    PhoneNumber = "01033334444"
                }
            ],
            Timezone = "Africa/Cairo"
        });
        var reply = AiGroupBookingReplyPolicy.Apply(
            "تم الحجز",
            shouldOfferTrial: true,
            result);

        Assert.True(result.Succeeded);
        Assert.Equal(19, result.DisplayedRemainingPlaces);
        Assert.Equal(20, (await db.GroupAppointments.AsNoTracking().SingleAsync()).Capacity);
        Assert.Single(await db.GroupAppointmentBookings.AsNoTracking().ToListAsync());
        Assert.Contains("فاضل ٧ أماكن", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("١٩", reply, StringComparison.Ordinal);
    }

    private static (GroupAppointmentsController Controller, AppDbContext Db) CreateController(
        Guid tenantProjectId,
        string role,
        Guid claimedProjectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(tenantProjectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        var controller = new GroupAppointmentsController(
            db,
            tenant,
            new ProjectAuthorizationService(),
            new GroupBookingCoordinator(db),
            NoOpHubContext(),
            NoOpRedis(),
            new WhatsAppAccountService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = User(role, claimedProjectId)
                }
            }
        };
        return (controller, db);
    }

    private static GroupAppointment ActiveGroup(Guid projectId, int capacity, string name = "مجموعة اختبار") => new()
    {
        ProjectId = projectId,
        Name = name,
        Capacity = capacity,
        IsActive = true,
        DateTime = DateTime.UtcNow.AddDays(2)
    };

    private static Customer Customer(Guid projectId, string phone, string name, string notes = "") => new()
    {
        ProjectId = projectId,
        PhoneNumber = phone,
        Name = name,
        City = string.Empty,
        Tags = Array.Empty<string>(),
        Notes = notes
    };

    private static GroupAppointmentBooking Booking(Guid projectId, Guid groupId, Customer customer, string phone) => new()
    {
        ProjectId = projectId,
        GroupAppointmentId = groupId,
        CustomerId = customer.Id,
        CustomerName = customer.Name,
        CustomerPhone = phone
    };

    private static ClaimsPrincipal User(string role, Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, role),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));

    private static JsonElement Body(ObjectResult response) =>
        JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static IHubContext<NotificationHub> NoOpHubContext()
    {
        var client = Proxy<IClientProxy>((method, _) =>
            method.ReturnType == typeof(Task) ? Task.CompletedTask : Default(method.ReturnType));
        var clients = Proxy<IHubClients>((_, _) => client);
        var groups = Proxy<IGroupManager>((method, _) =>
            method.ReturnType == typeof(Task) ? Task.CompletedTask : Default(method.ReturnType));
        return Proxy<IHubContext<NotificationHub>>((method, _) =>
            method.Name == "get_Clients" ? clients : groups);
    }

    private static IConnectionMultiplexer NoOpRedis()
    {
        var database = Proxy<IDatabase>((method, _) => Default(method.ReturnType));
        return Proxy<IConnectionMultiplexer>((method, _) =>
            method.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? database
                : Default(method.ReturnType));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? Default(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod == null ? null : Handler(targetMethod, args);
    }
}
