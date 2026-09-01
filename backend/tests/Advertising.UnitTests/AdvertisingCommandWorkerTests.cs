using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingCommandWorkerTests
{
    [Fact]
    public async Task Duplicate_execution_reads_preflight_and_performs_at_most_one_mutation()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false);

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);
        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        Assert.Equal(CommandState.Succeeded, setup.Command.State);
        Assert.Equal(1, setup.Handler.PostCalls);
        Assert.Equal(1, setup.Command.AttemptCount);
    }

    [Fact]
    public async Task Timeout_becomes_unknown_and_reconciles_by_read_without_blind_retry()
    {
        var setup = await SetupAsync("ACTIVE", failPost: true);

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);
        Assert.Equal(CommandState.Unknown, setup.Command.State);
        setup.Handler.Status = "PAUSED";
        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        Assert.Equal(CommandState.Succeeded, setup.Command.State);
        Assert.NotNull(setup.Command.ReconciledAtUtc);
        Assert.Equal(1, setup.Handler.PostCalls);
    }

    [Fact]
    public async Task Unknown_result_that_is_not_applied_requires_a_new_decision_identity()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false, CommandState.Unknown);

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        Assert.Equal(CommandState.Failed, setup.Command.State);
        Assert.Equal("ADS_UNKNOWN_RESULT_NOT_APPLIED_NEW_DECISION_REQUIRED", setup.Command.LastError);
        Assert.Equal(0, setup.Handler.PostCalls);
    }

    [Fact]
    public async Task Provider_target_must_match_the_owned_local_object()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false);
        setup.Command.TargetExternalId = "someone-elses-ad";

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        Assert.Equal(CommandState.Blocked, setup.Command.State);
        Assert.Equal("ADS_COMMAND_TARGET_MISMATCH", setup.Command.LastError);
        Assert.Equal(1, setup.Handler.PostCalls); // only the protective owned-ad pause
        var stop = await setup.WorkerDb.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(EmergencyTrigger.CrossProjectGuard, stop.Trigger);
    }

    [Fact]
    public async Task Protective_pause_reads_and_mutates_the_exact_hierarchy_target()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false);
        setup.Ad.CampaignExternalId = "campaign-1";
        setup.Command.CommandType = "PauseCampaign";
        setup.Command.TargetExternalId = setup.Ad.CampaignExternalId;
        setup.Command.DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            adId = setup.Ad.Id,
            status = "PAUSED",
            resourceType = "Campaign"
        });
        await setup.WorkerDb.SaveChangesAsync();

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        Assert.Equal(CommandState.Succeeded, setup.Command.State);
        Assert.Contains(setup.Handler.Requests, request =>
            request.Method == HttpMethod.Get && request.Path.Contains("campaign-1", StringComparison.Ordinal));
        Assert.Contains(setup.Handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Path.Contains("campaign-1", StringComparison.Ordinal));
        Assert.DoesNotContain(setup.Handler.Requests, request =>
            request.Path.Contains("ad-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Emergency_pause_success_persists_final_progress_and_duplicate_worker_retry_cannot_regress_it()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false);
        var stop = new EmergencyStopRecord
        {
            ProjectId = setup.ProjectId,
            Trigger = EmergencyTrigger.Manual,
            Reason = "test",
            ActivatedAtUtc = DateTime.UtcNow,
            State = "PausingManaged"
        };
        setup.Ad.AdSetExternalId = "adset-1";
        setup.Command.IdempotencyKey = $"emergency:{stop.Id:N}:Ad:{setup.Ad.AdExternalId}";
        setup.Command.DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            adId = setup.Ad.Id,
            status = "PAUSED",
            resourceType = "Ad",
            stopId = stop.Id
        });
        var second = new ExecutionCommand
        {
            ProjectId = setup.ProjectId,
            DecisionId = setup.Command.DecisionId,
            IdempotencyKey = $"emergency:{stop.Id:N}:AdSet:{setup.Ad.AdSetExternalId}",
            CommandType = "PauseAdSet",
            TargetExternalId = setup.Ad.AdSetExternalId,
            DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                adId = setup.Ad.Id,
                status = "PAUSED",
                resourceType = "AdSet",
                stopId = stop.Id
            }),
            RequestFingerprint = "second"
        };
        setup.WorkerDb.AddRange(stop, second);
        await setup.WorkerDb.SaveChangesAsync();

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);
        setup.WorkerDb.ChangeTracker.Clear();
        var pausingStop = await setup.WorkerDb.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stop.Id);
        Assert.Equal("PausingManaged", pausingStop.State);

        await setup.Worker.ExecuteAsync(setup.ProjectId, second.Id);
        setup.WorkerDb.ChangeTracker.Clear();
        var completedStop = await setup.WorkerDb.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stop.Id);
        Assert.Equal("Paused", completedStop.State);
        using (var completedProgress = System.Text.Json.JsonDocument.Parse(completedStop.ProgressJson))
        {
            Assert.False(completedProgress.RootElement.GetProperty("continuingSpend").GetBoolean());
        }

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);
        setup.WorkerDb.ChangeTracker.Clear();
        var retriedStop = await setup.WorkerDb.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stop.Id);
        Assert.Equal("Paused", retriedStop.State);
        using var retriedProgress = System.Text.Json.JsonDocument.Parse(retriedStop.ProgressJson);
        Assert.Equal(2, retriedProgress.RootElement.GetProperty("succeeded").GetInt32());
    }

    [Fact]
    public async Task Normal_disable_pause_success_persists_completed_state_from_worker()
    {
        var setup = await SetupAsync("ACTIVE", failPost: false);
        var request = new AutopilotDisableRequest
        {
            ProjectId = setup.ProjectId,
            Mode = AutopilotDisableMode.PauseManaged,
            RequestedByUserId = Guid.NewGuid(),
            RequestedAtUtc = DateTime.UtcNow,
            Reason = "test",
            State = "PausingManaged"
        };
        setup.Command.IdempotencyKey = $"disable:{request.Id:N}:Ad:{setup.Ad.AdExternalId}";
        setup.Command.DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            adId = setup.Ad.Id,
            status = "PAUSED",
            resourceType = "Ad",
            disableRequestId = request.Id
        });
        setup.WorkerDb.Add(request);
        await setup.WorkerDb.SaveChangesAsync();

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        setup.WorkerDb.ChangeTracker.Clear();
        var persistedRequest = await setup.WorkerDb.AdvertisingDisableRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == request.Id);
        Assert.Equal("Completed", persistedRequest.State);
        Assert.NotNull(persistedRequest.CompletedAtUtc);
        using var completedProgress = System.Text.Json.JsonDocument.Parse(persistedRequest.ProgressJson);
        Assert.False(completedProgress.RootElement.GetProperty("deliveryMayContinue").GetBoolean());
    }

    [Fact]
    public async Task Terminal_worker_retry_never_rewrites_an_already_resumed_emergency_stop()
    {
        var setup = await SetupAsync("PAUSED", failPost: false, CommandState.Succeeded);
        var stop = new EmergencyStopRecord
        {
            ProjectId = setup.ProjectId,
            Trigger = EmergencyTrigger.Manual,
            Reason = "resumed",
            ActivatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ResumedAtUtc = DateTime.UtcNow,
            ResumedByUserId = Guid.NewGuid(),
            State = "Resumed"
        };
        setup.Command.IdempotencyKey = $"emergency:{stop.Id:N}:Ad:{setup.Ad.AdExternalId}";
        setup.Command.DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            adId = setup.Ad.Id,
            status = "PAUSED",
            resourceType = "Ad",
            stopId = stop.Id
        });
        setup.WorkerDb.Add(stop);
        await setup.WorkerDb.SaveChangesAsync();

        await setup.Worker.ExecuteAsync(setup.ProjectId, setup.Command.Id);

        setup.WorkerDb.ChangeTracker.Clear();
        var persistedStop = await setup.WorkerDb.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stop.Id);
        Assert.Equal("Resumed", persistedStop.State);
        Assert.NotNull(persistedStop.ResumedAtUtc);
    }

    private static async Task<SetupState> SetupAsync(string status, bool failPost,
        CommandState commandState = CommandState.Pending)
    {
        var projectId = Guid.NewGuid(); var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"command-{Guid.NewGuid():N}"));
        var vault = new AdvertisingSecretVault(DataProtectionProvider.Create(directory));
        var connection = new AdvertisingConnection { ProjectId = projectId, State = AdvertisingConnectionState.Ready,
            ProtectedAccessToken = vault.Protect("token") };
        var destination = new AuthorizedWhatsAppDestination { ProjectId = projectId, ConnectionId = connection.Id,
            WabaExternalId = "waba", PhoneNumberExternalId = "phone", State = AuthorizedDestinationState.Eligible };
        var ownership = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            ProviderCampaignExternalId = "campaign", OwnershipKind = ManagedOwnershipKind.AutopilotCreated };
        var ad = new ManagedAdvertisement { ProjectId = projectId, ConnectionId = connection.Id,
            OwnershipRecordId = ownership.Id, DestinationId = destination.Id, DestinationType = "WHATSAPP",
            PublisherPlatform = "AdvantagePlus", PositionsJson = "[]", DailyBudget = 100m,
            AdExternalId = "ad-1", ConfiguredStatus = ManagedDeliveryState.Active };
        var decision = new AdvertisingDecision { ProjectId = projectId, ActionType = "PauseAd", TargetType = "Ad",
            TargetId = ad.Id, EvidenceStartUtc = DateTime.UtcNow.AddHours(-1), EvidenceEndUtc = DateTime.UtcNow,
            State = DecisionState.Approved };
        var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id,
            IdempotencyKey = Guid.NewGuid().ToString("N"), CommandType = "PauseAd", TargetExternalId = ad.AdExternalId,
            DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new { adId = ad.Id, status = "PAUSED" }),
            RequestFingerprint = "fingerprint", State = commandState };
        db.AddRange(connection, destination, ownership, ad, decision, command); await db.SaveChangesAsync();
        var handler = new CommandHandler(status, failPost);
        var meta = new MetaAdsClient(new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/v26.0/") },
            Options.Create(new AdvertisingOptions { Enabled = true, Meta = new MetaOptions { UseMock = true } }));
        var emergency = new AdvertisingEmergencyStopService(db, new AdvertisingOwnershipPolicy(db));
        var ownershipPolicy = new AdvertisingOwnershipPolicy(db);
        return new(projectId, command, handler, db,
            ad, new AdvertisingCommandWorker(db, meta, vault, new AdvertisingSafetyEngine(db), emergency,
                ownershipPolicy));
    }

    private sealed class CommandHandler(string status, bool failPost) : HttpMessageHandler
    {
        public string Status { get; set; } = status;
        public int PostCalls { get; private set; }
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri?.PathAndQuery ?? string.Empty));
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Response(HttpStatusCode.OK, $"{{\"id\":\"ad-1\",\"status\":\"{Status}\",\"effective_status\":\"{Status}\"}}"));
            PostCalls++;
            if (failPost) throw new HttpRequestException("ambiguous timeout", null, HttpStatusCode.GatewayTimeout);
            Status = "PAUSED";
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"success\":true}"));
        }
        private static HttpResponseMessage Response(HttpStatusCode code, string body) => new(code)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed record SetupState(Guid ProjectId, ExecutionCommand Command, CommandHandler Handler,
        AppDbContext WorkerDb, ManagedAdvertisement Ad, AdvertisingCommandWorker Worker);
}
