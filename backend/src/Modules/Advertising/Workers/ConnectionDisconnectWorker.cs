using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class ConnectionDisconnectWorker(
    AppDbContext db,
    MetaAdsClient meta,
    AdvertisingSecretVault vault,
    AdvertisingAuditService audit,
    ILogger<ConnectionDisconnectWorker> logger)
{
    public async Task RunAsync(Guid projectId, Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await db.AdvertisingDisconnectOperations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == operationId, cancellationToken);
        if (operation is null || operation.Phase == DisconnectPhase.Completed) return;
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters()
            .SingleAsync(x => x.ProjectId == projectId && x.Id == operation.ConnectionId, cancellationToken);
        try
        {
            if (operation.Mode == DisconnectMode.LeaveRunning)
            {
                operation.Phase = DisconnectPhase.ManualActionRequired;
                operation.RecoveryInstruction = "Credentials are retained for monitoring until managed delivery is transferred or stopped.";
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            operation.Phase = DisconnectPhase.AuthoritySuspended;
            await db.SaveChangesAsync(cancellationToken);
            operation.Phase = DisconnectPhase.ProtectiveStopQueued;
            var token = connection.ProtectedAccessToken is null
                ? throw new AdvertisingException("ADS_CONNECTION_CREDENTIAL_MISSING", "Cannot safely pause managed delivery without the current credential.", 409)
                : vault.Unprotect(connection.ProtectedAccessToken);
            var targets = await db.AdvertisingDisconnectTargets.IgnoreQueryFilters()
                .Where(x => x.ProjectId == projectId && x.DisconnectOperationId == operationId && x.CompletedAtUtc == null)
                .ToListAsync(cancellationToken);
            operation.Phase = DisconnectPhase.ReconcilingPauses;
            foreach (var target in targets)
            {
                await meta.SetAdStatusAsync(token, target.ProviderExternalId, "PAUSED", cancellationToken);
                var effective = await meta.GetDeliveryStatusAsync(token, target.ProviderExternalId, cancellationToken);
                target.ReadBackState = effective;
                if (!string.Equals(effective, "PAUSED", StringComparison.OrdinalIgnoreCase))
                    throw new AdvertisingException("ADS_PROTECTIVE_PAUSE_UNVERIFIED", "Meta did not verify a protective pause.", 409);
                target.CompletedAtUtc = DateTime.UtcNow;
            }

            operation.Phase = DisconnectPhase.DisposingCredential;
            connection.ProtectedAccessToken = null;
            operation.CredentialDisposedAtUtc = DateTime.UtcNow;
            operation.Phase = DisconnectPhase.PublishingRouteTombstone;
            var destinations = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters()
                .Where(x => x.ProjectId == projectId && x.ConnectionId == connection.Id && x.State != AuthorizedDestinationState.Revoked)
                .ToListAsync(cancellationToken);
            foreach (var destination in destinations)
            {
                destination.State = AuthorizedDestinationState.Revoked;
                destination.Version++;
                IntegrationOutbox.Enqueue(db, new AdvertisingWhatsAppDestinationChanged
                {
                    ProjectId = projectId, DestinationId = destination.Id, DestinationVersion = destination.Version,
                    WabaExternalId = destination.WabaExternalId, PhoneNumberExternalId = destination.PhoneNumberExternalId,
                    IntegrationMode = destination.WhatsAppIntegrationMode.ToString(),
                    State = "Revoked", IsTombstone = true, SourceAggregateType = nameof(AuthorizedWhatsAppDestination),
                    SourceAggregateId = destination.Id, SourceVersion = destination.Version
                });
                operation.RouteTombstoneVersion = Math.Max(operation.RouteTombstoneVersion ?? 0, destination.Version);
            }
            connection.State = AdvertisingConnectionState.Revoked;
            operation.Phase = DisconnectPhase.Completed;
            operation.CompletedAtUtc = DateTime.UtcNow;
            audit.Append(new(projectId, "Connection", "DisconnectCompleted", nameof(AdvertisingConnection), connection.Id.ToString(),
                "SystemAutopilot", null, "{}", operation.Id));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            operation.Phase = DisconnectPhase.Failed;
            operation.LastErrorCode = ex is AdvertisingException advertising ? advertising.Code : ex.GetType().Name;
            operation.RecoveryInstruction = "Resume this operation after restoring authorization; do not dispose the credential manually while spend may be active.";
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex, "Advertising disconnect {OperationId} failed in a resumable state", operationId);
            throw;
        }
    }
}
