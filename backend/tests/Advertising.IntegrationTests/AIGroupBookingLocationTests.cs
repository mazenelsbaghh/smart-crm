using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Brain.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AIGroupBookingLocationTests(AIReplySideEffectsDatabase database)
    : IClassFixture<AIReplySideEffectsDatabase>
{
    private const string ModelConfirmation = "تم تأكيد الحجز بنجاح في المجموعة المطلوبة.";

    [Theory]
    [InlineData("Cairo", null, "Unknown", "offline", "احجزلي المجموعة دي", false, false)]
    [InlineData("Alexandria", "القاهرة", "Unknown", "offline", "أنا ساكن في القاهرة، احجزلي المجموعة دي", false, false)]
    [InlineData("Alexandria", null, "Online", "offline", "عايز أحجز أونلاين", false, false)]
    [InlineData("", null, "Offline", "offline", "عايز أحجز في السنتر", false, true)]
    [InlineData("", null, "Online", "online", "عايز أحجز أونلاين", true, false)]
    [InlineData("Cairo", null, "Offline", "online", "أنا من القاهرة، احجزلي المجموعة دي", true, false)]
    [InlineData("الإسكندرية", null, "Offline", "offline", "عايز أحجز في السنتر", true, false)]
    public async Task Customer_location_and_attendance_are_enforced_before_saving_a_suggested_booking(
        string savedCity, string? extractedCity, string attendanceMode, string groupMode,
        string customerMessage, bool canBook, bool needsCity)
    {
        await using var scenario = await SeedAsync(savedCity, groupMode, customerMessage);
        var analysis = new MarketingAnalysisResult
        {
            ReplyContent = ModelConfirmation,
            SuggestedGroupBookingId = scenario.Group.Id.ToString(),
            SuggestedGroupBookingPeople = [new() { IsRequester = true }],
            AttendanceMode = attendanceMode,
            Entities = new CRMEntities { City = extractedCity! },
            SuggestedFollowUp = new()
            {
                Needed = true, Type = "AppointmentReminder",
                AppointmentTime = scenario.Group.DateTime.ToString("O"),
                DueDate = scenario.Group.DateTime.AddHours(-1).ToString("O")
            }
        };
        var bus = new RecordingEventBus();
        using var services = WorkerServices(scenario);
        var worker = new AIReplyWorker(services, Brain(analysis), bus, NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = scenario.ProjectId, ConversationId = scenario.Conversation.Id,
            Sender = scenario.Customer.FacebookPSID!, Channel = "Messenger",
            Content = customerMessage, SourceMessageTimestampUtc = scenario.Incoming.Timestamp
        });

        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var bookings = await verification.GroupAppointmentBookings.AsNoTracking().ToListAsync();
        var reply = Assert.Single(bus.Events.OfType<AIReplyGeneratedEvent>());
        Assert.Equal(canBook, Assert.Single(bus.Events.OfType<CRMUpdateSuggestedEvent>()).FollowUpNeeded);
        if (canBook)
        {
            var booking = Assert.Single(bookings);
            Assert.Equal(scenario.Group.Id, booking.GroupAppointmentId);
            Assert.Equal(scenario.Customer.Id, booking.CustomerId);
            Assert.Equal(ModelConfirmation, reply.Content);
        }
        else
        {
            Assert.Empty(bookings);
            Assert.NotEqual(ModelConfirmation, reply.Content);
            Assert.DoesNotContain("تم تأكيد الحجز", reply.Content);
            if (needsCity) Assert.Matches("مدينة|محافظة|منين|ساكن|بتسكن|مقيم", reply.Content);
        }
    }

    [Theory]
    [InlineData("Offline")]
    [InlineData("Unknown")]
    public async Task A_Cairo_customer_asking_for_different_dates_receives_online_schedule_guidance(string attendanceMode)
    {
        const string customerMessage = "المواعيد مش مناسبة";
        await using var scenario = await SeedAsync("القاهرة", "online", customerMessage);
        var analysis = new MarketingAnalysisResult
        {
            ReplyContent = "ممكن نختار ميعاد تاني مناسب لحضرتك.",
            Intent = "inquiry", AttendanceMode = attendanceMode
        };
        var bus = new RecordingEventBus();
        using var services = WorkerServices(scenario);
        var worker = new AIReplyWorker(services, Brain(analysis), bus, NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = scenario.ProjectId, ConversationId = scenario.Conversation.Id,
            Sender = scenario.Customer.FacebookPSID!, Channel = "Messenger",
            Content = customerMessage, SourceMessageTimestampUtc = scenario.Incoming.Timestamp
        });

        var reply = Assert.Single(bus.Events.OfType<AIReplyGeneratedEvent>());
        Assert.Matches("[أا]ونلاين|[Oo]nline", reply.Content);
        Assert.DoesNotMatch("سنتر|[أا]وفلاين|[Oo]ffline", reply.Content);
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        Assert.Empty(await verification.GroupAppointmentBookings.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Booking_orchestrator_rejects_offline_for_a_saved_non_Alexandria_customer_without_the_worker()
    {
        await using var scenario = await SeedAsync("القاهرة", "offline", "احجزلي المجموعة دي");
        var orchestrator = new AiGroupBookingOrchestrator(
            scenario.Db, new GroupBookingCoordinator(scenario.Db), Hub(), NullLogger<AiGroupBookingOrchestrator>.Instance);

        var result = await orchestrator.BookSuggestedPeopleAsync(new AiGroupBookingRequest
        {
            ProjectId = scenario.ProjectId, GroupId = scenario.Group.Id,
            RequesterCustomerId = scenario.Customer.Id, SuggestedPeople = [new() { IsRequester = true }]
        });

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.CustomerReplyOverride));
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        Assert.Empty(await verification.GroupAppointmentBookings.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("الإسكندرية", true)]
    [InlineData("القاهرة", false)]
    public async Task A_friend_booking_uses_the_friends_explicit_city_instead_of_the_requesters_city(
        string friendCity, bool canBook)
    {
        await using var scenario = await SeedAsync("القاهرة", "offline", $"احجز لصاحبي في السنتر، هو مقيم في {friendCity}");
        var analysis = new MarketingAnalysisResult
        {
            ReplyContent = ModelConfirmation, SuggestedGroupBookingId = scenario.Group.Id.ToString(),
            AttendanceMode = "Offline", SuggestedGroupBookingPeople =
            [new() { IsRequester = false, Name = "Friend", PhoneNumber = "201022223333", City = friendCity }]
        };
        var bus = new RecordingEventBus();
        using var services = WorkerServices(scenario);
        var worker = new AIReplyWorker(services, Brain(analysis), bus, NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = scenario.ProjectId, ConversationId = scenario.Conversation.Id,
            Sender = scenario.Customer.FacebookPSID!, Channel = "Messenger", Content = scenario.Incoming.Content,
            SourceMessageTimestampUtc = scenario.Incoming.Timestamp
        });

        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var bookings = await verification.GroupAppointmentBookings.AsNoTracking().ToListAsync();
        var reply = Assert.Single(bus.Events.OfType<AIReplyGeneratedEvent>());
        if (canBook)
        {
            var booking = Assert.Single(bookings);
            Assert.Equal("201022223333", booking.CustomerPhone);
            Assert.NotEqual(scenario.Customer.Id, booking.CustomerId);
            Assert.Equal(scenario.Group.Id, booking.GroupAppointmentId);
            Assert.Equal(ModelConfirmation, reply.Content);
        }
        else
        {
            Assert.Empty(bookings);
            Assert.NotEqual(ModelConfirmation, reply.Content);
        }
        Assert.Equal("القاهرة", (await verification.Customers.SingleAsync(customer => customer.Id == scenario.Customer.Id)).City);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ai_booking_by_phone_cannot_create_or_transfer_a_Cairo_customer_to_an_offline_group(bool hasOnlineBooking)
    {
        await using var scenario = await SeedAsync("القاهرة", "offline", "احجز لصاحبي");
        GroupAppointmentBooking? originalBooking = null;
        if (hasOnlineBooking)
        {
            var onlineGroup = new GroupAppointment
            {
                ProjectId = scenario.ProjectId, Name = "Existing online group", Mode = "online",
                Capacity = 10, DateTime = DateTime.UtcNow.AddDays(6)
            };
            originalBooking = new()
            {
                ProjectId = scenario.ProjectId, GroupAppointmentId = onlineGroup.Id, CustomerId = scenario.Customer.Id,
                CustomerName = scenario.Customer.Name, CustomerPhone = scenario.Customer.PhoneNumber
            };
            scenario.Db.AddRange(onlineGroup, originalBooking);
            await scenario.Db.SaveChangesAsync();
        }
        var originalGroupId = originalBooking?.GroupAppointmentId;
        var originalCustomerName = scenario.Customer.Name;

        var result = await new GroupBookingCoordinator(scenario.Db).BookAsync(new GroupBookingCommand
        {
            ProjectId = scenario.ProjectId, GroupId = scenario.Group.Id,
            CustomerName = "Name that must not overwrite the customer", CustomerPhone = scenario.Customer.PhoneNumber,
            Origin = GroupBookingOrigin.Ai, ExistingBookingPolicy = ExistingGroupBookingPolicy.Transfer
        });

        Assert.False(result.Succeeded);
        await using var verification = database.Database.CreateContext(scenario.Tenant);
        var bookings = await verification.GroupAppointmentBookings.AsNoTracking().ToListAsync();
        if (originalBooking is null) Assert.Empty(bookings);
        else
        {
            var saved = Assert.Single(bookings);
            Assert.Equal(originalBooking.Id, saved.Id);
            Assert.Equal(originalGroupId, saved.GroupAppointmentId);
            Assert.Equal(originalCustomerName, saved.CustomerName);
        }
        Assert.Equal(originalCustomerName, (await verification.Customers.SingleAsync()).Name);
    }

    private async Task<BookingScenario> SeedAsync(string city, string mode, string content)
    {
        var project = new Project { Name = "AI booking location regression" };
        var tenant = new TenantContext();
        tenant.SetProjectId(project.Id);
        var db = database.Database.CreateContext(tenant);
        var customer = new Customer
        {
            ProjectId = project.Id, Name = "Booking requester", PhoneNumber = "201011112222",
            FacebookPSID = Guid.NewGuid().ToString(), City = city
        };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id, Channel = "Messenger" };
        var incoming = new Message
        {
            ConversationId = conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(),
            Direction = "Incoming", MessageType = "Text", Content = content, Timestamp = DateTime.UtcNow.AddSeconds(-10)
        };
        var group = new GroupAppointment
        {
            ProjectId = project.Id, Name = "Requested group", Mode = mode,
            Capacity = 10, DateTime = DateTime.UtcNow.AddDays(5), IsActive = true
        };
        var behavior = new AIBehaviorSettingsService();
        var settings = new ProjectSettings
        {
            ProjectId = project.Id, IsGroupAppointmentsEnabled = true, MessengerAiAutoReplyEnabled = true,
            CustomerReplyProvider = "OpenAI", CustomerReplyModel = "test-model", MaxDailyMessages = 0,
            AiBehaviorSettingsJson = behavior.Serialize(new AIBehaviorSettings { MessengerWhatsAppTransitionEnabled = false })
        };
        db.AddRange(project, customer, conversation, incoming, group, settings,
            new WhatsAppAccount { Id = project.Id, ProjectId = project.Id, Name = "Default", IsDefault = true });
        await db.SaveChangesAsync();
        return new(db, tenant, project.Id, customer, conversation, incoming, group);
    }

    private static ServiceProvider WorkerServices(BookingScenario scenario)
    {
        var gemini = Proxy<IGeminiClient>((method, _) => throw new InvalidOperationException(method.Name));
        var redis = Proxy<IConnectionMultiplexer>((method, _) => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Isolated booking test has no Redis"));
        var protection = new EphemeralDataProtectionProvider();
        var configuration = new ConfigurationBuilder().Build();
        var hub = Hub();
        var coordinator = new GroupBookingCoordinator(scenario.Db);
        return new ServiceCollection()
            .AddSingleton(scenario.Db).AddSingleton<ITenantContext>(scenario.Tenant)
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IAIBehaviorSettingsService>(new AIBehaviorSettingsService())
            .AddSingleton<IProjectSecretVault>(new ProjectSecretVault(protection))
            .AddSingleton(gemini).AddSingleton<IAICompanyBrain>(new AICompanyBrain(scenario.Db, gemini))
            .AddSingleton(new AiGenerationCache(redis, protection, NullLogger<AiGenerationCache>.Instance))
            .AddSingleton(new WhatsAppAccountService(scenario.Db))
            .AddSingleton(new WhatsAppGatewaySessionClient(new HttpClient(new DisconnectedGateway()), configuration))
            .AddSingleton(hub).AddSingleton(coordinator)
            .AddSingleton(new AiGroupBookingOrchestrator(scenario.Db, coordinator, hub,
                NullLogger<AiGroupBookingOrchestrator>.Instance, new RegisteredNumber()))
            .BuildServiceProvider();
    }

    private static IAIMarketingBrain Brain(MarketingAnalysisResult analysis) => Proxy<IAIMarketingBrain>((method, _) => method.Name switch
    {
        nameof(IAIMarketingBrain.BuildStaticPrompt) => "Test system context",
        nameof(IAIMarketingBrain.AnalyzeAndGenerateReplyAsync) => Task.FromResult(analysis),
        _ => throw new InvalidOperationException(method.Name)
    });

    private static IHubContext<NotificationHub> Hub()
    {
        var client = Proxy<IClientProxy>((method, _) => method.Name == nameof(IClientProxy.SendCoreAsync)
            ? Task.CompletedTask : throw new InvalidOperationException(method.Name));
        var clients = Proxy<IHubClients>((method, _) => method.Name == nameof(IHubClients.Group)
            ? client : throw new InvalidOperationException(method.Name));
        return Proxy<IHubContext<NotificationHub>>((method, _) => method.Name == "get_Clients"
            ? clients : throw new InvalidOperationException(method.Name));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, BoundaryProxy>();
        ((BoundaryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class BoundaryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private sealed record BookingScenario(AppDbContext Db, TenantContext Tenant, Guid ProjectId,
        Customer Customer, Conversation Conversation, Message Incoming, GroupAppointment Group) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IntegrationEvent> Events { get; } = [];
        public Task PublishAsync<T>(T message) where T : IntegrationEvent
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
        public void Subscribe<T, TH>(int consumers = 1) where T : IntegrationEvent where TH : IIntegrationEventHandler<T> { }
    }

    private sealed class DisconnectedGateway : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Unexpected gateway send");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"status\":\"Disconnected\"}") });
        }
    }

    private sealed class RegisteredNumber : IWhatsAppBookingNumberVerifier
    {
        public Task<WhatsAppNumberVerification> VerifyAsync(Guid projectId, string phone, CancellationToken cancellationToken = default) =>
            Task.FromResult(WhatsAppNumberVerification.Registered);
    }
}
