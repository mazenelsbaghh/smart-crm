using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingSafetyEngineTests
{
    [Fact]
    public async Task Financial_change_requires_owned_whatsapp_target_fresh_health_envelope_and_expected_state()
    {
        var setup = await SetupAsync();
        var request = Request(setup);

        var allowed = await setup.Engine.EvaluateAsync(request);
        var stale = await setup.Engine.EvaluateAsync(request with { ExpectedStateHash = "stale" });
        setup.Tracking.State = TrackingHealthState.Unsafe; await setup.Db.SaveChangesAsync();
        var unsafeTracking = await setup.Engine.EvaluateAsync(request);

        Assert.Equal(DecisionVerdict.Approve, allowed.Verdict);
        Assert.Equal("ADS_EXPECTED_STATE_STALE", stale.Code);
        Assert.Equal("ADS_TRACKING_UNHEALTHY", unsafeTracking.Code);
    }

    [Fact]
    public async Task Manual_unowned_or_non_whatsapp_targets_fail_closed()
    {
        var setup = await SetupAsync();
        setup.Ownership.OwnershipKind = ManagedOwnershipKind.ManualUnowned;
        await setup.Db.SaveChangesAsync();
        var unowned = await setup.Engine.EvaluateAsync(Request(setup));
        setup.Ownership.OwnershipKind = ManagedOwnershipKind.AutopilotCreated;
        setup.Ad.DestinationType = "WEBSITE";
        await setup.Db.SaveChangesAsync();
        var website = await setup.Engine.EvaluateAsync(Request(setup));

        Assert.Equal("ADS_TARGET_NOT_MANAGED", unowned.Code);
        Assert.Equal("ADS_DESTINATION_NOT_WHATSAPP", website.Code);
    }

    [Fact]
    public async Task Increase_cannot_exceed_step_or_remaining_multi_period_authority()
    {
        var setup = await SetupAsync();
        var tooLargeStep = await setup.Engine.EvaluateAsync(Request(setup) with { ProposedBudget = 130m });
        setup.Ledger.CommittedAmount = 995m; await setup.Db.SaveChangesAsync();
        var capRisk = await setup.Engine.EvaluateAsync(Request(setup) with { ProposedBudget = 110m });

        Assert.Equal("ADS_INCREASE_EXCEEDED", tooLargeStep.Code);
        Assert.Equal("ADS_HARD_CAP_RISK", capRisk.Code);
    }

    private static SafetyRequest Request(SetupState setup) => new(setup.ProjectId, "IncreaseBudget", 100m, 110m,
        "AdvantagePlus", [], setup.Ad.Id, setup.Destination.Id, "provider-v1");

    private static async Task<SetupState> SetupAsync()
    {
        var projectId = Guid.NewGuid(); var now = DateTime.UtcNow;
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var connection = new AdvertisingConnection { ProjectId = projectId, State = AdvertisingConnectionState.Ready,
            AccountStatus = "ACTIVE", FundingStatus = "FUNDED" };
        var destination = new AuthorizedWhatsAppDestination { ProjectId = projectId, ConnectionId = connection.Id,
            WabaExternalId = "waba", PhoneNumberExternalId = "phone", DatasetExternalId = "dataset",
            State = AuthorizedDestinationState.Eligible };
        var envelope = new AutonomyEnvelope { ProjectId = projectId, ConnectionId = connection.Id, DailyCap = 1000m,
            MaximumIncreasePercent = 20m, StartsAtUtc = now.AddDays(-1), EndsAtUtc = now.AddDays(1), State = EnvelopeState.Active };
        var ownership = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            ProviderCampaignExternalId = "campaign", OwnershipKind = ManagedOwnershipKind.AutopilotCreated };
        var ad = new ManagedAdvertisement { ProjectId = projectId, ConnectionId = connection.Id,
            OwnershipRecordId = ownership.Id, DestinationId = destination.Id, DestinationType = "WHATSAPP",
            PublisherPlatform = "AdvantagePlus", PositionsJson = "[]", DailyBudget = 100m,
            ProviderStateHash = "provider-v1", AdExternalId = "ad-1" };
        var tracking = new TrackingHealthSnapshot { ProjectId = projectId, ConnectionId = connection.Id,
            DestinationId = destination.Id, TrackingHealthPolicyId = Guid.NewGuid(), TrackingHealthPolicyVersion = 1,
            WindowStartUtc = now.AddDays(-7), WindowEndUtc = now, State = TrackingHealthState.Healthy,
            EvaluatedAtUtc = now };
        var capability = new AdvertisingCapabilitySnapshot { ProjectId = projectId, ConnectionId = connection.Id,
            DestinationId = destination.Id, State = AdvertisingCapabilityState.Healthy, CheckedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1) };
        var ledger = new BudgetPeriodLedger { ProjectId = projectId, EnvelopeId = envelope.Id,
            PeriodKind = "Daily", PeriodStartUtc = now.AddDays(-1), PeriodEndUtc = now.AddDays(1),
            AuthorizedCap = 1000m, UsableCap = 1000m, CommittedAmount = 100m, LastReconciledAtUtc = now };
        db.AddRange(connection, destination, envelope, ownership, ad, tracking, capability, ledger);
        await db.SaveChangesAsync();
        return new(projectId, db, destination, ownership, ad, tracking, ledger, new AdvertisingSafetyEngine(db));
    }

    private sealed record SetupState(Guid ProjectId, AppDbContext Db, AuthorizedWhatsAppDestination Destination,
        ManagedOwnershipRecord Ownership, ManagedAdvertisement Ad, TrackingHealthSnapshot Tracking,
        BudgetPeriodLedger Ledger, AdvertisingSafetyEngine Engine);
}
