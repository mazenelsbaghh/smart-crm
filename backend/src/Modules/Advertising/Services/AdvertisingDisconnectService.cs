using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Workers;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AdvertisingDisconnectService(
    AppDbContext db,
    IBackgroundJobClient jobs,
    AdvertisingAuditService audit)
{
    public async Task<ConnectionDisconnectOperation> RequestAsync(Guid projectId, Guid connectionId, Guid actorUserId,
        DisconnectMode? requestedMode, DateTime? leaveRunningAcknowledgedAtUtc, CancellationToken cancellationToken = default)
    {
        var mode = AdvertisingDisconnectPolicy.NormalizeMode(requestedMode);
        if (mode == DisconnectMode.LeaveRunning && !AdvertisingDisconnectPolicy.CanLeaveRunning(leaveRunningAcknowledgedAtUtc, DateTime.UtcNow))
            throw new AdvertisingException("ADS_LEAVE_RUNNING_ACK_REQUIRED", "Leaving spend running requires a fresh explicit acknowledgement.", 422);
        var connection = await db.AdvertisingConnections.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == connectionId, cancellationToken)
            ?? throw new AdvertisingException("ADS_CONNECTION_NOT_FOUND", "Connection not found.", 404);
        var existing = await db.AdvertisingDisconnectOperations
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.ConnectionId == connectionId && x.CompletedAtUtc == null && x.Phase != DisconnectPhase.Failed, cancellationToken);
        if (existing is not null) return existing;

        var operation = new ConnectionDisconnectOperation
        {
            ProjectId = projectId,
            ConnectionId = connectionId,
            Mode = mode,
            RequestedByUserId = actorUserId,
            RequestedAtUtc = DateTime.UtcNow,
            ContinuingOrUnmonitoredSpendAcknowledgedAtUtc = mode == DisconnectMode.LeaveRunning ? leaveRunningAcknowledgedAtUtc : null
        };
        db.AdvertisingDisconnectOperations.Add(operation);
        var envelopes = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var envelope in envelopes) { envelope.State = EnvelopeState.Suspended; envelope.Version++; }
        connection.State = AdvertisingConnectionState.Disconnecting;
        connection.Version++;

        if (mode != DisconnectMode.LeaveRunning)
        {
            var owned = await db.ManagedAdvertisements.AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.ConnectionId == connectionId && x.ManagementSource == "CreatedBySystem" && x.AdExternalId != null)
                .Select(x => new { x.Id, x.OwnershipRecordId, ExternalId = x.AdExternalId! }).ToListAsync(cancellationToken);
            db.AdvertisingDisconnectTargets.AddRange(owned.Select(target => new ConnectionDisconnectTarget
            {
                ProjectId = projectId,
                DisconnectOperationId = operation.Id,
                OwnershipRecordId = target.OwnershipRecordId ?? Guid.Empty,
                TargetType = "Ad",
                TargetId = target.Id,
                ProviderExternalId = target.ExternalId,
                DesiredState = "PAUSED"
            }));
        }
        audit.Append(new(projectId, "Connection", "DisconnectRequested", nameof(AdvertisingConnection), connectionId.ToString(),
            "User", actorUserId, System.Text.Json.JsonSerializer.Serialize(new { mode = mode.ToString() }), operation.Id));
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<ConnectionDisconnectWorker>(worker => worker.RunAsync(projectId, operation.Id, CancellationToken.None));
        return operation;
    }
}
