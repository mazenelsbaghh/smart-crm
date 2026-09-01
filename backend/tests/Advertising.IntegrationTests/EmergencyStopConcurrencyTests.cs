using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Npgsql;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class EmergencyStopConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task August_2026_concurrent_activation_creates_one_stop_and_one_pause_command_set()
    {
        var projectId = Guid.NewGuid();
        await SeedActiveManagedAdAsync(projectId);
        await using var firstDb = postgres.CreateContext();
        await using var secondDb = postgres.CreateContext();
        var firstService = new AdvertisingEmergencyStopService(
            firstDb, new AdvertisingOwnershipPolicy(firstDb));
        var secondService = new AdvertisingEmergencyStopService(
            secondDb, new AdvertisingOwnershipPolicy(secondDb));

        var activations = await Task.WhenAll(
            firstService.ActivateAsync(projectId, EmergencyTrigger.Manual, "first"),
            secondService.ActivateAsync(projectId, EmergencyTrigger.Manual, "second"));

        Assert.Single(activations, activation => !activation.AlreadyActive);
        Assert.Single(activations, activation => activation.AlreadyActive);
        Assert.Single(activations.Select(activation => activation.StopId).Distinct());
        await using var verify = postgres.CreateContext();
        Assert.Single(await verify.AdvertisingEmergencyStops.IgnoreQueryFilters()
            .Where(stop => stop.ProjectId == projectId && stop.ResumedAtUtc == null).ToListAsync());
        Assert.Single(await verify.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .Where(command => command.ProjectId == projectId
                && command.IdempotencyKey.StartsWith("emergency:")).ToListAsync());
    }

    [Fact]
    public async Task August_2026_resume_waits_for_worker_progress_lock_and_remains_resumed()
    {
        var projectId = Guid.NewGuid();
        var commandId = await SeedRecoverableStopAsync(projectId);
        var workerProgressBarrier = new WorkerProgressSaveBarrier();
        await using var workerDb = postgres.CreateContext(null, workerProgressBarrier);
        using var metaHttp = new HttpClient();
        var workerOwnership = new AdvertisingOwnershipPolicy(workerDb);
        var workerEmergencyStops = new AdvertisingEmergencyStopService(workerDb, workerOwnership);
        var worker = new AdvertisingCommandWorker(
            workerDb,
            new MetaAdsClient(metaHttp, Options.Create(new AdvertisingOptions())),
            new AdvertisingSecretVault(new EphemeralDataProtectionProvider()),
            new AdvertisingSafetyEngine(workerDb),
            workerEmergencyStops,
            workerOwnership);
        var workerProgress = worker.ExecuteAsync(projectId, commandId);
        await workerProgressBarrier.ProgressSaveReached.WaitAsync(TimeSpan.FromSeconds(10));

        var resumeApplicationName = $"resume-lock-{Guid.NewGuid():N}";
        var resumeConnection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            ApplicationName = resumeApplicationName,
            Pooling = false
        };
        await using var resumeDb = PostgresFixture.CreateContext(resumeConnection.ConnectionString);
        var resume = new AdvertisingEmergencyStopService(
            resumeDb, new AdvertisingOwnershipPolicy(resumeDb))
            .ResumeAsync(projectId, Guid.NewGuid());

        bool observedAdvisoryWait;
        try
        {
            observedAdvisoryWait = await WaitForAdvisoryLockAsync(resumeApplicationName);
        }
        finally
        {
            workerProgressBarrier.Release();
        }

        await Task.WhenAll(workerProgress, resume);
        Assert.True(observedAdvisoryWait);
        await using var verify = postgres.CreateContext();
        var persistedStop = await verify.AdvertisingEmergencyStops.IgnoreQueryFilters()
            .SingleAsync(stop => stop.ProjectId == projectId);
        Assert.Equal("Resumed", persistedStop.State);
        Assert.NotNull(persistedStop.ResumedAtUtc);
    }

    private async Task<Guid> SeedRecoverableStopAsync(Guid projectId)
    {
        await using var seed = postgres.CreateContext();
        await seed.Database.MigrateAsync();
        var nowUtc = DateTime.UtcNow;
        var connection = new AdvertisingConnection
        {
            ProjectId = projectId,
            State = AdvertisingConnectionState.Ready
        };
        var stop = new EmergencyStopRecord
        {
            ProjectId = projectId,
            Trigger = EmergencyTrigger.Manual,
            Reason = "concurrency regression",
            ActivatedAtUtc = nowUtc.AddMinutes(-5),
            State = "PausingManaged"
        };
        var ownership = new ManagedOwnershipRecord
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            ProviderCampaignExternalId = $"campaign-{projectId:N}",
            OwnershipKind = ManagedOwnershipKind.AutopilotCreated
        };
        var advertisement = new ManagedAdvertisement
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            OwnershipRecordId = ownership.Id,
            AdExternalId = $"ad-{projectId:N}",
            DestinationType = "WHATSAPP",
            PublisherPlatform = "AdvantagePlus",
            PositionsJson = "[]",
            ConfiguredStatus = ManagedDeliveryState.Paused,
            EffectiveStatus = "PAUSED"
        };
        var decision = new AdvertisingDecision
        {
            ProjectId = projectId,
            ActionType = "PauseDelivery",
            TargetType = "EmergencyManagedSet",
            EvidenceStartUtc = nowUtc.AddMinutes(-5),
            EvidenceEndUtc = nowUtc,
            State = DecisionState.Approved
        };
        var command = new ExecutionCommand
        {
            ProjectId = projectId,
            DecisionId = decision.Id,
            IdempotencyKey = $"emergency:{stop.Id:N}:Ad:{advertisement.AdExternalId}",
            CommandType = "PauseAd",
            TargetExternalId = advertisement.AdExternalId,
            DesiredStateJson = JsonSerializer.Serialize(new
            {
                adId = advertisement.Id,
                status = "PAUSED",
                resourceType = "Ad",
                stopId = stop.Id
            }),
            RequestFingerprint = "worker-lock-regression",
            State = CommandState.Succeeded,
            CompletedAtUtc = nowUtc
        };
        seed.AddRange(
            connection,
            stop,
            ownership,
            advertisement,
            decision,
            command,
            new TrackingHealthSnapshot
            {
                ProjectId = projectId,
                ConnectionId = Guid.NewGuid(),
                DestinationId = Guid.NewGuid(),
                TrackingHealthPolicyId = Guid.NewGuid(),
                TrackingHealthPolicyVersion = 1,
                WindowStartUtc = nowUtc.AddDays(-1),
                WindowEndUtc = nowUtc,
                State = TrackingHealthState.Healthy,
                EvaluatedAtUtc = nowUtc
            },
            new BudgetPeriodLedger
            {
                ProjectId = projectId,
                EnvelopeId = Guid.NewGuid(),
                PeriodKind = "Daily",
                PeriodStartUtc = nowUtc.AddHours(-1),
                PeriodEndUtc = nowUtc.AddHours(1),
                LastReconciledAtUtc = nowUtc
            });
        await seed.SaveChangesAsync();
        return command.Id;
    }

    private async Task SeedActiveManagedAdAsync(Guid projectId)
    {
        await using var seed = postgres.CreateContext();
        await seed.Database.MigrateAsync();
        var connection = new AdvertisingConnection
        {
            ProjectId = projectId,
            State = AdvertisingConnectionState.Ready
        };
        var ownership = new ManagedOwnershipRecord
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            ProviderCampaignExternalId = $"campaign-{projectId:N}",
            OwnershipKind = ManagedOwnershipKind.AutopilotCreated
        };
        seed.AddRange(connection, ownership, new ManagedAdvertisement
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            OwnershipRecordId = ownership.Id,
            AdExternalId = $"ad-{projectId:N}",
            DestinationType = "WHATSAPP",
            PublisherPlatform = "AdvantagePlus",
            PositionsJson = "[]",
            ConfiguredStatus = ManagedDeliveryState.Active,
            EffectiveStatus = "ACTIVE"
        });
        await seed.SaveChangesAsync();
    }

    private async Task<bool> WaitForAdvisoryLockAsync(string applicationName)
    {
        await using var monitor = new NpgsqlConnection(postgres.ConnectionString);
        await monitor.OpenAsync();
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            await using var command = monitor.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE application_name = @application_name
                      AND wait_event_type = 'Lock'
                      AND wait_event = 'advisory');
                """;
            command.Parameters.AddWithValue("application_name", applicationName);
            if (await command.ExecuteScalarAsync() is true) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private sealed class WorkerProgressSaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _progressSaveReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hasBlocked;

        public Task ProgressSaveReached => _progressSaveReached.Task;

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hasBlocked, 1) == 0)
            {
                _progressSaveReached.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
