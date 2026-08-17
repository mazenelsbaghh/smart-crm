using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using StackExchange.Redis;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using System.Text.Json;

namespace Modules.Advertising.Jobs;

public sealed class AdvertisingRecurringJobs(AppDbContext db, IConnectionMultiplexer redis, MetaAdsClient meta, MetaInsightsClient insights,
    AdvertisingSecretVault vault, AllocationPolicyService allocationPolicy, AdvertisingEvidenceService evidenceService, IBackgroundJobClient jobs)
{
    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task DeliverConversionsAsync()
    {
        var projects = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.State == ConversionState.Verified || x.State == ConversionState.Corrected || x.State == ConversionState.DeliveryFailed).Select(x => x.ProjectId).Distinct().ToListAsync();
        foreach (var projectId in projects)
            await WithProjectLease(projectId, "conversion-delivery", TimeSpan.FromSeconds(55), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready);
                if (connection?.DatasetExternalId is null || connection.ProtectedAccessToken is null) return;
                var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && (x.State == ConversionState.Verified || x.State == ConversionState.Corrected || x.State == ConversionState.DeliveryFailed)).OrderBy(x => x.OccurredAtUtc).Take(100).ToListAsync();
                foreach (var conversion in conversions)
                {
                    IReadOnlyDictionary<string, string>? matchData = null;
                    if (conversion.ConsentState == ConsentState.Granted && conversion.ProtectedMatchData is not null)
                        matchData = JsonSerializer.Deserialize<Dictionary<string, string>>(vault.Unprotect(conversion.ProtectedMatchData));
                    var attemptNumber = await db.AdvertisingConversionDeliveryAttempts.IgnoreQueryFilters().CountAsync(x => x.ProjectId == projectId && x.ConversionId == conversion.Id) + 1;
                    var attempt = new ConversionDeliveryAttempt { ProjectId = projectId, ConversionId = conversion.Id, AttemptNumber = attemptNumber, AttemptedAtUtc = DateTime.UtcNow };
                    db.AdvertisingConversionDeliveryAttempts.Add(attempt);
                    try
                    {
                        await meta.SendConversionAsync(vault.Unprotect(connection.ProtectedAccessToken), new MetaConversionRequest(connection.DatasetExternalId, conversion, matchData), CancellationToken.None);
                        attempt.State = "Delivered"; conversion.State = ConversionState.Delivered;
                    }
                    catch (HttpRequestException ex)
                    {
                        attempt.State = "Failed"; attempt.ErrorCode = ex.StatusCode?.ToString() ?? "MetaRequestFailed"; conversion.State = ConversionState.DeliveryFailed;
                    }
                }
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task MonitorSpendAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "spend", TimeSpan.FromMinutes(4), async () =>
            {
                var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Active);
                if (envelope is null) return;
                var start = DateTime.UtcNow.Date;
                var spend = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.IntervalStartUtc >= start).SumAsync(x => x.Spend);
                if (!AdvertisingOperationalPolicy.MustEmergencyStop(spend, envelope.DailyCap)) return;
                await ActivateAutomaticStop(projectId, EmergencyTrigger.CapRisk, $"Observed spend {spend} reached daily cap {envelope.DailyCap}.");
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 540)]
    public async Task SynchronizeAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "sync", TimeSpan.FromMinutes(9), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId);
                if (connection?.State != AdvertisingConnectionState.Ready) return;
                if (connection.ProtectedAccessToken is null) return;
                var token = vault.Unprotect(connection.ProtectedAccessToken);
                var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdExternalId != null).ToListAsync();
                foreach (var ad in ads)
                {
                    var provider = await insights.GetAdStateAsync(token, ad.AdExternalId!, CancellationToken.None);
                    ad.EffectiveStatus = provider.EffectiveStatus; ad.LastSyncedAtUtc = DateTime.UtcNow;
                    ad.ProviderStateHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{provider.Status}:{provider.EffectiveStatus}:{provider.DailyBudget}"))).ToLowerInvariant();
                    if (Math.Abs(provider.DailyBudget - ad.DailyBudget) > .01m)
                    {
                        db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "ProviderDrift", Severity = "Warning", Summary = "Meta budget differs from the last authorized local state.", DetectedAtUtc = DateTime.UtcNow, EvidenceJson = JsonSerializer.Serialize(new { adId = ad.Id, local = ad.DailyBudget, provider = provider.DailyBudget }) });
                    }
                }
                var unknown = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.State == CommandState.Unknown).ToListAsync();
                foreach (var command in unknown)
                {
                    var ad = ads.SingleOrDefault(x => x.AdExternalId == command.TargetExternalId || x.AdSetExternalId == command.TargetExternalId || x.BudgetOwnerExternalId == command.TargetExternalId);
                    if (ad is null) continue;
                    command.State = CommandState.Reconciling;
                    if (command.CommandType == "PauseAd" && ad.EffectiveStatus == "PAUSED") command.State = CommandState.Succeeded;
                    else if (command.CommandType == "ResumeAd" && ad.EffectiveStatus == "ACTIVE") command.State = CommandState.Succeeded;
                    else command.State = CommandState.Failed;
                }
                connection.LastSyncAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1500)]
    public async Task PullInsightsAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "insights", TimeSpan.FromMinutes(25), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready);
                if (connection?.ProtectedAccessToken is null || connection.AdAccountExternalId is null) return;
                var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdExternalId != null).ToDictionaryAsync(x => x.AdExternalId!);
                var latest = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId).MaxAsync(x => (DateTime?)x.IntervalEndUtc);
                var since = DateOnly.FromDateTime((latest ?? DateTime.UtcNow.AddDays(-7)).AddDays(-1));
                var rows = await insights.GetAdInsightsAsync(vault.Unprotect(connection.ProtectedAccessToken), connection.AdAccountExternalId, since, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
                foreach (var row in rows)
                {
                    if (!ads.TryGetValue(row.AdExternalId, out var ad)) continue;
                    var existing = await db.AdvertisingInsights.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.TargetId == ad.Id && x.IntervalStartUtc == row.StartUtc && x.IntervalEndUtc == row.EndUtc);
                    if (existing is null) { existing = new InsightsSnapshot { ProjectId = projectId, TargetId = ad.Id, IntervalStartUtc = row.StartUtc, IntervalEndUtc = row.EndUtc }; db.AdvertisingInsights.Add(existing); }
                    existing.Spend = row.Spend; existing.Impressions = row.Impressions; existing.Clicks = row.Clicks; existing.Frequency = row.Frequency;
                    existing.ProviderActionsJson = JsonSerializer.Serialize(new { row.Actions, row.ActionValues }); existing.FetchedAtUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 840)]
    public async Task CheckTrackingAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "tracking", TimeSpan.FromMinutes(14), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId);
                var open = await db.TrackingIncidents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Category == "ConversionTracking" && x.State != IncidentState.Recovered);
                if (connection?.DatasetExternalId is null && open is null)
                {
                    db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "ConversionTracking", Severity = "Critical", Summary = "Facebook Dataset is unavailable; financial changes are frozen.", DetectedAtUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync();
                }
                else if (connection?.DatasetExternalId is not null && open is not null)
                {
                    open.State = IncidentState.Recovered; open.RecoveredAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3000)]
    public async Task RunDecisionCycleAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "decision", TimeSpan.FromMinutes(55), async () =>
            {
                if (await db.TrackingIncidents.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.State != IncidentState.Recovered)) return;
                var stop = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null);
                if (stop) return;
                var since = DateTime.UtcNow.AddHours(-24);
                var snapshots = await db.AdvertisingInsights.IgnoreQueryFilters().CountAsync(x => x.ProjectId == projectId && x.IntervalEndUtc >= since);
                if (snapshots >= 4) { await allocationPolicy.RebalanceAsync(projectId); return; }
                db.AdvertisingDecisions.Add(new AdvertisingDecision
                {
                    ProjectId = projectId, ActionType = "NoChange", TargetType = "Project", EvidenceStartUtc = since,
                    EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = $"{{\"snapshots\":{snapshots}}}", ProposedChangeJson = "{}",
                    RiskClass = "None", State = DecisionState.Waiting, EvaluateAfterUtc = DateTime.UtcNow.AddHours(1)
                });
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task EvaluateFatigueAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "fatigue", TimeSpan.FromHours(5), async () =>
            {
                var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId).ToListAsync();
                foreach (var ad in ads)
                {
                    var rows = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.TargetId == ad.Id && x.IntervalEndUtc >= DateTime.UtcNow.AddDays(-7)).ToListAsync();
                    var outcomes = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdvertisementId == ad.Id && x.OccurredAtUtc >= DateTime.UtcNow.AddDays(-7)).ToListAsync();
                    var result = evidenceService.Evaluate(rows, outcomes, Math.Max(25m, ad.DailyBudget));
                    var creative = await db.AdvertisingCreatives.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == ad.CreativeId);
                    creative.FatigueState = result.Verdict == EvidenceVerdict.Fatigued ? "Fatigued" : result.Verdict == EvidenceVerdict.Wait ? "InsufficientData" : "Fresh";
                    creative.LastAnalyzedAtUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RebalanceAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "rebalance", TimeSpan.FromHours(20), async () => await allocationPolicy.RebalanceAsync(projectId));
    }

    public async Task ReviewImpactAsync()
    {
        var due = await db.AdvertisingDecisions.IgnoreQueryFilters().Where(x => x.State == DecisionState.Executed && x.EvaluateAfterUtc <= DateTime.UtcNow).Take(200).ToListAsync();
        foreach (var decision in due)
        {
            var after = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == decision.ProjectId && (decision.TargetId == null || x.TargetId == decision.TargetId) && x.IntervalStartUtc >= decision.EvidenceEndUtc).ToListAsync();
            db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = decision.ProjectId, DecisionId = decision.Id, ReviewerType = "Impact", Verdict = after.Count > 0 ? DecisionVerdict.Approve : DecisionVerdict.Wait, ReasonsJson = JsonSerializer.Serialize(new { snapshots = after.Count, spend = after.Sum(x => x.Spend) }), EvidenceHash = "impact" });
            decision.EvaluateAfterUtc = after.Count > 0 ? null : DateTime.UtcNow.AddHours(2);
        }
        await db.SaveChangesAsync();
    }

    public async Task CreateTestsAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "tests", TimeSpan.FromHours(36), async () =>
            {
                var eligible = await db.AdvertisingCreatives.IgnoreQueryFilters().CountAsync(x => x.ProjectId == projectId && x.EligibilityState == CreativeEligibility.Eligible);
                var active = await db.ManagedAdvertisements.IgnoreQueryFilters().CountAsync(x => x.ProjectId == projectId && x.ConfiguredStatus == ManagedDeliveryState.Active);
                if (eligible <= active) return;
                db.AdvertisingDecisions.Add(new AdvertisingDecision { ProjectId = projectId, ActionType = "CreateTest", TargetType = "Creative", EvidenceStartUtc = DateTime.UtcNow.AddDays(-3), EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = JsonSerializer.Serialize(new { eligible, active }), ProposedChangeJson = "{}", State = DecisionState.Waiting, RiskClass = "Financial" });
                await db.SaveChangesAsync();
            });
    }

    public async Task AnalyzeStrategyAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "strategy", TimeSpan.FromDays(6), async () =>
            {
                var since = DateTime.UtcNow.AddDays(-7);
                var spend = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.IntervalEndUtc >= since).SumAsync(x => x.Spend);
                var revenue = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.OccurredAtUtc >= since).SumAsync(x => x.CurrentValue ?? 0m);
                db.AdvertisingDecisions.Add(new AdvertisingDecision { ProjectId = projectId, ActionType = "StrategyReview", TargetType = "Project", EvidenceStartUtc = since, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = JsonSerializer.Serialize(new { spend, revenue, roas = spend > 0 ? revenue / spend : 0 }), ProposedChangeJson = "{}", State = DecisionState.Waiting, RiskClass = "None" });
                await db.SaveChangesAsync();
            });
    }

    private async Task<List<Guid>> ActiveProjectIdsAsync() => await db.AutonomyEnvelopes.IgnoreQueryFilters()
        .Where(x => x.State == EnvelopeState.Active).Select(x => x.ProjectId).Distinct().ToListAsync();

    private async Task WithProjectLease(Guid projectId, string job, TimeSpan expiry, Func<Task> work)
    {
        var key = $"advertising:{job}:{projectId:N}";
        var owner = Guid.NewGuid().ToString("N");
        var cache = redis.GetDatabase();
        if (!await cache.StringSetAsync(key, owner, expiry, When.NotExists)) return;
        AdvertisingCycleRun? cycle = null;
        try
        {
            var now = DateTime.UtcNow;
            var bucket = new DateTime(now.Ticks / expiry.Ticks * expiry.Ticks, DateTimeKind.Utc);
            cycle = await db.AdvertisingCycleRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.JobName == job && x.BucketStartUtc == bucket);
            if (cycle?.State is "Running" or "Completed") return;
            if (cycle is null)
            {
                cycle = new AdvertisingCycleRun { ProjectId = projectId, JobName = job, BucketStartUtc = bucket, StartedAtUtc = now };
                db.AdvertisingCycleRuns.Add(cycle);
            }
            else
            {
                cycle.State = "Running"; cycle.StartedAtUtc = now; cycle.CompletedAtUtc = null; cycle.ErrorType = null;
            }
            await db.SaveChangesAsync();
            await work();
            cycle.State = "Completed"; cycle.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            if (cycle is not null)
            {
                cycle.State = "Failed"; cycle.ErrorType = ex.GetType().Name;
                await db.SaveChangesAsync();
            }
            throw;
        }
        finally
        {
            const string release = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
            await cache.ScriptEvaluateAsync(release, [new RedisKey(key)], [new RedisValue(owner)]);
        }
    }

    private async Task ActivateAutomaticStop(Guid projectId, EmergencyTrigger trigger, string reason)
    {
        if (!await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null))
            db.AdvertisingEmergencyStops.Add(new EmergencyStopRecord { ProjectId = projectId, Trigger = trigger, Reason = reason, ActivatedAtUtc = DateTime.UtcNow });
        var envelopes = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active).ToListAsync();
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Suspended;
        var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.ConfiguredStatus == ManagedDeliveryState.Active && x.AdExternalId != null).ToListAsync();
        var decision = new AdvertisingDecision { ProjectId = projectId, ActionType = "PauseAd", TargetType = "EmergencySet", EvidenceStartUtc = DateTime.UtcNow, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = JsonSerializer.Serialize(new { trigger, reason }), ProposedChangeJson = "{\"status\":\"PAUSED\"}", RiskClass = "Protective", State = DecisionState.Approved };
        db.AdvertisingDecisions.Add(decision);
        var commands = new List<ExecutionCommand>();
        foreach (var ad in ads)
        {
            ad.ConfiguredStatus = ManagedDeliveryState.Paused;
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id, IdempotencyKey = $"emergency:{projectId:N}:{ad.Id:N}:{DateTime.UtcNow:yyyyMMddHHmm}", CommandType = "PauseAd", TargetExternalId = ad.AdExternalId, DesiredStateJson = JsonSerializer.Serialize(new { adId = ad.Id, status = "PAUSED" }), RequestFingerprint = $"{ad.AdExternalId}:PAUSED" };
            db.AdvertisingExecutionCommands.Add(command); commands.Add(command);
        }
        await db.SaveChangesAsync();
        foreach (var command in commands) jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, command.Id, CancellationToken.None));
    }
}
