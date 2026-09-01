using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record EmergencyStopResult(Guid StopId, bool AlreadyActive, IReadOnlyList<Guid> CommandIds);
public sealed record AdvertisingEmergencyStopStatus(
    Guid Id,
    string Trigger,
    string State,
    string Reason,
    DateTime ActivatedAtUtc,
    JsonElement Progress);

public sealed class AdvertisingEmergencyStopService(AppDbContext db, AdvertisingOwnershipPolicy ownership)
{
    public async Task<EmergencyStopResult> ActivateAsync(Guid projectId, EmergencyTrigger trigger, string reason,
        Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var useDatabaseLock = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            && db.Database.CurrentTransaction is null;
        await using var transaction = useDatabaseLock
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (useDatabaseLock)
        {
            var lockKey = $"ads-stop:{projectId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        }
        var existing = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.ResumedAtUtc == null, cancellationToken);
        if (existing is not null)
        {
            var existingCommands = await StopCommands(existing.Id, cancellationToken);
            var existingProgress = await CurrentProgressAsync(projectId, existingCommands, cancellationToken);
            existing.State = existingProgress.State;
            existing.ProgressJson = existingProgress.Progress.GetRawText();
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(existing.Id, true, existingCommands.Select(item => item.Id).ToArray());
        }
        var stop = new EmergencyStopRecord
        {
            ProjectId = projectId,
            Trigger = trigger,
            ActivatedByUserId = actorUserId,
            Reason = reason.Trim(),
            ActivatedAtUtc = DateTime.UtcNow,
            State = "PausingManaged"
        };
        db.AdvertisingEmergencyStops.Add(stop);
        var envelopes = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Suspended;
        var pending = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && (item.State == CommandState.Pending || item.State == CommandState.Claimed || item.State == CommandState.Sent)).ToListAsync(cancellationToken);
        foreach (var command in pending)
        {
            command.State = command.State == CommandState.Sent ? CommandState.Unknown : CommandState.Cancelled;
            command.LastError = "ADS_EMERGENCY_STOP_ACTIVE";
        }
        await db.SaveChangesAsync(cancellationToken);

        var ads = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
        var adsRequiringPause = AdvertisingProtectiveProgress.RequiringPause(ads);
        var decision = new AdvertisingDecision
        {
            ProjectId = projectId,
            ActionType = "PauseDelivery",
            TargetType = "EmergencyManagedSet",
            EvidenceStartUtc = DateTime.UtcNow,
            EvidenceEndUtc = DateTime.UtcNow,
            EvidenceJson = JsonSerializer.Serialize(new { stopId = stop.Id, trigger, reason }),
            EvidenceHash = Hash($"{stop.Id:N}:{trigger}:{reason}"),
            ProposedChangeJson = "{\"status\":\"PAUSED\"}",
            RiskClass = "Protective",
            State = DecisionState.Approved
        };
        db.AdvertisingDecisions.Add(decision);
        var commands = new List<ExecutionCommand>();
        foreach (var target in ManagedHierarchyTargets(adsRequiringPause))
        {
            var key = $"emergency:{stop.Id:N}:{target.ResourceType}:{target.ExternalId}";
            var command = new ExecutionCommand
            {
                ProjectId = projectId,
                DecisionId = decision.Id,
                IdempotencyKey = key,
                CommandType = $"Pause{target.ResourceType}",
                TargetExternalId = target.ExternalId,
                ExpectedStateHash = target.Ad.ProviderStateHash,
                DesiredStateJson = JsonSerializer.Serialize(new
                {
                    adId = target.Ad.Id,
                    status = "PAUSED",
                    resourceType = target.ResourceType,
                    stopId = stop.Id
                }),
                RequestFingerprint = Hash($"{target.ResourceType}:{target.ExternalId}:PAUSED:{stop.Id:N}")
            };
            db.AdvertisingExecutionCommands.Add(command); commands.Add(command);
        }
        var progress = AdvertisingProtectiveProgress.Emergency(commands, adsRequiringPause);
        stop.State = progress.State;
        stop.ProgressJson = progress.Progress.GetRawText();
        AdvertisingAudit.Add(db, projectId, "EmergencyStopActivated", nameof(EmergencyStopRecord), stop.Id,
            new
            {
                trigger = trigger.ToString(),
                reason,
                managedTargets = ads.Count,
                hasUncommandedManagedDelivery = progress.HasUncommandedManagedDelivery,
                blockedCommands = pending.Count
            }, actorUserId);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(stop.Id, false, commands.Select(command => command.Id).ToArray());
    }

    public async Task<AdvertisingEmergencyStopStatus?> StateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var stop = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AsNoTracking()
            .OrderByDescending(item => item.ActivatedAtUtc)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.ResumedAtUtc == null, cancellationToken);
        if (stop is null) return null;
        var commands = await StopCommands(stop.Id, cancellationToken);
        var progress = await CurrentProgressAsync(projectId, commands, cancellationToken);
        return new(stop.Id, stop.Trigger.ToString(), progress.State, stop.Reason, stop.ActivatedAtUtc,
            progress.Progress);
    }

    public async Task ResumeAsync(Guid projectId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var useDatabaseLock = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
            && db.Database.CurrentTransaction is null;
        await using var transaction = useDatabaseLock
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (useDatabaseLock)
        {
            var lockKey = $"ads-stop:{projectId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        }

        var stop = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().SingleAsync(item =>
            item.ProjectId == projectId && item.ResumedAtUtc == null, cancellationToken);
        var connectionReady = await db.AdvertisingConnections.IgnoreQueryFilters().AnyAsync(item =>
            item.ProjectId == projectId && item.State == AdvertisingConnectionState.Ready, cancellationToken);
        var tracking = await db.AdvertisingTrackingHealthSnapshots.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.EvaluatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var commands = await StopCommands(stop.Id, cancellationToken);
        var progress = await CurrentProgressAsync(projectId, commands, cancellationToken);
        var ledgers = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.PeriodStartUtc <= DateTime.UtcNow && item.PeriodEndUtc > DateTime.UtcNow).ToListAsync(cancellationToken);
        var spendFresh = ledgers.Count > 0 && ledgers.All(item => item.LastReconciledAtUtc >= DateTime.UtcNow.AddMinutes(-15));
        var hasTrackingIncident = await db.TrackingIncidents.IgnoreQueryFilters().AnyAsync(item =>
            item.ProjectId == projectId && item.State != IncidentState.Recovered, cancellationToken);
        if (!connectionReady || !AdvertisingOperationalPolicy.HasFreshHealthyTracking(
                tracking, hasTrackingIncident, DateTime.UtcNow, TimeSpan.FromMinutes(30))
            || progress.State != "Paused" || progress.ContinuingSpend
            || commands.Any(item => item.State != CommandState.Succeeded) || !spendFresh)
            throw new AdvertisingException("ADS_RECOVERY_NOT_READY", "Fresh tracking, spend and provider pause read-back are required before resume.", 409);
        stop.State = "Resumed"; stop.ResumedAtUtc = DateTime.UtcNow; stop.ResumedByUserId = actorUserId;
        AdvertisingAudit.Add(db, projectId, "EmergencyStopResumed", nameof(EmergencyStopRecord), stop.Id,
            new { trackingSnapshotId = tracking.Id, pauseCommands = commands.Count, spendLedgers = ledgers.Count }, actorUserId);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public static EmergencyTrigger TriggerFor(bool trackingUnsafe, bool capRisk, bool crossProject,
        bool repeatedFinancialCommands, bool lostAuthorization) => trackingUnsafe ? EmergencyTrigger.TrackingUnsafe
        : capRisk ? EmergencyTrigger.CapRisk : crossProject ? EmergencyTrigger.CrossProjectGuard
        : repeatedFinancialCommands ? EmergencyTrigger.RepeatedFinancialCommands
        : lostAuthorization ? EmergencyTrigger.LostAuthorization : EmergencyTrigger.Provider;

    private async Task<List<ExecutionCommand>> StopCommands(Guid stopId, CancellationToken cancellationToken) =>
        await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AsNoTracking().Where(item =>
            item.IdempotencyKey.StartsWith($"emergency:{stopId:N}:")).ToListAsync(cancellationToken);

    private async Task<EmergencyPauseProgress> CurrentProgressAsync(
        Guid projectId,
        IReadOnlyCollection<ExecutionCommand> commands,
        CancellationToken cancellationToken)
    {
        var activeManagedAds = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
        var adsRequiringPause = AdvertisingProtectiveProgress.RequiringPause(activeManagedAds);
        return AdvertisingProtectiveProgress.Emergency(commands, adsRequiringPause);
    }

    internal static IReadOnlyList<ManagedHierarchyTarget> ManagedHierarchyTargets(IEnumerable<ManagedAdvertisement> ads)
    {
        var targets = new Dictionary<string, ManagedHierarchyTarget>(StringComparer.Ordinal);
        foreach (var ad in ads)
        {
            Add("Campaign", ad.CampaignExternalId, ad);
            Add("AdSet", ad.AdSetExternalId, ad);
            Add("Ad", ad.AdExternalId, ad);
        }
        return targets.Values.ToArray();

        void Add(string resourceType, string? externalId, ManagedAdvertisement ad)
        {
            if (!string.IsNullOrWhiteSpace(externalId))
                targets.TryAdd($"{resourceType}:{externalId}", new(resourceType, externalId, ad));
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record ManagedHierarchyTarget(string ResourceType, string ExternalId, ManagedAdvertisement Ad);
