using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingSpendMonitorTests(PostgresFixture postgres)
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Monthly", EnvelopeState.Active)]
    [InlineData("Total", EnvelopeState.Active)]
    [InlineData("Monthly", EnvelopeState.Suspended)]
    public async Task Period_cap_uses_cumulative_managed_spend_even_when_autopilot_is_suspended(string period, EnvelopeState state)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
        var projectId = Guid.NewGuid();
        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        var connection = new AdvertisingConnection { ProjectId = projectId, State = AdvertisingConnectionState.Ready,
            ProtectedAccessToken = vault.Protect("synthetic-token"), AdAccountExternalId = "act-test", AccountTimezoneIana = "UTC" };
        var envelope = new AutonomyEnvelope { ProjectId = projectId, ConnectionId = connection.Id,
            StartsAtUtc = Now.AddDays(-10), DailyCap = 100, PeriodCap = 50, PeriodCapKind = period,
            SafetyReservePercent = 0, ReportingTimezoneIana = "UTC", State = state };
        var ownership = new ManagedOwnershipRecord { ProjectId = projectId, ConnectionId = connection.Id,
            OwnershipKind = ManagedOwnershipKind.AutopilotCreated, ProviderCampaignExternalId = "campaign-test" };
        var ad = new ManagedAdvertisement { ProjectId = projectId, ConnectionId = connection.Id,
            OwnershipRecordId = ownership.Id, AdExternalId = "ad-test" };
        db.AddRange(connection, envelope, ownership, ad);
        await db.SaveChangesAsync();
        using var http = new HttpClient(new SpendResponse()) { BaseAddress = new Uri("https://meta.invalid/") };

        var breach = await new AdvertisingSpendMonitor(db, new MetaInsightsClient(http), vault, new FixedClock())
            .ReconcileAsync(projectId);

        Assert.NotNull(breach);
        Assert.Equal(period, breach.PeriodKind);
        Assert.Equal(50m, breach.Exposure);
        db.ChangeTracker.Clear();
        var ledgers = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(row => row.ProjectId == projectId).ToListAsync();
        Assert.Equal(10m, ledgers.Single(row => row.PeriodKind == "Daily").ObservedSpend);
        Assert.Equal(50m, ledgers.Single(row => row.PeriodKind == period).ObservedSpend);
        Assert.All(ledgers, ledger => Assert.Equal(Now, ledger.LastReconciledAtUtc));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class SpendResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                data = new[]
                {
                    new { ad_id = "ad-test", date_start = "2026-10-15", date_stop = "2026-10-15", spend = "40" },
                    new { ad_id = "ad-test", date_start = "2026-10-16", date_stop = "2026-10-16", spend = "10" },
                    new { ad_id = "unmanaged-ad", date_start = "2026-10-16", date_stop = "2026-10-16", spend = "999" }
                }
            })) });
    }
}
