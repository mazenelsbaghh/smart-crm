using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record AdvertisingOverviewWindow(DateTime StartUtc, DateTime EndUtc);

public sealed record AdvertisingOverviewInsightMetrics(
    decimal Spend,
    long Impressions,
    long Clicks,
    int DaysLoaded,
    int Snapshots,
    DateTime? LastPulledAtUtc,
    decimal AllTimeSpend);

public sealed record AdvertisingOverviewConversionMetrics(
    decimal Revenue,
    int Leads,
    int QualifiedLeads,
    int Bookings,
    int Purchases);

public sealed record AdvertisingOverviewCampaign(
    string Name,
    decimal DailyBudget,
    string? EffectiveStatus,
    DateTime? LastSyncedAtUtc,
    DateTime? ImportedAtUtc,
    string ManagementSource);

public sealed record AdvertisingOverviewAdvertisementMetrics(
    int ActiveAds,
    int TotalAds,
    bool HasDeliveringAd,
    AdvertisingOverviewCampaign? CurrentCampaign);

public sealed record AdvertisingOverviewDisableCommandMetrics(
    int Total,
    int Succeeded,
    bool NeedsAttention);

public sealed record AdvertisingOverviewDisableStatus(
    string? State,
    bool PauseOngoing);

public static class AdvertisingOverviewQuery
{
    private static readonly string[] ProviderStoppedStatuses =
        ["PAUSED", "CAMPAIGN_PAUSED", "ADSET_PAUSED", "ARCHIVED", "DELETED", "DISAPPROVED"];

    public static async Task<AdvertisingOverviewInsightMetrics> InsightsAsync(
        AppDbContext db,
        Guid projectId,
        AdvertisingOverviewWindow window,
        CancellationToken cancellationToken)
    {
        var metrics = await db.AdvertisingInsights.AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId && snapshot.IsCurrent)
            .GroupBy(snapshot => snapshot.ProjectId)
            .Select(group => new AdvertisingOverviewInsightMetrics(
                group.Sum(snapshot => snapshot.IntervalStartUtc >= window.StartUtc && snapshot.IntervalStartUtc < window.EndUtc ? snapshot.Spend : 0m),
                group.Sum(snapshot => snapshot.IntervalStartUtc >= window.StartUtc && snapshot.IntervalStartUtc < window.EndUtc ? snapshot.Impressions : 0L),
                group.Sum(snapshot => snapshot.IntervalStartUtc >= window.StartUtc && snapshot.IntervalStartUtc < window.EndUtc ? snapshot.Clicks : 0L),
                group.Select(snapshot => snapshot.IntervalStartUtc.Date).Distinct().Count(),
                group.Count(),
                group.Max(snapshot => (DateTime?)snapshot.FetchedAtUtc),
                group.Sum(snapshot => snapshot.Spend)))
            .SingleOrDefaultAsync(cancellationToken);

        return metrics ?? new(0m, 0L, 0L, 0, 0, null, 0m);
    }

    public static async Task<AdvertisingOverviewConversionMetrics> ConversionsAsync(
        AppDbContext db,
        Guid projectId,
        AdvertisingOverviewWindow window,
        CancellationToken cancellationToken)
    {
        var metrics = await db.AdvertisingConversions.AsNoTracking()
            .Where(conversion => conversion.ProjectId == projectId
                && conversion.OccurredAtUtc >= window.StartUtc
                && conversion.OccurredAtUtc < window.EndUtc)
            .GroupBy(conversion => conversion.ProjectId)
            .Select(group => new AdvertisingOverviewConversionMetrics(
                group.Sum(conversion => conversion.CurrentValue > 0 ? conversion.CurrentValue ?? 0m : 0m),
                group.Count(conversion => conversion.EventType == "Lead"),
                group.Count(conversion => conversion.EventType == "QualifiedLead"),
                group.Count(conversion => conversion.EventType == "BookingConfirmed"
                    || conversion.EventType == "EnrollmentPaid"
                    || conversion.EventType == "AttendanceConfirmed"),
                group.Count(conversion => conversion.EventType == "Purchase"
                    || conversion.EventType == "SubscriptionStarted"
                    || conversion.EventType == "EnrollmentPaid")))
            .SingleOrDefaultAsync(cancellationToken);

        return metrics ?? new(0m, 0, 0, 0, 0);
    }

    public static async Task<AdvertisingOverviewAdvertisementMetrics> AdvertisementsAsync(
        AppDbContext db,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var metrics = await db.ManagedAdvertisements.AsNoTracking()
            .Where(advertisement => advertisement.ProjectId == projectId)
            .GroupBy(advertisement => advertisement.ProjectId)
            .Select(group => new AdvertisingOverviewAdvertisementMetrics(
                group.Count(advertisement => advertisement.EffectiveStatus != null
                    && advertisement.EffectiveStatus.ToUpper() == "ACTIVE"),
                group.Count(),
                group.Any(advertisement => advertisement.EffectiveStatus == null
                    || !ProviderStoppedStatuses.Contains(advertisement.EffectiveStatus.ToUpper())),
                group.OrderByDescending(advertisement =>
                        advertisement.LastSyncedAtUtc ?? advertisement.ImportedAtUtc ?? advertisement.CreatedAt)
                    .Select(advertisement => new AdvertisingOverviewCampaign(
                        advertisement.Name,
                        advertisement.DailyBudget,
                        advertisement.EffectiveStatus,
                        advertisement.LastSyncedAtUtc,
                        advertisement.ImportedAtUtc,
                        advertisement.ManagementSource))
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

        return metrics ?? new(0, 0, false, null);
    }

    public static async Task<AdvertisingOverviewDisableCommandMetrics?> DisableCommandsAsync(
        AppDbContext db,
        Guid projectId,
        Guid disableRequestId,
        CancellationToken cancellationToken)
    {
        var commandPrefix = $"disable:{disableRequestId:N}:";
        return await db.AdvertisingExecutionCommands.AsNoTracking()
            .Where(command => command.ProjectId == projectId
                && command.IdempotencyKey.StartsWith(commandPrefix))
            .GroupBy(command => command.ProjectId)
            .Select(group => new AdvertisingOverviewDisableCommandMetrics(
                group.Count(),
                group.Count(command => command.State == CommandState.Succeeded),
                group.Any(command => command.State == CommandState.Unknown
                    || command.State == CommandState.Failed
                    || command.State == CommandState.Stale
                    || command.State == CommandState.Blocked
                    || command.State == CommandState.Cancelled)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static AdvertisingOverviewDisableStatus DisableStatus(
        AutopilotDisableRequest? disableRequest,
        AdvertisingOverviewDisableCommandMetrics? commands,
        bool managedDeliveryMayContinue)
    {
        if (disableRequest is null) return new(null, false);
        if (disableRequest.Mode == AutopilotDisableMode.LeaveRunning)
            return new("MonitoringContinuingSpend", false);
        if (commands is null)
            return managedDeliveryMayContinue
                ? new("NeedsAttention", true)
                : new(disableRequest.State, disableRequest.State is not "Completed");
        if (commands.Succeeded == commands.Total)
            return managedDeliveryMayContinue
                ? new("NeedsAttention", true)
                : new("Completed", false);
        return new(commands.NeedsAttention ? "NeedsAttention" : "PausingManaged", true);
    }
}
