using System.Text.Json;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

internal sealed record EmergencyPauseProgress(
    string State,
    bool HasUncommandedManagedDelivery,
    bool ContinuingSpend,
    JsonElement Progress);

internal sealed record DisablePauseProgress(
    string State,
    DateTime? CompletedAtUtc,
    bool HasUncommandedManagedDelivery,
    bool PauseOngoing,
    bool DeliveryMayContinue,
    JsonElement Progress);

internal sealed record ProtectiveCommandCounts(
    int Total,
    int Succeeded,
    int Unknown,
    int Failed,
    int Pending);

internal sealed record ManagedDeliveryCoverage(
    bool HasUncommandedManagedDelivery,
    bool HasProviderStateContradiction);

internal static class AdvertisingProtectiveProgress
{
    public static EmergencyPauseProgress Emergency(
        IReadOnlyCollection<ExecutionCommand> commands,
        IReadOnlyCollection<ManagedAdvertisement> adsRequiringPause)
    {
        var commandCounts = CountCommands(commands);
        var coverage = DeliveryCoverage(adsRequiringPause, commands);
        var state = coverage.HasUncommandedManagedDelivery
            || coverage.HasProviderStateContradiction
            || commandCounts.Unknown > 0 || commandCounts.Failed > 0
            ? "NeedsAttention"
            : adsRequiringPause.Count == 0
                && (commandCounts.Total == 0 || commandCounts.Succeeded == commandCounts.Total)
                ? "Paused" : "PausingManaged";
        var continuingSpend = adsRequiringPause.Count > 0
            || commandCounts.Succeeded < commandCounts.Total;
        var progress = JsonSerializer.SerializeToElement(new
        {
            total = commandCounts.Total,
            succeeded = commandCounts.Succeeded,
            unknown = commandCounts.Unknown,
            failed = commandCounts.Failed,
            pending = commandCounts.Pending,
            hasUncommandedManagedDelivery = coverage.HasUncommandedManagedDelivery,
            providerStateContradiction = coverage.HasProviderStateContradiction,
            continuingSpend
        });
        return new(state, coverage.HasUncommandedManagedDelivery, continuingSpend, progress);
    }

    public static DisablePauseProgress Disable(
        AutopilotDisableRequest request,
        IReadOnlyCollection<ExecutionCommand> commands,
        IReadOnlyCollection<ManagedAdvertisement> adsRequiringPause)
    {
        var commandCounts = CountCommands(commands);
        var coverage = request.Mode == AutopilotDisableMode.PauseManaged
            ? DeliveryCoverage(adsRequiringPause, commands)
            : new(false, false);
        var needsAttention = coverage.HasUncommandedManagedDelivery
            || coverage.HasProviderStateContradiction
            || commandCounts.Unknown > 0 || commandCounts.Failed > 0;

        var state = request.Mode == AutopilotDisableMode.LeaveRunning
            ? "MonitoringContinuingSpend"
            : adsRequiringPause.Count == 0
                && commandCounts.Succeeded == commandCounts.Total
                ? "Completed"
                : needsAttention ? "NeedsAttention" : "PausingManaged";
        var pauseOngoing = request.Mode == AutopilotDisableMode.PauseManaged && state != "Completed";
        var deliveryMayContinue = request.Mode == AutopilotDisableMode.LeaveRunning
            ? adsRequiringPause.Count > 0
            : adsRequiringPause.Count > 0 || commandCounts.Succeeded < commandCounts.Total;
        var completedAtUtc = state == "Completed"
            ? request.CompletedAtUtc ?? commands
                .Where(command => command.CompletedAtUtc.HasValue)
                .Select(command => command.CompletedAtUtc)
                .Max() ?? request.RequestedAtUtc
            : request.CompletedAtUtc;
        var progress = JsonSerializer.SerializeToElement(new
        {
            total = commandCounts.Total,
            succeeded = commandCounts.Succeeded,
            unknown = commandCounts.Unknown,
            failed = commandCounts.Failed,
            pending = commandCounts.Pending,
            needsAttention,
            hasUncommandedManagedDelivery = coverage.HasUncommandedManagedDelivery,
            providerStateContradiction = coverage.HasProviderStateContradiction,
            pauseOngoing,
            deliveryMayContinue,
            continuingSpend = deliveryMayContinue
        });
        return new(state, completedAtUtc, coverage.HasUncommandedManagedDelivery,
            pauseOngoing, deliveryMayContinue, progress);
    }

    public static List<ManagedAdvertisement> RequiringPause(IEnumerable<ManagedAdvertisement> ads) =>
        ads.Where(ad => !IsProviderStopped(ad.EffectiveStatus)).ToList();

    public static bool IsProviderStopped(string? effectiveStatus) => effectiveStatus?.ToUpperInvariant() is
        "PAUSED" or "CAMPAIGN_PAUSED" or "ADSET_PAUSED" or "ARCHIVED" or "DELETED" or "DISAPPROVED";

    private static ProtectiveCommandCounts CountCommands(IReadOnlyCollection<ExecutionCommand> commands)
    {
        var succeeded = commands.Count(command => command.State == CommandState.Succeeded);
        var unknown = commands.Count(command => command.State == CommandState.Unknown);
        var failed = commands.Count(command => command.State is
            CommandState.Failed or CommandState.Stale or CommandState.Blocked or CommandState.Cancelled);
        return new(commands.Count, succeeded, unknown, failed,
            Math.Max(0, commands.Count - succeeded - unknown - failed));
    }

    private static ManagedDeliveryCoverage DeliveryCoverage(
        IEnumerable<ManagedAdvertisement> adsRequiringPause,
        IReadOnlyCollection<ExecutionCommand> commands)
    {
        var hasUncommandedManagedDelivery = false;
        var hasProviderStateContradiction = false;
        foreach (var advertisement in adsRequiringPause)
        {
            var providerTargets = new[]
            {
                advertisement.CampaignExternalId,
                advertisement.AdSetExternalId,
                advertisement.AdExternalId
            }.Where(target => !string.IsNullOrWhiteSpace(target)).ToHashSet(StringComparer.Ordinal);
            var coveringCommands = commands.Where(command =>
                command.TargetExternalId is not null && providerTargets.Contains(command.TargetExternalId)).ToArray();
            if (coveringCommands.Length == 0)
                hasUncommandedManagedDelivery = true;
            else if (coveringCommands.All(command => command.State == CommandState.Succeeded))
                hasProviderStateContradiction = true;
        }
        return new(hasUncommandedManagedDelivery, hasProviderStateContradiction);
    }
}
