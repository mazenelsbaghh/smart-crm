using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingSafetyJobsTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Finance_freezes_on_connection_tracking_or_emergency_failure(bool connection, bool tracking, bool stop) =>
        Assert.True(AdvertisingOperationalPolicy.MustFreezeFinance(connection, tracking, stop));

    [Fact]
    public void Spend_at_cap_triggers_stop_but_normal_spend_does_not()
    {
        Assert.True(AdvertisingOperationalPolicy.MustEmergencyStop(300m, 300m));
        Assert.False(AdvertisingOperationalPolicy.MustEmergencyStop(299.99m, 300m));
    }

    [Fact]
    public async Task Unknown_pause_keeps_stop_in_needs_attention_and_blocks_resume_until_every_guard_is_fresh()
    {
        var setup = await SafetySetup.CreateAsync();
        var service = new AdvertisingEmergencyStopService(setup.Db, new AdvertisingOwnershipPolicy(setup.Db));
        var result = await service.ActivateAsync(setup.ProjectId, EmergencyTrigger.Provider, "provider uncertainty");
        var command = await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == result.CommandIds[0]);
        command.State = CommandState.Unknown;
        await setup.Db.SaveChangesAsync();

        var status = await service.StateAsync(setup.ProjectId);
        var stop = await setup.Db.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("NeedsAttention", status?.State);
        Assert.Equal("PausingManaged", stop.State);
        Assert.False(setup.Db.ChangeTracker.HasChanges());
        await Assert.ThrowsAsync<AdvertisingException>(() => service.ResumeAsync(setup.ProjectId, Guid.NewGuid()));

        command.State = CommandState.Succeeded;
        setup.OwnedAd.ConfiguredStatus = ManagedDeliveryState.Paused;
        setup.OwnedAd.EffectiveStatus = "PAUSED";
        setup.Db.AdvertisingTrackingHealthSnapshots.Add(new TrackingHealthSnapshot { ProjectId = setup.ProjectId,
            ConnectionId = Guid.NewGuid(), DestinationId = Guid.NewGuid(), TrackingHealthPolicyId = Guid.NewGuid(),
            TrackingHealthPolicyVersion = 1, WindowStartUtc = DateTime.UtcNow.AddDays(-1), WindowEndUtc = DateTime.UtcNow,
            State = TrackingHealthState.Healthy, EvaluatedAtUtc = DateTime.UtcNow });
        setup.Db.AdvertisingBudgetLedgers.Add(new BudgetPeriodLedger { ProjectId = setup.ProjectId,
            EnvelopeId = Guid.NewGuid(), PeriodKind = "Daily", PeriodStartUtc = DateTime.UtcNow.AddHours(-1),
            PeriodEndUtc = DateTime.UtcNow.AddHours(1), LastReconciledAtUtc = DateTime.UtcNow });
        await setup.Db.SaveChangesAsync();

        await service.ResumeAsync(setup.ProjectId, Guid.NewGuid());
        Assert.NotNull(stop.ResumedAtUtc);
    }
}
