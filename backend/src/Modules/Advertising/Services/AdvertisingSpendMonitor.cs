using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record SpendLimitBreach(string PeriodKind, decimal Exposure, decimal Cap, bool Abnormal);

public sealed class AdvertisingSpendMonitor(AppDbContext db, MetaInsightsClient insights, AdvertisingSecretVault vault, TimeProvider? clock = null)
{
    public async Task<SpendLimitBreach?> ReconcileAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
                && (item.State == EnvelopeState.Active || item.State == EnvelopeState.Suspended))
            .OrderByDescending(item => item.State == EnvelopeState.Active).ThenByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (envelope is null) return null;
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (connection?.ProtectedAccessToken is null || string.IsNullOrWhiteSpace(connection.AdAccountExternalId)
            || string.IsNullOrWhiteSpace(connection.AccountTimezoneIana))
            throw new AdvertisingException("ADS_SPEND_SOURCE_UNAVAILABLE", "Live account spend and timezone are required.", 409);
        var ledgers = await new BudgetAllocator().EnsureCurrentLedgersAsync(db, envelope, now, cancellationToken);
        var ads = await new AdvertisingOwnershipPolicy(db).ManagedAdsAsync(projectId, activeOnly: false, cancellationToken);
        var managedIds = ads.Where(ad => ad.AdExternalId != null).Select(ad => ad.AdExternalId!).ToHashSet(StringComparer.Ordinal);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(connection.AccountTimezoneIana);
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ledgers.Min(ledger => ledger.PeriodStartUtc), zone));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var rows = await insights.GetAdInsightsAsync(vault.Unprotect(connection.ProtectedAccessToken),
            connection.AdAccountExternalId, firstDate, today, cancellationToken);
        var managedRows = rows.Where(row => managedIds.Contains(row.AdExternalId)).ToArray();
        foreach (var ledger in ledgers)
        {
            // Meta daily dates belong to the ad-account timezone. Count overlapping days conservatively.
            var spend = managedRows.Where(row => AccountUtc(row.StartUtc, zone) < ledger.PeriodEndUtc
                && AccountUtc(row.EndUtc, zone) > ledger.PeriodStartUtc).Sum(row => row.Spend);
            ledger.ObservedSpend = Math.Max(ledger.ObservedSpend, spend);
            ledger.LastReconciledAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        return ledgers.Where(ledger => AdvertisingSpendGuard.ObservedExposure(ledger) >= ledger.UsableCap)
            .Select(ledger => new SpendLimitBreach(ledger.PeriodKind, AdvertisingSpendGuard.ObservedExposure(ledger),
                ledger.UsableCap, AdvertisingOperationalPolicy.IsAbnormalForecast(
                    AdvertisingSpendGuard.ObservedExposure(ledger), ledger.UsableCap))).FirstOrDefault();
    }

    private static DateTime AccountUtc(DateTime reportedDate, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(reportedDate, DateTimeKind.Unspecified), zone);
}
