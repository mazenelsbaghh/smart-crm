using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Workers;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Jobs;

public sealed class AdvertisingRetentionJob(AppDbContext db, IBackgroundJobClient jobs) : IIntegrationEventHandler<AdvertisingProjectLifecycleChanged>
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task CompactAsync()
    {
        var now = DateTime.UtcNow;
        await db.IntegrationOutboxMessages.Where(x => x.PublishedAtUtc < now.AddDays(-30)).ExecuteDeleteAsync();
        await db.IntegrationInboxReceipts.Where(x => x.ProcessedAtUtc < now.AddDays(-90)).ExecuteDeleteAsync();
        await db.AdvertisingConversionDeliveryAttempts.IgnoreQueryFilters().Where(x => x.AttemptedAtUtc < now.AddDays(-180)).ExecuteDeleteAsync();
        await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.IntervalEndUtc < now.AddYears(-2)).ExecuteDeleteAsync();
    }

    public async Task HandleAsync(AdvertisingProjectLifecycleChanged e)
    {
        if (e.State is not ("Archived" or "Deleted")) return;
        var envelopes = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(x => x.ProjectId == e.ProjectId && x.State == EnvelopeState.Active).ToListAsync();
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Revoked;
        var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == e.ProjectId && x.AdExternalId != null && x.ConfiguredStatus == ManagedDeliveryState.Active).ToListAsync();
        var decision = new AdvertisingDecision { ProjectId = e.ProjectId, ActionType = "PauseAd", TargetType = "ProjectLifecycle", EvidenceStartUtc = e.OccurredOn, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = "{}", ProposedChangeJson = "{\"status\":\"PAUSED\"}", State = DecisionState.Approved, RiskClass = "Protective" };
        db.AdvertisingDecisions.Add(decision); var commands = new List<ExecutionCommand>();
        foreach (var ad in ads)
        {
            ad.ConfiguredStatus = ManagedDeliveryState.Paused;
            var command = new ExecutionCommand { ProjectId = e.ProjectId, DecisionId = decision.Id, IdempotencyKey = $"lifecycle:{e.Id:N}:{ad.Id:N}", CommandType = "PauseAd", TargetExternalId = ad.AdExternalId, DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new { adId = ad.Id, status = "PAUSED" }), RequestFingerprint = $"{ad.AdExternalId}:PAUSED" };
            db.AdvertisingExecutionCommands.Add(command); commands.Add(command);
        }
        await db.SaveChangesAsync();
        foreach (var command in commands) jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(e.ProjectId, command.Id, CancellationToken.None));
        jobs.Schedule<AdvertisingRetentionJob>(job => job.FinalizeProjectRevocationAsync(e.ProjectId), TimeSpan.FromMinutes(15));
    }

    public async Task FinalizeProjectRevocationAsync(Guid projectId)
    {
        var unfinished = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.IdempotencyKey.StartsWith("lifecycle:") && x.State != CommandState.Succeeded && x.State != CommandState.Failed && x.State != CommandState.Blocked);
        if (unfinished) { jobs.Schedule<AdvertisingRetentionJob>(job => job.FinalizeProjectRevocationAsync(projectId), TimeSpan.FromHours(1)); return; }
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId);
        if (connection is not null) { connection.State = AdvertisingConnectionState.Revoked; connection.ProtectedAccessToken = null; await db.SaveChangesAsync(); }
    }
}
