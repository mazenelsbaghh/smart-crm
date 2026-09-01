using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record DisableResult(
    Guid RequestId,
    AutopilotDisableMode Mode,
    bool ContinuingSpend,
    bool PauseOngoing,
    bool DeliveryMayContinue,
    IReadOnlyList<Guid> CommandIds);
public sealed record AdvertisingDisableStatus(
    Guid Id,
    string Mode,
    string State,
    DateTime? CompletedAtUtc,
    JsonElement Progress);

public sealed class AdvertisingDisableService(AppDbContext db, AdvertisingOwnershipPolicy ownership)
{
    public async Task<DisableResult> DisableAsync(Guid projectId, Guid actorUserId, AutopilotDisableMode mode,
        string reason, bool acknowledgeContinuingSpend, CancellationToken cancellationToken = default)
    {
        if (mode == AutopilotDisableMode.LeaveRunning && !acknowledgeContinuingSpend)
            throw new AdvertisingException("ADS_CONTINUING_SPEND_ACK_REQUIRED", "LeaveRunning requires explicit continuing-spend acknowledgement.", 422);
        var request = new AutopilotDisableRequest
        {
            ProjectId = projectId,
            RequestedByUserId = actorUserId,
            Mode = mode,
            RequestedAtUtc = DateTime.UtcNow,
            Reason = reason.Trim(),
            ContinuingSpendAcknowledgedAtUtc = mode == AutopilotDisableMode.LeaveRunning ? DateTime.UtcNow : null,
            State = mode == AutopilotDisableMode.LeaveRunning ? "MonitoringContinuingSpend" : "PausingManaged"
        };
        db.AdvertisingDisableRequests.Add(request);
        var envelopes = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Suspended;
        var ads = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
        var adsRequiringPause = AdvertisingProtectiveProgress.RequiringPause(ads);
        var commands = new List<ExecutionCommand>();
        if (mode == AutopilotDisableMode.PauseManaged)
        {
            var decision = new AdvertisingDecision
            {
                ProjectId = projectId,
                ActionType = "PauseDelivery",
                TargetType = "NormalDisableManagedSet",
                EvidenceStartUtc = DateTime.UtcNow,
                EvidenceEndUtc = DateTime.UtcNow,
                EvidenceJson = JsonSerializer.Serialize(new { requestId = request.Id, reason }),
                EvidenceHash = Hash($"{request.Id:N}:{reason}"),
                ProposedChangeJson = "{\"status\":\"PAUSED\"}",
                RiskClass = "Protective",
                State = DecisionState.Approved
            };
            db.AdvertisingDecisions.Add(decision);
            foreach (var target in AdvertisingEmergencyStopService.ManagedHierarchyTargets(adsRequiringPause))
            {
                var command = new ExecutionCommand
                {
                    ProjectId = projectId,
                    DecisionId = decision.Id,
                    IdempotencyKey = $"disable:{request.Id:N}:{target.ResourceType}:{target.ExternalId}",
                    CommandType = $"Pause{target.ResourceType}",
                    TargetExternalId = target.ExternalId,
                    ExpectedStateHash = target.Ad.ProviderStateHash,
                    DesiredStateJson = JsonSerializer.Serialize(new
                    {
                        adId = target.Ad.Id,
                        status = "PAUSED",
                        resourceType = target.ResourceType,
                        disableRequestId = request.Id
                    }),
                    RequestFingerprint = Hash($"{target.ResourceType}:{target.ExternalId}:PAUSED:{request.Id:N}")
                };
                db.AdvertisingExecutionCommands.Add(command); commands.Add(command);
            }
        }
        var progress = AdvertisingProtectiveProgress.Disable(request, commands, adsRequiringPause);
        request.State = progress.State;
        request.CompletedAtUtc = progress.CompletedAtUtc;
        request.ProgressJson = progress.Progress.GetRawText();
        AdvertisingAudit.Add(db, projectId, "AutopilotDisabled", nameof(AutopilotDisableRequest), request.Id,
            new
            {
                mode = mode.ToString(),
                managedTargets = ads.Count,
                progress.HasUncommandedManagedDelivery,
                continuingSpendAcknowledged = acknowledgeContinuingSpend
            }, actorUserId);
        await db.SaveChangesAsync(cancellationToken);
        return new(request.Id, mode, progress.DeliveryMayContinue,
            progress.PauseOngoing, progress.DeliveryMayContinue,
            commands.Select(command => command.Id).ToArray());
    }

    public async Task<AdvertisingDisableStatus?> StateAsync(
        Guid projectId,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await db.AdvertisingDisableRequests.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.Id == requestId, cancellationToken);
        if (request is null) return null;
        var commands = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AsNoTracking().Where(item =>
            item.IdempotencyKey.StartsWith($"disable:{request.Id:N}:")).ToListAsync(cancellationToken);
        var activeManagedAds = await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken);
        var adsRequiringPause = AdvertisingProtectiveProgress.RequiringPause(activeManagedAds);
        var progress = AdvertisingProtectiveProgress.Disable(request, commands, adsRequiringPause);
        return new(request.Id, request.Mode.ToString(), progress.State, progress.CompletedAtUtc,
            progress.Progress);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

}
