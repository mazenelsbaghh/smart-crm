using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class BudgetConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_reservations_cannot_both_cross_daily_or_monthly_authority()
    {
        var projectId = Guid.NewGuid();
        Guid envelopeId;
        await using (var seed = postgres.CreateContext())
        {
            await seed.Database.MigrateAsync();
            var envelope = new AutonomyEnvelope
            {
                ProjectId = projectId, DailyCap = 100m, PeriodCap = 100m, PeriodCapKind = "Monthly",
                Currency = "EGP", ReportingTimezoneIana = "Africa/Cairo", StartsAtUtc = DateTime.UtcNow.AddDays(-1),
                State = EnvelopeState.Active
            };
            seed.AutonomyEnvelopes.Add(envelope); await seed.SaveChangesAsync(); envelopeId = envelope.Id;
        }

        async Task<BudgetBatchReservationResult> Reserve(Guid targetId)
        {
            await using var context = postgres.CreateContext();
            var result = await new BudgetAllocator().ReserveAsync(context, projectId, envelopeId, targetId, BudgetPurpose.Canary, 70m);
            return new(result.Reserved, result.AllocationId is { } id ? [id] : [], result.Available, result.Code);
        }

        var results = await Task.WhenAll(Reserve(Guid.NewGuid()), Reserve(Guid.NewGuid()));

        Assert.Single(results, result => result.Reserved);
        Assert.Single(results, result => !result.Reserved && result.Code == "ADS_USABLE_CAP_EXCEEDED");
        await using var verify = postgres.CreateContext();
        Assert.All(await verify.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(x => x.EnvelopeId == envelopeId).ToListAsync(),
            ledger => Assert.Equal(70m, ledger.CommittedAmount));
    }
}
