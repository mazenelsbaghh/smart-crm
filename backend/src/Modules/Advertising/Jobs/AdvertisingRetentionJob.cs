using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Jobs;

public static class AdvertisingRetentionPolicy
{
    public const int AiWorkDays = 90;
    public const int DeliveryAttemptDays = 180;
    public const int ProtectedAttributionDays = 180;
    public const int InsightsYears = 2;
}

public sealed class AdvertisingRetentionJob(AppDbContext db, IBackgroundJobClient jobs,
    AdvertisingAuditService audit) : IIntegrationEventHandler<AdvertisingProjectLifecycleChanged>
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task CompactAsync()
    {
        var now = DateTime.UtcNow;
        await DeleteExpiredOperationalEvidenceAsync(now);
        await StripExpiredAttributionIdentifiersAsync(now.AddDays(-AdvertisingRetentionPolicy.ProtectedAttributionDays));
        await StripExpiredInputsAndSecretsAsync(now.AddDays(-AdvertisingRetentionPolicy.AiWorkDays));
    }

    public async Task HandleAsync(AdvertisingProjectLifecycleChanged lifecycle)
    {
        if (lifecycle.State is not ("Archived" or "Deleted")) return;
        await RevokeActiveEnvelopesAsync(lifecycle.ProjectId);
        var pauseCommands = await CreateLifecyclePauseCommandsAsync(lifecycle);
        await db.SaveChangesAsync();
        foreach (var pauseCommand in pauseCommands)
            jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(lifecycle.ProjectId, pauseCommand.Id, CancellationToken.None));
        jobs.Schedule<AdvertisingRetentionJob>(job => job.FinalizeProjectRevocationAsync(lifecycle.ProjectId), TimeSpan.FromMinutes(15));
    }

    public async Task FinalizeProjectRevocationAsync(Guid projectId)
    {
        if (await HasUnverifiedLifecyclePauseAsync(projectId))
        {
            jobs.Schedule<AdvertisingRetentionJob>(job => job.FinalizeProjectRevocationAsync(projectId), TimeSpan.FromHours(1));
            return;
        }
        await RevokeConnectionAsync(projectId);
        var routeTombstoneCount = await PublishDestinationTombstonesAsync(projectId);
        var compaction = await ScrubProjectProtectedFieldsAsync(projectId);
        RecordProjectCompaction(projectId, routeTombstoneCount, compaction);
        await db.SaveChangesAsync();
    }

    private async Task DeleteExpiredOperationalEvidenceAsync(DateTime now)
    {
        await db.AdvertisingConversionDeliveryAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.AttemptedAtUtc < now.AddDays(-AdvertisingRetentionPolicy.DeliveryAttemptDays)).ExecuteDeleteAsync();
        await db.AdvertisingInsights.IgnoreQueryFilters()
            .Where(insight => insight.IntervalEndUtc < now.AddYears(-AdvertisingRetentionPolicy.InsightsYears)).ExecuteDeleteAsync();
    }

    private async Task StripExpiredAttributionIdentifiersAsync(DateTime cutoff)
    {
        await db.AdvertisingAttributionObservations.IgnoreQueryFilters()
            .Where(observation => observation.MessageOccurredAtUtc < cutoff && observation.ProtectedCtwaClid != null)
            .ExecuteUpdateAsync(update => update.SetProperty(observation => observation.ProtectedCtwaClid, (string?)null)
                .SetProperty(observation => observation.ProtectionPurpose, (string?)null));
        await db.AdvertisingAttributionTouches.IgnoreQueryFilters()
            .Where(touch => touch.TouchedAtUtc < cutoff && touch.ProtectedCtwaClid != null)
            .ExecuteUpdateAsync(update => update.SetProperty(touch => touch.ProtectedCtwaClid, (string?)null));
        await db.AdvertisingConversions.IgnoreQueryFilters()
            .Where(conversion => conversion.OccurredAtUtc < cutoff && conversion.ProtectedMatchData != null)
            .ExecuteUpdateAsync(update => update.SetProperty(conversion => conversion.ProtectedMatchData, (string?)null));
        await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters()
            .Where(sourceEvent => sourceEvent.OccurredAtUtc < cutoff && (sourceEvent.NormalizedPayloadJson != "{}" || sourceEvent.ConsentEvidenceJson != null))
            .ExecuteUpdateAsync(update => update.SetProperty(sourceEvent => sourceEvent.NormalizedPayloadJson, "{}")
                .SetProperty(sourceEvent => sourceEvent.ConsentEvidenceJson, (string?)null));
    }

    private async Task StripExpiredInputsAndSecretsAsync(DateTime cutoff)
    {
        await db.AdvertisingAiWorkItems.IgnoreQueryFilters()
            .Where(work => work.CompletedAtUtc < cutoff && (work.InputJson != "{}" || work.ResultJson != null))
            .ExecuteUpdateAsync(update => update.SetProperty(work => work.InputJson, "{}")
                .SetProperty(work => work.ResultJson, (string?)null));
        await db.AdvertisingWebhookSources.IgnoreQueryFilters()
            .Where(source => source.RevokedAtUtc < cutoff && (source.ProtectedSigningSecret != string.Empty || source.PreviousProtectedSigningSecret != null))
            .ExecuteUpdateAsync(update => update.SetProperty(source => source.ProtectedSigningSecret, string.Empty)
                .SetProperty(source => source.PreviousProtectedSigningSecret, (string?)null));
    }

    private async Task RevokeActiveEnvelopesAsync(Guid projectId)
    {
        var activeEnvelopes = await db.AutonomyEnvelopes.IgnoreQueryFilters()
            .Where(envelope => envelope.ProjectId == projectId && envelope.State == EnvelopeState.Active).ToListAsync();
        foreach (var envelope in activeEnvelopes) envelope.State = EnvelopeState.Revoked;
    }

    private async Task<List<ExecutionCommand>> CreateLifecyclePauseCommandsAsync(AdvertisingProjectLifecycleChanged lifecycle)
    {
        var activeAds = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(ad => ad.ProjectId == lifecycle.ProjectId
            && ad.AdExternalId != null && ad.ConfiguredStatus == ManagedDeliveryState.Active).ToListAsync();
        var decision = LifecyclePauseDecision(lifecycle);
        db.AdvertisingDecisions.Add(decision);
        var pauseCommands = activeAds.Select(ad => LifecyclePauseCommand(lifecycle, decision.Id, ad)).ToList();
        foreach (var pauseCommand in pauseCommands) db.AdvertisingExecutionCommands.Add(pauseCommand);
        foreach (var activeAd in activeAds) activeAd.ConfiguredStatus = ManagedDeliveryState.Paused;
        return pauseCommands;
    }

    private static AdvertisingDecision LifecyclePauseDecision(AdvertisingProjectLifecycleChanged lifecycle) => new()
    {
        ProjectId = lifecycle.ProjectId, ActionType = "PauseAd", TargetType = "ProjectLifecycle",
        EvidenceStartUtc = lifecycle.OccurredOn, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = "{}",
        ProposedChangeJson = "{\"status\":\"PAUSED\"}", State = DecisionState.Approved, RiskClass = "Protective"
    };

    private static ExecutionCommand LifecyclePauseCommand(AdvertisingProjectLifecycleChanged lifecycle,
        Guid decisionId, ManagedAdvertisement advertisement) => new()
    {
        ProjectId = lifecycle.ProjectId, DecisionId = decisionId,
        IdempotencyKey = $"lifecycle:{lifecycle.Id:N}:{advertisement.Id:N}", CommandType = "PauseAd",
        TargetExternalId = advertisement.AdExternalId,
        DesiredStateJson = JsonSerializer.Serialize(new { adId = advertisement.Id, status = "PAUSED" }),
        RequestFingerprint = $"{advertisement.AdExternalId}:PAUSED"
    };

    private Task<bool> HasUnverifiedLifecyclePauseAsync(Guid projectId) =>
        db.AdvertisingExecutionCommands.IgnoreQueryFilters().AnyAsync(command => command.ProjectId == projectId
            && command.IdempotencyKey.StartsWith("lifecycle:") && command.State != CommandState.Succeeded);

    private async Task RevokeConnectionAsync(Guid projectId)
    {
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId);
        if (connection is null) return;
        connection.State = AdvertisingConnectionState.Revoked;
        connection.ProtectedAccessToken = null;
    }

    private async Task<int> PublishDestinationTombstonesAsync(Guid projectId)
    {
        var destinations = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters()
            .Where(destination => destination.ProjectId == projectId && destination.State != AuthorizedDestinationState.Revoked).ToListAsync();
        foreach (var destination in destinations)
        {
            destination.State = AuthorizedDestinationState.Revoked;
            destination.Version++;
            IntegrationOutbox.Enqueue(db, DestinationTombstone(projectId, destination));
        }
        return destinations.Count;
    }

    private static AdvertisingWhatsAppDestinationChanged DestinationTombstone(Guid projectId,
        AuthorizedWhatsAppDestination destination) => new()
    {
        ProjectId = projectId, DestinationId = destination.Id, DestinationVersion = destination.Version,
        WabaExternalId = destination.WabaExternalId, PhoneNumberExternalId = destination.PhoneNumberExternalId,
        IntegrationMode = destination.WhatsAppIntegrationMode.ToString(), State = "Revoked", IsTombstone = true,
        SourceAggregateType = nameof(AuthorizedWhatsAppDestination), SourceAggregateId = destination.Id,
        SourceVersion = destination.Version
    };

    private async Task<RetentionCompactionSummary> ScrubProjectProtectedFieldsAsync(Guid projectId)
    {
        var observations = await db.AdvertisingAttributionObservations.IgnoreQueryFilters().Where(observation => observation.ProjectId == projectId).ToListAsync();
        foreach (var observation in observations) { observation.ProtectedCtwaClid = null; observation.ProtectionPurpose = null; }
        var touches = await db.AdvertisingAttributionTouches.IgnoreQueryFilters().Where(touch => touch.ProjectId == projectId).ToListAsync();
        foreach (var touch in touches) touch.ProtectedCtwaClid = null;
        var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().Where(conversion => conversion.ProjectId == projectId).ToListAsync();
        foreach (var conversion in conversions) conversion.ProtectedMatchData = null;
        var sourceEvents = await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().Where(sourceEvent => sourceEvent.ProjectId == projectId).ToListAsync();
        foreach (var sourceEvent in sourceEvents) { sourceEvent.NormalizedPayloadJson = "{}"; sourceEvent.ConsentEvidenceJson = null; }
        var webhookSources = await db.AdvertisingWebhookSources.IgnoreQueryFilters().Where(source => source.ProjectId == projectId).ToListAsync();
        foreach (var source in webhookSources) RevokeWebhookSource(source);
        var aiWorkItems = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().Where(work => work.ProjectId == projectId).ToListAsync();
        foreach (var work in aiWorkItems) { work.InputJson = "{}"; work.ResultJson = null; }
        return new(observations.Count, touches.Count, conversions.Count, sourceEvents.Count, webhookSources.Count, aiWorkItems.Count);
    }

    private static void RevokeWebhookSource(AdvertisingWebhookSource source)
    {
        source.ProtectedSigningSecret = string.Empty;
        source.PreviousProtectedSigningSecret = null;
        source.IsActive = false;
        source.State = WebhookSourceState.Revoked;
        source.RevokedAtUtc ??= DateTime.UtcNow;
    }

    private void RecordProjectCompaction(Guid projectId, int routeTombstoneCount, RetentionCompactionSummary summary) =>
        audit.Append(new(projectId, "Retention", "ProtectedFieldsCompacted", "Project", projectId.ToString(),
            "System", null, JsonSerializer.Serialize(new
            {
                summary.ObservationCount, summary.TouchCount, summary.ConversionCount, summary.SourceEventCount,
                summary.WebhookSourceCount, summary.AiWorkItemCount, routeTombstoneCount
            }), Guid.NewGuid()));

    private sealed record RetentionCompactionSummary(int ObservationCount, int TouchCount, int ConversionCount,
        int SourceEventCount, int WebhookSourceCount, int AiWorkItemCount);
}
