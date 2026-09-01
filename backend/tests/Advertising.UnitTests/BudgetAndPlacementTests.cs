using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class BudgetAndPlacementTests
{
    [Fact]
    public async Task One_reservation_atomically_debits_daily_and_monthly_authority()
    {
        var setup = await SetupAsync(dailyCap: 100m, periodCap: 500m);
        var allocator = new BudgetAllocator();

        var result = await allocator.ReserveAsync(setup.Db, setup.ProjectId, setup.EnvelopeId,
            Guid.NewGuid(), BudgetPurpose.Canary, 50m);

        Assert.True(result.Reserved);
        var ledgers = await setup.Db.AdvertisingBudgetLedgers.IgnoreQueryFilters().OrderBy(x => x.PeriodKind).ToListAsync();
        Assert.Equal(new[] { "Daily", "Monthly" }, ledgers.Select(x => x.PeriodKind));
        Assert.All(ledgers, ledger => Assert.Equal(50m, ledger.CommittedAmount));
        var allocation = await setup.Db.AdvertisingBudgetAllocations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(2, await setup.Db.AdvertisingBudgetAllocationDebits.IgnoreQueryFilters().CountAsync(x => x.AllocationId == allocation.Id));

        await allocator.ReleaseAsync(setup.Db, setup.ProjectId, allocation.Id);
        Assert.All(ledgers, ledger => Assert.Equal(50m, ledger.ReleasedAmount));
    }

    [Fact]
    public async Task Tightest_period_rejects_the_whole_batch_without_partial_reservation()
    {
        var setup = await SetupAsync(dailyCap: 100m, periodCap: 55m, reservePercent: 10m);

        var result = await new BudgetAllocator().ReserveBatchAsync(setup.Db,
            new(setup.ProjectId, setup.EnvelopeId,
            [new(Guid.NewGuid(), BudgetPurpose.CreativeTest, 30m), new(Guid.NewGuid(), BudgetPurpose.AudienceTest, 20m)]));

        Assert.False(result.Reserved);
        Assert.Equal("ADS_USABLE_CAP_EXCEEDED", result.Code);
        Assert.Empty(await setup.Db.AdvertisingBudgetAllocations.IgnoreQueryFilters().ToListAsync());
        Assert.All(await setup.Db.AdvertisingBudgetLedgers.IgnoreQueryFilters().ToListAsync(), ledger => Assert.Equal(0m, ledger.CommittedAmount));
    }

    [Fact]
    public void Cairo_daily_period_is_derived_from_project_timezone_not_server_midnight()
    {
        var envelope = new AutonomyEnvelope { DailyCap = 100m, ReportingTimezoneIana = "Africa/Cairo" };
        var periods = BudgetPeriodPolicy.Resolve(envelope, new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        var daily = Assert.Single(periods);
        Assert.Equal(new DateTime(2026, 8, 18, 21, 0, 0, DateTimeKind.Utc), daily.StartUtc);
        Assert.Equal(new DateTime(2026, 8, 19, 21, 0, 0, DateTimeKind.Utc), daily.EndUtc);
    }

    private static async Task<Setup> SetupAsync(decimal dailyCap, decimal periodCap, decimal reservePercent = 0m)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new ServiceCollection().BuildServiceProvider());
        var envelope = new AutonomyEnvelope
        {
            ProjectId = projectId, DailyCap = dailyCap, PeriodCap = periodCap, PeriodCapKind = "Monthly",
            SafetyReservePercent = reservePercent, Currency = "EGP", ReportingTimezoneIana = "Africa/Cairo",
            StartsAtUtc = DateTime.UtcNow.AddDays(-1), State = EnvelopeState.Active
        };
        db.AutonomyEnvelopes.Add(envelope); await db.SaveChangesAsync();
        return new(projectId, envelope.Id, db);
    }

    private sealed record Setup(Guid ProjectId, Guid EnvelopeId, AppDbContext Db);
}
