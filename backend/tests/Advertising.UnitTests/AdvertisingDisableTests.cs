using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDisableTests
{
    [Fact]
    public async Task PauseManaged_is_default_safe_behavior_and_excludes_manual_unowned_ads()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.PauseManaged, "normal stop", false);

        Assert.True(result.ContinuingSpend);
        Assert.True(result.PauseOngoing);
        Assert.True(result.DeliveryMayContinue);
        Assert.Single(result.CommandIds);
        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        Assert.Contains(setup.OwnedAd.Id.ToString(), command.DesiredStateJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setup.ManualAd.Id.ToString(), command.DesiredStateJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LeaveRunning_requires_explicit_ack_and_keeps_continuing_spend_visible()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var error = await Assert.ThrowsAsync<AdvertisingException>(() => service.DisableAsync(setup.ProjectId,
            Guid.NewGuid(), AutopilotDisableMode.LeaveRunning, "observe only", false));
        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.LeaveRunning, "observe only", true);

        Assert.Equal("ADS_CONTINUING_SPEND_ACK_REQUIRED", error.Code);
        Assert.True(result.ContinuingSpend);
        Assert.False(result.PauseOngoing);
        Assert.True(result.DeliveryMayContinue);
        Assert.Empty(result.CommandIds);
        var request = await setup.Db.AdvertisingDisableRequests.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(request.ContinuingSpendAcknowledgedAtUtc);
        Assert.Equal("MonitoringContinuingSpend", request.State);
    }

    [Fact]
    public async Task PauseManaged_progress_is_reported_without_mutating_the_polling_read()
    {
        var saveAttempts = new SaveAttemptCounter();
        var setup = await SafetySetup.CreateAsync(saveAttempts);
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.PauseManaged, "normal stop", false);
        setup.Db.ChangeTracker.Clear();
        saveAttempts.Reset();

        var pausingStatus = await service.StateAsync(setup.ProjectId, result.RequestId);
        var request = await setup.Db.AdvertisingDisableRequests.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.NotNull(pausingStatus);
        Assert.Equal("PausingManaged", pausingStatus.State);
        Assert.True(pausingStatus.Progress.GetProperty("pauseOngoing").GetBoolean());
        Assert.True(pausingStatus.Progress.GetProperty("deliveryMayContinue").GetBoolean());
        Assert.Equal("PausingManaged", request.State);
        Assert.Null(request.CompletedAtUtc);
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
        var completedStatus = await service.StateAsync(setup.ProjectId, result.RequestId);
        var persistedRequest = await setup.Db.AdvertisingDisableRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(item => item.Id == result.RequestId);

        Assert.NotNull(completedStatus);
        Assert.Equal("Completed", completedStatus.State);
        Assert.NotNull(completedStatus.CompletedAtUtc);
        Assert.False(completedStatus.Progress.GetProperty("pauseOngoing").GetBoolean());
        Assert.False(completedStatus.Progress.GetProperty("deliveryMayContinue").GetBoolean());
        Assert.Equal("PausingManaged", persistedRequest.State);
        Assert.Null(persistedRequest.CompletedAtUtc);
        Assert.Equal(0, saveAttempts.AttemptCount);
        Assert.False(setup.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task PauseManaged_is_immediately_complete_when_provider_delivery_is_already_stopped()
    {
        var setup = await SafetySetup.CreateAsync();
        setup.OwnedAd.EffectiveStatus = "PAUSED";
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.PauseManaged, "normal stop", false);
        var request = await setup.Db.AdvertisingDisableRequests.IgnoreQueryFilters().SingleAsync();

        Assert.Empty(result.CommandIds);
        Assert.False(result.PauseOngoing);
        Assert.False(result.DeliveryMayContinue);
        Assert.Equal("Completed", request.State);
        Assert.NotNull(request.CompletedAtUtc);
    }

    [Fact]
    public async Task PauseManaged_never_claims_completion_when_any_owned_delivery_has_no_provider_target()
    {
        var setup = await SafetySetup.CreateAsync();
        setup.OwnedAd.AdExternalId = null;
        setup.Db.ManagedAdvertisements.Add(new ManagedAdvertisement
        {
            ProjectId = setup.ProjectId,
            ConnectionId = setup.OwnedAd.ConnectionId,
            OwnershipRecordId = setup.OwnedAd.OwnershipRecordId,
            AdExternalId = "second-owned-ad",
            ConfiguredStatus = ManagedDeliveryState.Active
        });
        await setup.Db.SaveChangesAsync();
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));

        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.PauseManaged, "normal stop", false);
        var status = await service.StateAsync(setup.ProjectId, result.RequestId);
        var request = await setup.Db.AdvertisingDisableRequests.IgnoreQueryFilters().SingleAsync();

        Assert.Single(result.CommandIds);
        Assert.True(result.PauseOngoing);
        Assert.True(result.DeliveryMayContinue);
        Assert.NotNull(status);
        Assert.Equal("NeedsAttention", status.State);
        Assert.True(status.Progress.GetProperty("hasUncommandedManagedDelivery").GetBoolean());
        Assert.Equal("NeedsAttention", request.State);
        Assert.False(setup.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Succeeded_pause_with_still_active_provider_state_keeps_disable_in_needs_attention()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingDisableService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.DisableAsync(setup.ProjectId, Guid.NewGuid(),
            AutopilotDisableMode.PauseManaged, "normal stop", false);
        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        command.State = CommandState.Succeeded;
        command.CompletedAtUtc = DateTime.UtcNow;
        await setup.Db.SaveChangesAsync();

        var status = await service.StateAsync(setup.ProjectId, result.RequestId);

        Assert.NotNull(status);
        Assert.Equal("NeedsAttention", status.State);
        Assert.True(status.Progress.GetProperty("providerStateContradiction").GetBoolean());
        Assert.True(status.Progress.GetProperty("pauseOngoing").GetBoolean());
        Assert.True(status.Progress.GetProperty("deliveryMayContinue").GetBoolean());
    }
}
