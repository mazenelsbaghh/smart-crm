using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;

namespace Modules.Advertising.Workers;

public sealed class AdvertisingCommandWorker(AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault, AdvertisingSafetyEngine safety)
{
    public async Task ExecuteAsync(Guid projectId, Guid commandId, CancellationToken cancellationToken = default)
    {
        var claimed = await db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .Where(x => x.ProjectId == projectId && x.Id == commandId && x.State == CommandState.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.State, CommandState.Claimed).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken);
        if (claimed == 0) return;
        var command = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == commandId, cancellationToken);
        var desired = JsonSerializer.Deserialize<AdvertisingProviderCommand>(command.DesiredStateJson) ?? throw new InvalidOperationException("Invalid Advertising command payload.");
        var ad = await db.ManagedAdvertisements.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == desired.AdId, cancellationToken);
        var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
        var action = desired.DailyBudget is not null ? (desired.DailyBudget > ad.DailyBudget ? "IncreaseBudget" : "DecreaseBudget") : desired.Status == "ACTIVE" ? "ResumeAd" : "PauseAd";
        var safetyResult = await safety.EvaluateAsync(new(projectId, action, ad.DailyBudget, desired.DailyBudget ?? ad.DailyBudget, ad.PublisherPlatform, positions), cancellationToken);
        if (safetyResult.Verdict != DecisionVerdict.Approve) { command.State = CommandState.Blocked; command.LastError = safetyResult.Code; await db.SaveChangesAsync(cancellationToken); return; }
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (connection?.ProtectedAccessToken is null) { command.State = CommandState.Blocked; command.LastError = "ConnectionUnavailable"; await db.SaveChangesAsync(cancellationToken); return; }
        try
        {
            command.State = CommandState.Sent;
            var token = vault.Unprotect(connection.ProtectedAccessToken);
            if (desired.DailyBudget is not null)
            {
                var budgetOwnerId = ad.BudgetOwnerExternalId ?? ad.AdSetExternalId;
                if (budgetOwnerId is null) throw new InvalidOperationException("Managed ad has no Meta budget owner.");
                await meta.SetDailyBudgetAsync(token, budgetOwnerId, desired.DailyBudget.Value, cancellationToken);
                ad.DailyBudget = desired.DailyBudget.Value;
            }
            else
            {
                await meta.SetAdStatusAsync(token, command.TargetExternalId!, desired.Status!, cancellationToken);
                ad.ConfiguredStatus = desired.Status == "ACTIVE" ? ManagedDeliveryState.Active : ManagedDeliveryState.Paused;
                ad.EffectiveStatus = desired.Status!;
            }
            ad.LastSyncedAtUtc = DateTime.UtcNow;
            command.State = CommandState.Succeeded;
            AdvertisingAudit.Add(db, projectId, "ProviderCommandSucceeded", "ExecutionCommand", command.Id, new { command.CommandType, command.TargetExternalId, command.AttemptCount });
            var decision = await db.AdvertisingDecisions.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == command.DecisionId, cancellationToken);
            decision.State = DecisionState.Executed; decision.EvaluateAfterUtc = DateTime.UtcNow.AddHours(2);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            command.State = CommandState.Unknown; command.LastError = ex.StatusCode?.ToString() ?? "MetaRequestUnknown";
            AdvertisingAudit.Add(db, projectId, "ProviderCommandUnknown", "ExecutionCommand", command.Id, new { command.CommandType, errorCode = command.LastError, command.AttemptCount });
            db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "CommandReconciliation", Severity = "Critical", Summary = "Meta command outcome is unknown; blind retry is blocked.", DetectedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal sealed record AdvertisingProviderCommand(Guid AdId, string? Status = null, decimal? DailyBudget = null);
