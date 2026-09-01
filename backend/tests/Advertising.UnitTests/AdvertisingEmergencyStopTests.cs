using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingEmergencyStopTests
{
    [Fact]
    public async Task Stop_is_idempotent_blocks_pending_finance_and_pauses_only_owned_delivery()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var first = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.TrackingUnsafe, "tracking unsafe");
        var repeated = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.TrackingUnsafe, "tracking unsafe");

        Assert.False(first.AlreadyActive);
        Assert.True(repeated.AlreadyActive);
        Assert.Equal(first.StopId, repeated.StopId);
        Assert.Single(first.CommandIds);
        Assert.Equal(CommandState.Cancelled, setup.PendingFinancial.State);
        var pause = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == first.CommandIds[0]);
        Assert.Contains(setup.OwnedAd.Id.ToString(), pause.DesiredStateJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setup.ManualAd.Id.ToString(), pause.DesiredStateJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsafe_tracking_repeated_commands_lost_authorization_and_cross_project_are_closed_triggers()
    {
        Assert.Equal(EmergencyTrigger.TrackingUnsafe,
            AdvertisingEmergencyStopService.TriggerFor(true, false, false, false, false));
        Assert.Equal(EmergencyTrigger.CrossProjectGuard,
            AdvertisingEmergencyStopService.TriggerFor(false, false, true, false, false));
        Assert.Equal(EmergencyTrigger.RepeatedFinancialCommands,
            AdvertisingEmergencyStopService.TriggerFor(false, false, false, true, false));
        Assert.Equal(EmergencyTrigger.LostAuthorization,
            AdvertisingEmergencyStopService.TriggerFor(false, false, false, false, true));
    }

    [Fact]
    public async Task Stop_pauses_each_owned_hierarchy_identity_once_and_never_manual_ownership()
    {
        var setup = await SafetySetup.CreateAsync();
        setup.OwnedAd.CampaignExternalId = "owned-campaign";
        setup.OwnedAd.AdSetExternalId = "owned-adset";
        setup.ManualAd.CampaignExternalId = "manual-campaign";
        setup.ManualAd.AdSetExternalId = "manual-adset";
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.CapRisk, "cap");
        var commands = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .Where(item => result.CommandIds.Contains(item.Id)).ToListAsync();

        Assert.Equal(3, commands.Count);
        Assert.Contains(commands, item => item.TargetExternalId == "owned-campaign");
        Assert.Contains(commands, item => item.TargetExternalId == "owned-adset");
        Assert.Contains(commands, item => item.TargetExternalId == "owned-ad");
        Assert.DoesNotContain(commands, item => item.TargetExternalId!.StartsWith("manual", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Already_sent_financial_command_becomes_unknown_for_read_back_not_cancelled_or_replayed()
    {
        var setup = await SafetySetup.CreateAsync();
        setup.PendingFinancial.State = CommandState.Sent;
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Manual, "stop now");

        Assert.Equal(CommandState.Unknown, setup.PendingFinancial.State);
        Assert.Equal("ADS_EMERGENCY_STOP_ACTIVE", setup.PendingFinancial.LastError);
    }

    [Fact]
    public async Task Stop_progress_read_reports_provider_completion_without_writing()
    {
        var saveAttempts = new SaveAttemptCounter();
        var setup = await SafetySetup.CreateAsync(saveAttempts);
        var service = new AdvertisingEmergencyStopService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Manual, "stop now");
        setup.Db.ChangeTracker.Clear();
        saveAttempts.Reset();

        var pausingStatus = await service.StateAsync(setup.ProjectId);
        var pausingPersistedStop = await setup.Db.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(item => item.Id == result.StopId);

        Assert.NotNull(pausingStatus);
        Assert.Equal("PausingManaged", pausingStatus.State);
        Assert.True(pausingStatus.Progress.GetProperty("continuingSpend").GetBoolean());
        Assert.Equal("PausingManaged", pausingPersistedStop.State);
        Assert.Equal(0, saveAttempts.AttemptCount);
        Assert.False(setup.Db.ChangeTracker.HasChanges());

        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        var ownedAd = await setup.Db.ManagedAdvertisements.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == setup.OwnedAd.Id);
        command.State = CommandState.Succeeded;
        command.CompletedAtUtc = DateTime.UtcNow;
        ownedAd.ConfiguredStatus = ManagedDeliveryState.Paused;
        ownedAd.EffectiveStatus = "PAUSED";
        await setup.Db.SaveChangesAsync();
        setup.Db.ChangeTracker.Clear();
        saveAttempts.Reset();

        var status = await service.StateAsync(setup.ProjectId);
        var persistedStop = await setup.Db.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(item => item.Id == result.StopId);

        Assert.NotNull(status);
        Assert.Equal("Paused", status.State);
        Assert.False(status.Progress.GetProperty("continuingSpend").GetBoolean());
        Assert.Equal("PausingManaged", persistedStop.State);
        Assert.Equal(0, saveAttempts.AttemptCount);
        Assert.False(setup.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Stop_with_unaddressable_owned_delivery_stays_in_needs_attention_and_cannot_resume()
    {
        var setup = await SafetySetup.CreateAsync();
        setup.OwnedAd.CampaignExternalId = null;
        setup.OwnedAd.AdSetExternalId = null;
        setup.OwnedAd.AdExternalId = null;
        AddRecoveryPrerequisites(setup);
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db,
            new AdvertisingOwnershipPolicy(setup.Db));

        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Manual, "stop now");
        var status = await service.StateAsync(setup.ProjectId);
        var persistedStop = await setup.Db.AdvertisingEmergencyStops.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.StopId);

        Assert.Empty(result.CommandIds);
        Assert.NotNull(status);
        Assert.Equal("NeedsAttention", status.State);
        Assert.True(status.Progress.GetProperty("hasUncommandedManagedDelivery").GetBoolean());
        Assert.True(status.Progress.GetProperty("continuingSpend").GetBoolean());
        Assert.Equal("NeedsAttention", persistedStop.State);
        using var persistedProgress = System.Text.Json.JsonDocument.Parse(persistedStop.ProgressJson);
        Assert.True(persistedProgress.RootElement.GetProperty("continuingSpend").GetBoolean());
        var error = await Assert.ThrowsAsync<AdvertisingException>(() =>
            service.ResumeAsync(setup.ProjectId, Guid.NewGuid()));
        Assert.Equal("ADS_RECOVERY_NOT_READY", error.Code);
    }

    [Fact]
    public async Task Succeeded_pause_with_still_active_provider_state_requires_attention_and_blocks_resume()
    {
        var setup = await SafetySetup.CreateAsync();
        AddRecoveryPrerequisites(setup);
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db,
            new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Provider, "provider drift");
        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        command.State = CommandState.Succeeded;
        command.CompletedAtUtc = DateTime.UtcNow;
        await setup.Db.SaveChangesAsync();

        var status = await service.StateAsync(setup.ProjectId);

        Assert.NotNull(status);
        Assert.Equal("NeedsAttention", status.State);
        Assert.True(status.Progress.GetProperty("providerStateContradiction").GetBoolean());
        Assert.True(status.Progress.GetProperty("continuingSpend").GetBoolean());
        var error = await Assert.ThrowsAsync<AdvertisingException>(() =>
            service.ResumeAsync(setup.ProjectId, Guid.NewGuid()));
        Assert.Equal("ADS_RECOVERY_NOT_READY", error.Code);
    }

    [Fact]
    public async Task Stale_pause_command_requires_attention_instead_of_remaining_in_progress_forever()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db,
            new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Provider, "stale pause");
        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        command.State = CommandState.Stale;
        await setup.Db.SaveChangesAsync();

        var status = await service.StateAsync(setup.ProjectId);

        Assert.NotNull(status);
        Assert.Equal("NeedsAttention", status.State);
        Assert.True(status.Progress.GetProperty("continuingSpend").GetBoolean());
    }

    private static void AddRecoveryPrerequisites(SafetySetup setup)
    {
        var nowUtc = DateTime.UtcNow;
        setup.Db.AdvertisingTrackingHealthSnapshots.Add(new TrackingHealthSnapshot
        {
            ProjectId = setup.ProjectId,
            ConnectionId = Guid.NewGuid(),
            DestinationId = Guid.NewGuid(),
            TrackingHealthPolicyId = Guid.NewGuid(),
            TrackingHealthPolicyVersion = 1,
            WindowStartUtc = nowUtc.AddDays(-1),
            WindowEndUtc = nowUtc,
            State = TrackingHealthState.Healthy,
            EvaluatedAtUtc = nowUtc
        });
        setup.Db.AdvertisingBudgetLedgers.Add(new BudgetPeriodLedger
        {
            ProjectId = setup.ProjectId,
            EnvelopeId = Guid.NewGuid(),
            PeriodKind = "Daily",
            PeriodStartUtc = nowUtc.AddHours(-1),
            PeriodEndUtc = nowUtc.AddHours(1),
            LastReconciledAtUtc = nowUtc
        });
    }
}

internal sealed record SafetySetup(Guid ProjectId, AppDbContext Db, ManagedAdvertisement OwnedAd,
    ManagedAdvertisement ManualAd, ExecutionCommand PendingFinancial)
{
    public static async Task<SafetySetup> CreateAsync(params IInterceptor[] interceptors)
    {
        var projectId = Guid.NewGuid(); var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        var db = new AppDbContext(options.Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var connection = new AdvertisingConnection { ProjectId = projectId, State = AdvertisingConnectionState.Ready };
        var owned = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            ProviderCampaignExternalId = "owned", OwnershipKind = ManagedOwnershipKind.AutopilotCreated };
        var manual = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            ProviderCampaignExternalId = "manual", OwnershipKind = ManagedOwnershipKind.ManualUnowned };
        var ownedAd = Ad(projectId, connection.Id, owned.Id, "owned-ad");
        var manualAd = Ad(projectId, connection.Id, manual.Id, "manual-ad");
        var oldDecision = new AdvertisingDecision { ProjectId = projectId, ActionType = "IncreaseBudget",
            TargetType = "Ad", EvidenceStartUtc = DateTime.UtcNow.AddHours(-1), EvidenceEndUtc = DateTime.UtcNow };
        var pending = new ExecutionCommand { ProjectId = projectId, DecisionId = oldDecision.Id,
            IdempotencyKey = "pending-finance", CommandType = "IncreaseBudget", TargetExternalId = "owned-ad",
            DesiredStateJson = $"{{\"adId\":\"{ownedAd.Id}\",\"dailyBudget\":120}}", RequestFingerprint = "pending" };
        db.AddRange(connection, owned, manual, ownedAd, manualAd, oldDecision, pending);
        await db.SaveChangesAsync();
        return new(projectId, db, ownedAd, manualAd, pending);
    }

    private static ManagedAdvertisement Ad(Guid projectId, Guid connectionId, Guid ownershipId, string externalId) => new()
    {
        ProjectId = projectId, ConnectionId = connectionId, OwnershipRecordId = ownershipId,
        AdExternalId = externalId, DestinationType = "WHATSAPP", PublisherPlatform = "AdvantagePlus",
        PositionsJson = "[]", ConfiguredStatus = ManagedDeliveryState.Active, DailyBudget = 100m
    };
}

internal sealed class SaveAttemptCounter : SaveChangesInterceptor
{
    public int AttemptCount { get; private set; }

    public void Reset() => AttemptCount = 0;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AttemptCount++;
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AttemptCount++;
        return ValueTask.FromResult(result);
    }
}
