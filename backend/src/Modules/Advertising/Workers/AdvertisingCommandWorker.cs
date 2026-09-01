using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;

namespace Modules.Advertising.Workers;

public sealed class AdvertisingCommandWorker(AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault,
    AdvertisingSafetyEngine safety, AdvertisingEmergencyStopService emergencyStops,
    AdvertisingOwnershipPolicy ownership)
{
    private static readonly JsonSerializerOptions CommandJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ExecuteAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default)
    {
        var current = await db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == commandId, cancellationToken);
        if (current is null) return;
        if (current.State is CommandState.Succeeded or CommandState.Failed or CommandState.Stale
            or CommandState.Blocked or CommandState.Cancelled)
        {
            var terminalDesired = DeserializeDesired(current);
            if (terminalDesired is not null)
                await PersistProtectiveProgressAsync(current, terminalDesired, cancellationToken);
            return;
        }
        if (current.State is CommandState.Unknown or CommandState.Sent or CommandState.Claimed)
        {
            await ReconcileAsync(current, cancellationToken);
            return;
        }
        int claimed;
        if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
        {
            var pending = await db.AdvertisingExecutionCommands.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == commandId
                    && item.State == CommandState.Pending, cancellationToken);
            if (pending is null) claimed = 0;
            else { pending.State = CommandState.Claimed; pending.ClaimedAtUtc = DateTime.UtcNow; pending.AttemptCount++; await db.SaveChangesAsync(cancellationToken); claimed = 1; }
        }
        else
        {
            db.Entry(current).State = EntityState.Detached;
            claimed = await db.AdvertisingExecutionCommands.IgnoreQueryFilters()
                .Where(x => x.ProjectId == projectId && x.Id == commandId && x.State == CommandState.Pending)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, CommandState.Claimed)
                    .SetProperty(x => x.ClaimedAtUtc, DateTime.UtcNow)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);
        }
        if (claimed == 0) return;
        var command = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == commandId, cancellationToken);
        var desired = DeserializeDesired(command)
            ?? throw new InvalidOperationException("Invalid Advertising command payload.");
        var ad = await db.ManagedAdvertisements.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.ProjectId == projectId && x.Id == desired.AdId, cancellationToken);
        if (ad is null)
        {
            command.State = CommandState.Blocked;
            command.LastError = "ADS_COMMAND_TARGET_UNAVAILABLE";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            return;
        }
        if (!TargetMatches(command, desired, ad))
        {
            command.State = CommandState.Blocked; command.LastError = "ADS_COMMAND_TARGET_MISMATCH";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            await StopForTargetMismatchAsync(projectId, command, cancellationToken);
            return;
        }
        var execution = new ProviderCommandExecution(command, ad, desired);
        var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
        var action = desired.DailyBudget is not null ? (desired.DailyBudget > ad.DailyBudget ? "IncreaseBudget" : "DecreaseBudget") : desired.Status == "ACTIVE" ? "ResumeAd" : "PauseAd";
        var safetyResult = await safety.EvaluateAsync(new(projectId, action, ad.DailyBudget,
            desired.DailyBudget ?? ad.DailyBudget, ad.PublisherPlatform, positions, ad.Id,
            ad.DestinationId, command.ExpectedStateHash), cancellationToken);
        if (safetyResult.Verdict != DecisionVerdict.Approve)
        {
            command.State = CommandState.Blocked; command.LastError = safetyResult.Code;
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            return;
        }
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (connection?.ProtectedAccessToken is null)
        {
            command.State = CommandState.Blocked; command.LastError = "ConnectionUnavailable";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            return;
        }
        try
        {
            var token = vault.Unprotect(connection.ProtectedAccessToken);
            if (desired.DailyBudget is null && !string.IsNullOrWhiteSpace(desired.Status))
            {
                if (await DeliveryMatchesAsync(token, execution, cancellationToken))
                {
                    await CompleteAsync(execution, "PreflightReadBack", cancellationToken);
                    return;
                }
            }
            command.State = CommandState.Sent;
            command.SentAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (desired.DailyBudget is not null)
            {
                var budgetOwnerId = ad.BudgetOwnerExternalId ?? ad.AdSetExternalId;
                if (budgetOwnerId is null) throw new InvalidOperationException("Managed ad has no Meta budget owner.");
                await meta.SetDailyBudgetAsync(token, budgetOwnerId, desired.DailyBudget.Value, cancellationToken);
                ad.DailyBudget = desired.DailyBudget.Value;
            }
            else
            {
                await SetDeliveryHierarchyAsync(token, execution, cancellationToken);
                ad.ConfiguredStatus = desired.Status == "ACTIVE" ? ManagedDeliveryState.Active : ManagedDeliveryState.Paused;
                ad.EffectiveStatus = desired.Status!;
            }
            await CompleteAsync(execution, "ProviderMutation", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            command.State = CommandState.Unknown; command.LastError = ex.StatusCode?.ToString() ?? "MetaRequestUnknown";
            AdvertisingAudit.Add(db, projectId, "ProviderCommandUnknown", "ExecutionCommand", command.Id, new { command.CommandType, errorCode = command.LastError, command.AttemptCount });
            db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "CommandReconciliation", Severity = "Critical", Summary = "Meta command outcome is unknown; blind retry is blocked.", DetectedAtUtc = DateTime.UtcNow });
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
        }
    }

    private async Task SetDeliveryHierarchyAsync(
        string token,
        ProviderCommandExecution execution,
        CancellationToken cancellationToken)
    {
        if (execution.Desired.Status == "ACTIVE")
        {
            if (string.IsNullOrWhiteSpace(execution.Ad.CampaignExternalId)
                || string.IsNullOrWhiteSpace(execution.Ad.AdSetExternalId)
                || string.IsNullOrWhiteSpace(execution.Ad.AdExternalId))
                throw new InvalidOperationException("Managed delivery hierarchy is incomplete.");
            await meta.SetAdStatusAsync(token, execution.Ad.CampaignExternalId,
                execution.Desired.Status, cancellationToken);
            await meta.SetAdStatusAsync(token, execution.Ad.AdSetExternalId,
                execution.Desired.Status, cancellationToken);
            await meta.SetAdStatusAsync(token, execution.Ad.AdExternalId,
                execution.Desired.Status, cancellationToken);
            return;
        }
        await meta.SetAdStatusAsync(token, execution.Command.TargetExternalId!,
            execution.Desired.Status!, cancellationToken);
    }

    private async Task<bool> DeliveryMatchesAsync(
        string token,
        ProviderCommandExecution execution,
        CancellationToken cancellationToken)
    {
        if (execution.Desired.Status != "ACTIVE")
            return string.Equals(await meta.GetDeliveryStatusAsync(
                    token, execution.Command.TargetExternalId!, cancellationToken),
                execution.Desired.Status, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(execution.Ad.CampaignExternalId)
            || string.IsNullOrWhiteSpace(execution.Ad.AdSetExternalId)
            || string.IsNullOrWhiteSpace(execution.Ad.AdExternalId)) return false;
        foreach (var resourceId in new[]
                 {
                     execution.Ad.CampaignExternalId,
                     execution.Ad.AdSetExternalId,
                     execution.Ad.AdExternalId
                 })
            if (!string.Equals(await meta.GetDeliveryStatusAsync(token, resourceId, cancellationToken),
                    execution.Desired.Status, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private async Task ReconcileAsync(ExecutionCommand command, CancellationToken cancellationToken)
    {
        var desired = DeserializeDesired(command);
        if (desired is null) { command.State = CommandState.Blocked; command.LastError = "ADS_COMMAND_PAYLOAD_INVALID"; await db.SaveChangesAsync(cancellationToken); return; }
        var ad = await db.ManagedAdvertisements.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == command.ProjectId && item.Id == desired.AdId, cancellationToken);
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == command.ProjectId && item.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (ad is null || connection?.ProtectedAccessToken is null || command.TargetExternalId is null)
        {
            command.State = CommandState.Blocked; command.LastError = "ADS_RECONCILIATION_UNAVAILABLE";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken); return;
        }
        if (!TargetMatches(command, desired, ad))
        {
            command.State = CommandState.Blocked; command.LastError = "ADS_COMMAND_TARGET_MISMATCH";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            await StopForTargetMismatchAsync(command.ProjectId, command, cancellationToken);
            return;
        }
        var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
        var action = desired.DailyBudget is not null
            ? (desired.DailyBudget > ad.DailyBudget ? "IncreaseBudget" : "DecreaseBudget")
            : desired.Status == "ACTIVE" ? "ResumeAd" : "PauseAd";
        var safetyResult = await safety.EvaluateAsync(new(command.ProjectId, action, ad.DailyBudget,
            desired.DailyBudget ?? ad.DailyBudget, ad.PublisherPlatform, positions, ad.Id,
            ad.DestinationId, command.ExpectedStateHash), cancellationToken);
        if (safetyResult.Verdict != DecisionVerdict.Approve)
        {
            command.State = CommandState.Blocked; command.LastError = safetyResult.Code;
            await SaveCommandAndProgressAsync(command, desired, cancellationToken); return;
        }
        if (desired.DailyBudget is not null)
        {
            command.State = CommandState.Blocked;
            command.LastError = "ADS_BUDGET_RESULT_UNKNOWN_REQUIRES_NEW_DECISION";
            command.ReconciledAtUtc = DateTime.UtcNow;
            command.ReconciliationEvidenceJson = "{\"result\":\"NotSafelyReadable\"}";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken); return;
        }
        var execution = new ProviderCommandExecution(command, ad, desired);
        try
        {
            var matches = await DeliveryMatchesAsync(vault.Unprotect(connection.ProtectedAccessToken),
                execution, cancellationToken);
            command.ReconciledAtUtc = DateTime.UtcNow;
            command.ReconciliationEvidenceJson = JsonSerializer.Serialize(new { hierarchyMatches = matches, desired = desired.Status });
            if (matches)
                await CompleteAsync(execution, "ReconciledReadBack", cancellationToken);
            else
            {
                command.State = CommandState.Failed;
                command.LastError = "ADS_UNKNOWN_RESULT_NOT_APPLIED_NEW_DECISION_REQUIRED";
                AdvertisingAudit.Add(db, command.ProjectId, "ProviderCommandReconciledNotApplied",
                    nameof(ExecutionCommand), command.Id, new { hierarchyMatches = matches, desired = desired.Status, command.RequestFingerprint });
                await SaveCommandAndProgressAsync(command, desired, cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            command.State = CommandState.Unknown;
            command.LastError = ex.StatusCode?.ToString() ?? "ADS_RECONCILIATION_UNKNOWN";
            await SaveCommandAndProgressAsync(command, desired, cancellationToken);
        }
    }

    private async Task CompleteAsync(
        ProviderCommandExecution execution,
        string completionSource,
        CancellationToken cancellationToken)
    {
        if (execution.Desired.DailyBudget is not null)
            execution.Ad.DailyBudget = execution.Desired.DailyBudget.Value;
        if (!string.IsNullOrWhiteSpace(execution.Desired.Status))
        {
            execution.Ad.ConfiguredStatus = execution.Desired.Status == "ACTIVE"
                ? ManagedDeliveryState.Active : ManagedDeliveryState.Paused;
            execution.Ad.EffectiveStatus = execution.Desired.Status;
        }
        execution.Ad.LastSyncedAtUtc = DateTime.UtcNow;
        execution.Command.State = CommandState.Succeeded;
        execution.Command.CompletedAtUtc = DateTime.UtcNow;
        AdvertisingAudit.Add(db, execution.Command.ProjectId, "ProviderCommandSucceeded",
            "ExecutionCommand", execution.Command.Id,
            new
            {
                execution.Command.CommandType,
                execution.Command.TargetExternalId,
                execution.Command.AttemptCount,
                completionSource
            });
        var decision = await db.AdvertisingDecisions.IgnoreQueryFilters().SingleAsync(item =>
            item.ProjectId == execution.Command.ProjectId
            && item.Id == execution.Command.DecisionId, cancellationToken);
        decision.State = DecisionState.Executed; decision.EvaluateAfterUtc = DateTime.UtcNow.AddHours(2);
        await SaveCommandAndProgressAsync(execution.Command, execution.Desired, cancellationToken);
    }

    private async Task SaveCommandAndProgressAsync(
        ExecutionCommand command,
        AdvertisingProviderCommand desired,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await PersistProtectiveProgressAsync(command, desired, cancellationToken);
    }

    private async Task PersistProtectiveProgressAsync(
        ExecutionCommand command,
        AdvertisingProviderCommand desired,
        CancellationToken cancellationToken)
    {
        if (desired.StopId is { } stopId
            && command.IdempotencyKey.StartsWith($"emergency:{stopId:N}:", StringComparison.Ordinal))
            await PersistEmergencyProgressAsync(command.ProjectId, stopId, cancellationToken);

        if (desired.DisableRequestId is { } disableRequestId
            && command.IdempotencyKey.StartsWith($"disable:{disableRequestId:N}:", StringComparison.Ordinal))
            await PersistDisableProgressAsync(command.ProjectId, disableRequestId, cancellationToken);
    }

    private async Task PersistEmergencyProgressAsync(
        Guid projectId,
        Guid stopId,
        CancellationToken cancellationToken)
    {
        await PersistWithAggregateLockAsync($"ads-stop:{projectId:N}", async () =>
        {
            var stop = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == projectId && item.Id == stopId && item.ResumedAtUtc == null,
                cancellationToken);
            if (stop is null) return;
            var commands = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AsNoTracking().Where(item =>
                item.ProjectId == projectId
                && item.IdempotencyKey.StartsWith($"emergency:{stopId:N}:")).ToListAsync(cancellationToken);
            var activeManagedAds = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
            var emergencyProgress = AdvertisingProtectiveProgress.Emergency(
                commands, AdvertisingProtectiveProgress.RequiringPause(activeManagedAds));
            stop.State = emergencyProgress.State;
            stop.ProgressJson = emergencyProgress.Progress.GetRawText();
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task PersistDisableProgressAsync(
        Guid projectId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        await PersistWithAggregateLockAsync($"ads-disable:{projectId:N}:{requestId:N}", async () =>
        {
            var request = await db.AdvertisingDisableRequests.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == projectId && item.Id == requestId, cancellationToken);
            if (request is null) return;
            var commands = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AsNoTracking().Where(item =>
                item.ProjectId == projectId
                && item.IdempotencyKey.StartsWith($"disable:{requestId:N}:")).ToListAsync(cancellationToken);
            var activeManagedAds = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
            var disableProgress = AdvertisingProtectiveProgress.Disable(
                request, commands, AdvertisingProtectiveProgress.RequiringPause(activeManagedAds));
            request.State = disableProgress.State;
            request.ProgressJson = disableProgress.Progress.GetRawText();
            request.CompletedAtUtc = disableProgress.CompletedAtUtc;
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task PersistWithAggregateLockAsync(
        string lockKey,
        Func<Task> persistProgress,
        CancellationToken cancellationToken)
    {
        var useDatabaseLock = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            && db.Database.CurrentTransaction is null;
        if (!useDatabaseLock)
        {
            await persistProgress();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        await persistProgress();
        await transaction.CommitAsync(cancellationToken);
    }

    private static AdvertisingProviderCommand? DeserializeDesired(ExecutionCommand command) =>
        JsonSerializer.Deserialize<AdvertisingProviderCommand>(command.DesiredStateJson, CommandJsonOptions);

    private static bool TargetMatches(ExecutionCommand command, AdvertisingProviderCommand desired,
        ManagedAdvertisement ad) => desired.DailyBudget is not null
        ? command.TargetExternalId == (ad.BudgetOwnerExternalId ?? ad.AdSetExternalId)
        : desired.ResourceType switch
        {
            "Campaign" => command.TargetExternalId == ad.CampaignExternalId,
            "AdSet" => command.TargetExternalId == ad.AdSetExternalId,
            "Ad" or null or "" => command.TargetExternalId == ad.AdExternalId,
            _ => false
        };

    private async Task StopForTargetMismatchAsync(Guid projectId, ExecutionCommand source,
        CancellationToken cancellationToken)
    {
        var emergencyStop = await emergencyStops.ActivateAsync(projectId, EmergencyTrigger.CrossProjectGuard,
            $"Provider target mismatch was blocked for command {source.Id:N}.", cancellationToken: cancellationToken);
        foreach (var protectiveCommandId in emergencyStop.CommandIds.Where(id => id != source.Id))
            await ExecuteAsync(projectId, protectiveCommandId, cancellationToken);
    }
}

internal sealed record AdvertisingProviderCommand(
    Guid AdId,
    string? Status = null,
    decimal? DailyBudget = null,
    string? ResourceType = null,
    Guid? StopId = null,
    Guid? DisableRequestId = null);

internal sealed record ProviderCommandExecution(
    ExecutionCommand Command,
    ManagedAdvertisement Ad,
    AdvertisingProviderCommand Desired);
