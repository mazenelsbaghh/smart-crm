using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.API;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class GroupBookingConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Public_and_manual_booking_competing_for_final_slot_cannot_overbook()
    {
        var projectId = Guid.NewGuid();
        var group = await SeedAvailableGroupAsync(projectId, capacity: 1);
        var lockBarrier = new GroupLockBarrierInterceptor();
        var manualTenant = Tenant(projectId);
        var publicTenant = Tenant(projectId);
        await using var manualDb = postgres.CreateContext(manualTenant, lockBarrier);
        await using var publicDb = postgres.CreateContext(publicTenant, lockBarrier);
        var manualController = Controller(manualDb, manualTenant);
        var publicController = Controller(publicDb, publicTenant);

        var manualTask = manualController.CreateManualBooking(group.Id, new ManualGroupBookingRequest
        {
            CustomerName = "عميل يدوي",
            CustomerPhone = "01011112222"
        });
        var publicTask = publicController.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = group.Id,
            CustomerName = "عميل عام",
            CustomerPhone = "01033334444"
        });
        var responses = await Task.WhenAll(manualTask, publicTask);

        Assert.Equal(2, lockBarrier.Arrivals);
        Assert.Equal(1, CountSuccessfulBookings(responses[0], responses[1]));
        await using var verificationDb = postgres.CreateContext(Tenant(projectId));
        var booking = Assert.Single(await verificationDb.GroupAppointmentBookings.AsNoTracking().ToListAsync());
        var customer = Assert.Single(await verificationDb.Customers.AsNoTracking().ToListAsync());
        Assert.Equal(customer.Id, booking.CustomerId);
        await AssertBookingOutcomeAsync(verificationDb, projectId, booking, customer);
    }

    [Fact]
    public async Task Public_and_ai_orchestrator_competing_for_final_slot_cannot_overbook()
    {
        var projectId = Guid.NewGuid();
        var group = await SeedAvailableGroupAsync(projectId, capacity: 1);
        var lockBarrier = new GroupLockBarrierInterceptor();
        await using var aiDb = postgres.CreateContext(Tenant(projectId), lockBarrier);
        await using var publicDb = postgres.CreateContext(Tenant(projectId), lockBarrier);
        var (aiHub, broadcastProbe) = CommitObservingHub(projectId);
        var aiOrchestrator = new AiGroupBookingOrchestrator(
            aiDb,
            new GroupBookingCoordinator(aiDb),
            aiHub,
            NullLogger<AiGroupBookingOrchestrator>.Instance);
        var publicController = Controller(publicDb, Tenant(projectId));

        var aiTask = aiOrchestrator.BookSuggestedPeopleAsync(new AiGroupBookingRequest
        {
            ProjectId = projectId,
            GroupId = group.Id,
            SuggestedPeople =
            [
                new SuggestedGroupBookingPerson
                {
                    Name = "عميل AI الأول",
                    PhoneNumber = "٠١٠-٥٥٥٥-٦٦٦٦"
                },
                new SuggestedGroupBookingPerson
                {
                    Name = "عميل AI الثاني",
                    PhoneNumber = "01099990000"
                }
            ],
            Timezone = "Africa/Cairo"
        });
        var publicTask = publicController.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = group.Id,
            CustomerName = "عميل عام",
            CustomerPhone = "01077778888"
        });
        await Task.WhenAll(aiTask, publicTask);
        var aiResult = await aiTask;
        var publicResult = await publicTask;

        Assert.True(lockBarrier.Arrivals >= 2);
        var successCount = Convert.ToInt32(aiResult.Succeeded) +
                           Convert.ToInt32(publicResult is OkObjectResult);
        Assert.Equal(1, successCount);
        await using var verificationDb = postgres.CreateContext(Tenant(projectId));
        var booking = Assert.Single(await verificationDb.GroupAppointmentBookings.AsNoTracking().ToListAsync());
        var customer = Assert.Single(await verificationDb.Customers.AsNoTracking().ToListAsync());
        Assert.Equal(customer.Id, booking.CustomerId);
        await AssertBookingOutcomeAsync(verificationDb, projectId, booking, customer);
        Assert.Null(broadcastProbe.Failure);
        Assert.True(broadcastProbe.AllBroadcastsObservedCommitted);
        Assert.Equal(aiResult.Succeeded ? 1 : 0, broadcastProbe.BroadcastCount);
    }

    [Fact]
    public async Task Ai_orchestrator_with_two_people_uses_final_slot_once_and_broadcasts_after_commit()
    {
        var projectId = Guid.NewGuid();
        var group = await SeedAvailableGroupAsync(projectId, capacity: 1);
        var indexEventProbe = new CommitObservingEventBus(postgres, projectId);
        await using var aiDb = postgres.CreateContextWithEventBus(Tenant(projectId), indexEventProbe);
        var (hub, broadcastProbe) = CommitObservingHub(projectId);
        var orchestrator = new AiGroupBookingOrchestrator(
            aiDb,
            new GroupBookingCoordinator(aiDb, indexEventProbe),
            hub,
            NullLogger<AiGroupBookingOrchestrator>.Instance);

        var result = await orchestrator.BookSuggestedPeopleAsync(new AiGroupBookingRequest
        {
            ProjectId = projectId,
            GroupId = group.Id,
            SuggestedPeople =
            [
                new SuggestedGroupBookingPerson { Name = "الشخص الأول", PhoneNumber = "01044445555" },
                new SuggestedGroupBookingPerson { Name = "الشخص الثاني", PhoneNumber = "01066667777" }
            ],
            Timezone = "Africa/Cairo"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(AiGroupBookingFailure.GroupFull, result.Failure);
        Assert.Equal(["الشخص الأول"], result.BookedPeople);
        Assert.Equal(["الشخص الثاني"], result.UnbookedPeople);
        Assert.Contains("الشخص الأول", result.CustomerReplyOverride, StringComparison.Ordinal);
        Assert.Contains("الشخص الثاني", result.CustomerReplyOverride, StringComparison.Ordinal);
        Assert.Equal(0, result.DisplayedRemainingPlaces);
        await using var verificationDb = postgres.CreateContext(Tenant(projectId));
        var booking = Assert.Single(await verificationDb.GroupAppointmentBookings.AsNoTracking().ToListAsync());
        var customer = Assert.Single(await verificationDb.Customers.AsNoTracking().ToListAsync());
        Assert.Equal(customer.Id, booking.CustomerId);
        await AssertBookingOutcomeAsync(verificationDb, projectId, booking, customer);
        Assert.Equal(1, broadcastProbe.BroadcastCount);
        Assert.True(broadcastProbe.AllBroadcastsObservedCommitted);
        Assert.Null(broadcastProbe.Failure);
        Assert.Equal(1, indexEventProbe.CustomerIndexEventCount);
        Assert.True(indexEventProbe.AllCustomerEventsObservedCommitted);
        Assert.Null(indexEventProbe.Failure);
    }

    [Fact]
    public async Task Legacy_arabic_formatted_phone_is_deduplicated_by_postgres_normalization()
    {
        var projectId = Guid.NewGuid();
        var group = await SeedAvailableGroupAsync(projectId, capacity: 1);
        var tenant = Tenant(projectId);
        await using (var seedDb = postgres.CreateContext(tenant))
        {
            var customer = Customer(projectId, "٠١٠ ١٢٣٤ ٥٦٧٨");
            seedDb.AddRange(customer, Booking(projectId, group.Id, customer, "٠١٠ (١٢٣٤) ٥٦٧٨"));
            await seedDb.SaveChangesAsync();
        }

        await using var bookingDb = postgres.CreateContext(Tenant(projectId));
        var controller = Controller(bookingDb, Tenant(projectId));
        var response = await controller.BookGroupSlot(new PublicBookRequest
        {
            ProjectId = projectId,
            GroupAppointmentId = group.Id,
            CustomerName = "عميل قديم",
            CustomerPhone = "+20 (10) 1234-5678"
        });

        Assert.IsType<OkObjectResult>(response);
        await using var verificationDb = postgres.CreateContext(Tenant(projectId));
        var customerAfterRetry = await verificationDb.Customers.SingleAsync();
        var bookingAfterRetry = await verificationDb.GroupAppointmentBookings.SingleAsync();
        Assert.Equal("201012345678", customerAfterRetry.PhoneNumber);
        Assert.Equal("201012345678", bookingAfterRetry.CustomerPhone);
        Assert.Equal(customerAfterRetry.Id, bookingAfterRetry.CustomerId);
        Assert.Empty(await verificationDb.NotificationAlerts.ToListAsync());
    }

    [Theory]
    [InlineData("010-1234-5678")]
    [InlineData("٠١٠ (١٢٣٤) ٥٦٧٨")]
    [InlineData("۰۱۰ ۱۲۳۴ ۵۶۷۸")]
    [InlineData("+20 (10) 1234-5678")]
    [InlineData("010+12345678")]
    [InlineData("++201012345678")]
    [InlineData("+001234567")]
    [InlineData("01234567")]
    [InlineData("1234567890123456")]
    [InlineData("phone: 01012345678")]
    [InlineData("201012345678@lid")]
    [InlineData("０１０１２３４５６７８")]
    [InlineData("०१०१२३४५६७८")]
    [InlineData("010 12345678")]
    public async Task PostgreSql_canonical_function_matches_application_identity(string input)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();

        var databaseValue = await db.Database.SqlQueryRaw<string>(
            "SELECT COALESCE(public.canonical_group_booking_phone_v1({0}), '<NULL>') AS \"Value\"",
            input).SingleAsync();

        Assert.Equal(GroupBookingPhone.Normalize(input) ?? "<NULL>", databaseValue);
    }

    private async Task<GroupAppointment> SeedAvailableGroupAsync(Guid projectId, int capacity)
    {
        var tenant = Tenant(projectId);
        await using var seedDb = postgres.CreateContext(tenant);
        await seedDb.Database.MigrateAsync();
        var group = new GroupAppointment
        {
            ProjectId = projectId,
            Name = "مجموعة اختبار التزامن",
            Capacity = capacity,
            IsActive = true,
            DateTime = DateTime.UtcNow.AddDays(2)
        };
        seedDb.AddRange(group, new ProjectSettings
        {
            ProjectId = projectId,
            IsGroupAppointmentsEnabled = true,
            Timezone = "Africa/Cairo"
        });
        await seedDb.SaveChangesAsync();
        return group;
    }

    private static int CountSuccessfulBookings(IActionResult manualResponse, IActionResult publicResponse)
    {
        var manualSucceeded = manualResponse is ObjectResult { StatusCode: StatusCodes.Status201Created };
        var publicSucceeded = publicResponse is OkObjectResult;
        return Convert.ToInt32(manualSucceeded) + Convert.ToInt32(publicSucceeded);
    }

    private async Task AssertBookingOutcomeAsync(
        AppDbContext db,
        Guid projectId,
        GroupAppointmentBooking booking,
        Customer customer)
    {
        var messages = await db.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType == "BookingChanged.v2")
            .ToListAsync();
        var projectMessages = messages
            .Select(message => new
            {
                Message = message,
                Payload = JsonSerializer.Deserialize<AdvertisingBookingOutcomeChanged>(message.PayloadJson)
            })
            .Where(item => item.Payload?.ProjectId == projectId)
            .ToList();
        var outcome = Assert.Single(projectMessages);

        Assert.Equal(2, outcome.Message.SchemaVersion);
        Assert.NotNull(outcome.Payload);
        Assert.Equal(booking.Id, outcome.Payload.BookingId);
        Assert.Equal(customer.Id, outcome.Payload.CustomerId);
        Assert.Equal("Confirmed", outcome.Payload.State);
        Assert.False(outcome.Payload.IsPaid);
        Assert.False(outcome.Payload.IsAttended);
    }

    private (IHubContext<NotificationHub> Hub, CommitObservingClientProxy Probe) CommitObservingHub(Guid projectId)
    {
        var probe = new CommitObservingClientProxy(async (method, payload, cancellationToken) =>
        {
            if (!string.Equals(method, "GroupBookingUpdated", StringComparison.Ordinal) || payload.Length != 1)
            {
                return false;
            }

            var bookingIdProperty = payload[0]?.GetType().GetProperty("bookingId");
            if (bookingIdProperty?.GetValue(payload[0]) is not Guid bookingId)
            {
                return false;
            }

            await using var verificationDb = postgres.CreateContext(Tenant(projectId));
            return await verificationDb.GroupAppointmentBookings
                .AsNoTracking()
                .AnyAsync(booking => booking.Id == bookingId, cancellationToken);
        });
        var clients = Proxy<IHubClients>((method, _) =>
            method.ReturnType == typeof(IClientProxy) ? probe : Default(method.ReturnType));
        var groups = Proxy<IGroupManager>((method, _) =>
            method.ReturnType == typeof(Task) ? Task.CompletedTask : Default(method.ReturnType));
        var hub = Proxy<IHubContext<NotificationHub>>((method, _) =>
            method.Name == "get_Clients" ? clients : groups);
        return (hub, probe);
    }

    private static GroupAppointmentsController Controller(AppDbContext db, TenantContext tenant)
    {
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
                HttpContext = new DefaultHttpContext { User = User(projectId: tenant.ProjectId) }
            }
        };
        return controller;
    }

    private static TenantContext Tenant(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return tenant;
    }

    private static Customer Customer(Guid projectId, string phone) => new()
    {
        ProjectId = projectId,
        PhoneNumber = phone,
        Name = "عميل قديم",
        City = string.Empty,
        Tags = ["حجز مجموعة"],
        Notes = "حجز قديم"
    };

    private static GroupAppointmentBooking Booking(
        Guid projectId,
        Guid groupId,
        Customer customer,
        string phone) => new()
        {
            ProjectId = projectId,
            GroupAppointmentId = groupId,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerPhone = phone
        };

    private static ClaimsPrincipal User(Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, "Owner"),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));

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
        var database = Proxy<IDatabase>((method, _) =>
            method.ReturnType == typeof(Task<RedisValue[]>)
                ? Task.FromResult(Array.Empty<RedisValue>())
                : Default(method.ReturnType));
        return Proxy<IConnectionMultiplexer>((method, _) =>
            method.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? database
                : Default(method.ReturnType));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> invocation) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Invocation = invocation;
        return proxy;
    }

    private static object? Default(Type type) =>
        type.IsValueType ? Activator.CreateInstance(type) : null;

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Invocation { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod == null ? null : Invocation(targetMethod, args);
    }

    private sealed class CommitObservingClientProxy(
        Func<string, object?[], CancellationToken, Task<bool>> observeCommit) : IClientProxy
    {
        public int BroadcastCount { get; private set; }
        public bool AllBroadcastsObservedCommitted { get; private set; } = true;
        public Exception? Failure { get; private set; }

        public async Task SendCoreAsync(
            string method,
            object?[] args,
            CancellationToken cancellationToken = default)
        {
            BroadcastCount++;
            try
            {
                AllBroadcastsObservedCommitted &= await observeCommit(method, args, cancellationToken);
            }
            catch (Exception exception)
            {
                Failure = exception;
                AllBroadcastsObservedCommitted = false;
            }
        }
    }

    private sealed class CommitObservingEventBus(PostgresFixture fixture, Guid projectId) : IEventBus
    {
        public int CustomerIndexEventCount { get; private set; }
        public bool AllCustomerEventsObservedCommitted { get; private set; } = true;
        public Exception? Failure { get; private set; }

        public async Task PublishAsync<T>(T @event) where T : IntegrationEvent
        {
            if (@event is not EntityIndexedEvent { EntityType: "Customer" } customerEvent)
            {
                return;
            }

            CustomerIndexEventCount++;
            try
            {
                await using var verificationDb = fixture.CreateContext(Tenant(projectId));
                AllCustomerEventsObservedCommitted &= await verificationDb.Customers
                    .AsNoTracking()
                    .AnyAsync(customer => customer.Id == customerEvent.EntityId);
            }
            catch (Exception exception)
            {
                Failure = exception;
                AllCustomerEventsObservedCommitted = false;
            }
        }

        public void Subscribe<T, THandler>(int consumerCount = 1)
            where T : IntegrationEvent
            where THandler : IIntegrationEventHandler<T>
        {
        }
    }

    private sealed class GroupLockBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothTransactionsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public int Arrivals => _arrivals;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _bothTransactionsReady.TrySetResult();
            }
            await _bothTransactionsReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
