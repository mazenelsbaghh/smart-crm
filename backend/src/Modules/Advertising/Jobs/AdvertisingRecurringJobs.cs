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

public sealed class AdvertisingRecurringJobs(AppDbContext db, IConnectionMultiplexer redis, MetaInsightsClient insights,
    AdvertisingSecretVault vault, AllocationPolicyService allocationPolicy, AdvertisingEvidenceService evidenceService, IBackgroundJobClient jobs,
    WhatsAppCreativeTestService whatsAppTests, AdvertisingDecisionService decisions, ConversionDeliveryJob conversionDelivery,
    AdvertisingTrackingHealthService trackingHealth, AdvertisingDecisionImpactService impactService,
    AdvertisingEmergencyStopService emergencyStopService, AdvertisingOwnershipPolicy ownership,
    AdvertisingCampaignBootstrapService campaignBootstrap, BudgetAllocator budgets)
{
    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public Task DeliverConversionsAsync() => conversionDelivery.RunAsync();

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task BootstrapCampaignsAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "bootstrap", TimeSpan.FromMinutes(4), async () =>
                await campaignBootstrap.EnsurePausedHierarchyAsync(projectId));
    }

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task MonitorSpendAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "spend", TimeSpan.FromMinutes(4), async () =>
            {
                var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Active);
                if (envelope is null) return;
                var start = AdvertisingSchedulePolicy.DayStartUtc(DateTime.UtcNow, envelope.ReportingTimezoneIana);
                var managedAds = await ownership.ManagedAdsAsync(projectId, activeOnly: false);
                var managedAdIds = managedAds.Select(item => item.Id).ToArray();
                var analyticalSpend = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x =>
                    x.ProjectId == projectId && managedAdIds.Contains(x.TargetId)
                    && x.IntervalStartUtc >= start && x.IsCurrent).SumAsync(x => x.Spend);
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                    item.ProjectId == projectId && item.State == AdvertisingConnectionState.Ready);
                var spend = analyticalSpend;
                if (connection?.ProtectedAccessToken is not null && connection.AdAccountExternalId is not null)
                {
                    var managedIds = managedAds.Where(item => item.AdExternalId != null)
                        .Select(item => item.AdExternalId!).ToHashSet(StringComparer.Ordinal);
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var liveRows = await insights.GetAdInsightsAsync(vault.Unprotect(connection.ProtectedAccessToken),
                        connection.AdAccountExternalId, today, today, CancellationToken.None);
                    spend = Math.Max(analyticalSpend, liveRows.Where(row => managedIds.Contains(row.AdExternalId)).Sum(row => row.Spend));
                }
                var ledgers = await budgets.EnsureCurrentLedgersAsync(db, envelope);
                foreach (var ledger in ledgers) { ledger.ObservedSpend = Math.Max(ledger.ObservedSpend, spend); ledger.LastReconciledAtUtc = DateTime.UtcNow; }
                var exposure = ledgers.Count == 0 ? spend : Math.Max(spend, ledgers.Max(AdvertisingSpendGuard.Exposure));
                await db.SaveChangesAsync();
                if (!AdvertisingOperationalPolicy.MustEmergencyStop(exposure, envelope.DailyCap)) return;
                var abnormal = ledgers.Any(ledger => AdvertisingOperationalPolicy.IsAbnormalForecast(
                    Math.Max(ledger.ForecastSpend, ledger.ObservedSpend + ledger.DelayedSpendEstimate), ledger.UsableCap));
                await ActivateAutomaticStop(projectId, abnormal ? EmergencyTrigger.AbnormalSpend : EmergencyTrigger.CapRisk,
                    $"Guarded spend exposure {exposure} reached the authorized cap {envelope.DailyCap}.");
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 540)]
    public async Task SynchronizeAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "sync", TimeSpan.FromMinutes(9), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId);
                var activeManaged = await db.ManagedAdvertisements.IgnoreQueryFilters().AnyAsync(item =>
                    item.ProjectId == projectId && item.ConfiguredStatus == ManagedDeliveryState.Active
                    && item.OwnershipRecordId != null);
                if (activeManaged && (connection?.State != AdvertisingConnectionState.Ready
                    || connection.ProtectedAccessToken is null))
                {
                    await ActivateAutomaticStop(projectId, EmergencyTrigger.LostAuthorization,
                        "Meta authorization was lost while managed delivery could still spend.");
                    return;
                }
                if (connection?.ProtectedAccessToken is null) return;
                var recentFinancialCommands = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().CountAsync(item =>
                    item.ProjectId == projectId && item.CreatedAt >= DateTime.UtcNow.AddMinutes(-10)
                    && (item.CommandType.Contains("Budget") || item.CommandType.Contains("Resume")));
                if (recentFinancialCommands >= 5)
                {
                    await ActivateAutomaticStop(projectId, EmergencyTrigger.RepeatedFinancialCommands,
                        "Repeated financial commands exceeded the ten-minute safety threshold.");
                    return;
                }
                var token = vault.Unprotect(connection.ProtectedAccessToken);
                var ads = await ownership.ManagedAdsAsync(projectId, activeOnly: false);
                foreach (var ad in ads)
                {
                    var provider = await insights.GetAdStateAsync(token, ad.AdExternalId!, CancellationToken.None);
                    ad.EffectiveStatus = provider.EffectiveStatus; ad.LastSyncedAtUtc = DateTime.UtcNow;
                    ad.ProviderStateHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{provider.Status}:{provider.EffectiveStatus}:{provider.DailyBudget}"))).ToLowerInvariant();
                }
                var obsoleteAdBudgetDrift = await db.TrackingIncidents.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
                    && item.Category == "ProviderDrift" && item.Severity == "Warning" && item.State != IncidentState.Recovered).ToListAsync();
                foreach (var incident in obsoleteAdBudgetDrift)
                {
                    incident.State = IncidentState.Recovered;
                    incident.RecoveredAtUtc = DateTime.UtcNow;
                }
                var unknown = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.State == CommandState.Unknown).ToListAsync();
                foreach (var command in unknown)
                    jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, command.Id, CancellationToken.None));
                connection.LastSyncAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await QueueHierarchyRecoveryAsync(projectId, ads);
            });
    }

    private async Task QueueHierarchyRecoveryAsync(Guid projectId, IReadOnlyCollection<ManagedAdvertisement> ads)
    {
        var blockedAds = ads.Where(ad => ad.ManagementSource == "CreatedBySystem"
            && ad.EffectiveStatus is "ADSET_PAUSED" or "CAMPAIGN_PAUSED").ToList();
        if (blockedAds.Count == 0) return;

        foreach (var ad in blockedAds) ad.ConfiguredStatus = ManagedDeliveryState.Paused;
        await db.SaveChangesAsync();
        var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, CancellationToken.None, adIds: blockedAds.Select(ad => ad.Id).ToList());
        foreach (var commandId in commandIds)
            jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
    }

    [DisableConcurrentExecution(timeoutInSeconds: 1500)]
    public async Task PullInsightsAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "insights", TimeSpan.FromMinutes(25), async () =>
            {
                var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready);
                if (connection?.ProtectedAccessToken is null || connection.AdAccountExternalId is null) return;
                if (string.IsNullOrWhiteSpace(connection.AccountCurrency))
                    throw new AdvertisingException("ADS_REPORTING_CURRENCY_UNKNOWN", "Meta account currency has not been verified.", 409);
                if (string.IsNullOrWhiteSpace(connection.AccountTimezoneIana))
                    throw new AdvertisingException("ADS_REPORTING_TIMEZONE_UNKNOWN", "Meta account timezone has not been verified.", 409);
                _ = AdvertisingSchedulePolicy.DayStartUtc(DateTime.UtcNow, connection.AccountTimezoneIana);
                var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdExternalId != null).ToDictionaryAsync(x => x.AdExternalId!);
                var latest = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId).MaxAsync(x => (DateTime?)x.IntervalEndUtc);
                var since = DateOnly.FromDateTime((latest ?? DateTime.UtcNow.AddDays(-7)).AddDays(-1));
                var rows = await insights.GetAdInsightsAsync(vault.Unprotect(connection.ProtectedAccessToken), connection.AdAccountExternalId, since, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
                foreach (var row in rows)
                {
                    if (!ads.TryGetValue(row.AdExternalId, out var ad)) continue;
                    var existing = await db.AdvertisingInsights.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.TargetId == ad.Id
                        && x.IntervalStartUtc == row.StartUtc && x.IntervalEndUtc == row.EndUtc && x.BreakdownHash == "none" && x.IsCurrent);
                    var fingerprint = MetaInsightRevisionPolicy.Fingerprint(row);
                    if (existing is not null && MetaInsightRevisionPolicy.Fingerprint(existing) == fingerprint) continue;
                    if (existing is not null) existing.IsCurrent = false;
                    db.AdvertisingInsights.Add(new InsightsSnapshot
                    {
                        ProjectId = projectId, ConnectionId = connection.Id, TargetId = ad.Id,
                        IntervalStartUtc = row.StartUtc, IntervalEndUtc = row.EndUtc, Spend = row.Spend,
                        Impressions = row.Impressions, Clicks = row.Clicks, Frequency = row.Frequency,
                        BreakdownHash = "none", Currency = connection.AccountCurrency,
                        AccountTimezone = connection.AccountTimezoneIana,
                        AttributionSetting = "provider-reported", ProviderActionsJson = JsonSerializer.Serialize(new { row.Actions }),
                        ProviderActionValuesJson = JsonSerializer.Serialize(new { row.ActionValues }), FetchedAtUtc = DateTime.UtcNow,
                        SourceFreshnessUtc = row.EndUtc, FetchRunId = Guid.NewGuid(), Revision = (existing?.Revision ?? 0) + 1,
                        SupersedesSnapshotId = existing?.Id, IsCurrent = true
                    });
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
                var destinations = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().Where(x => x.ProjectId == projectId
                    && x.State == AuthorizedDestinationState.Eligible).ToListAsync();
                var open = await db.TrackingIncidents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Category == "ConversionTracking" && x.State != IncidentState.Recovered);
                var snapshots = new List<TrackingHealthSnapshot>();
                foreach (var destination in destinations) snapshots.Add(await trackingHealth.EvaluateAsync(projectId, destination.Id));
                if (snapshots.Any(snapshot => snapshot.State == TrackingHealthState.Unsafe))
                {
                    if (open is null)
                    {
                        db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "ConversionTracking",
                            Severity = "Critical", Summary = "WhatsApp advertising tracking is unsafe; financial decisions must WAIT.",
                            EvidenceJson = JsonSerializer.Serialize(new { snapshots = snapshots.Select(snapshot => new { snapshot.Id, snapshot.ReasonCodesJson }) }),
                            DetectedAtUtc = DateTime.UtcNow });
                    }
                    await ActivateAutomaticStop(projectId, EmergencyTrigger.TrackingUnsafe,
                        "WhatsApp advertising tracking became Unsafe.");
                }
                else if (snapshots.Count > 0 && snapshots.All(snapshot => snapshot.State == TrackingHealthState.Healthy) && open is not null)
                {
                    open.State = IncidentState.Recovered; open.RecoveredAtUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
            });
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3000)]
    public async Task RunDecisionCycleAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "decision", TimeSpan.FromMinutes(55), async () =>
            {
                if (await db.TrackingIncidents.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId
                    && x.State != IncidentState.Recovered && x.Severity == "Critical")) return;
                var stop = await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null);
                if (stop) return;
                var since = DateTime.UtcNow.AddHours(-24);
                var snapshots = await db.AdvertisingInsights.IgnoreQueryFilters().CountAsync(x =>
                    x.ProjectId == projectId && x.IntervalEndUtc >= since && x.IsCurrent);
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
                    var rows = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId
                        && x.TargetId == ad.Id && x.IntervalEndUtc >= DateTime.UtcNow.AddDays(-7) && x.IsCurrent).ToListAsync();
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
        {
            var timezone = await ProjectTimezoneAsync(projectId);
            if (!AdvertisingSchedulePolicy.IsLocalHour(DateTime.UtcNow, timezone, 4)) continue;
            await WithProjectLease(projectId, "rebalance", TimeSpan.FromHours(20), async () => await allocationPolicy.RebalanceAsync(projectId));
        }
    }

    public async Task ReviewImpactAsync()
    {
        var due = await db.AdvertisingDecisions.IgnoreQueryFilters().Where(x => x.State == DecisionState.Executed && x.EvaluateAfterUtc <= DateTime.UtcNow).Take(200).ToListAsync();
        foreach (var decision in due)
        {
            var now = DateTime.UtcNow;
            var outcomes = await db.AdvertisingConversions.IgnoreQueryFilters().Where(item => item.ProjectId == decision.ProjectId
                && (decision.TargetId == null || item.AdvertisementId == decision.TargetId)
                && item.OccurredAtUtc >= decision.EvidenceEndUtc && item.OccurredAtUtc < now).ToListAsync();
            var baseline = ReadEvidenceMetric(decision.EvidenceJson);
            var evaluationMetric = outcomes.Where(item => item.CurrentValue > 0).Sum(item => item.CurrentValue ?? 0m);
            if (evaluationMetric == 0m) evaluationMetric = outcomes.Count;
            var impact = await impactService.EvaluateAsync(decision.ProjectId, decision.Id,
                new DecisionImpactEvidence(baseline.Metric, evaluationMetric, baseline.Sample, outcomes.Count,
                    decision.EvidenceStartUtc, decision.EvidenceEndUtc, decision.EvidenceEndUtc, now,
                    baseline.Goal, outcomes.Any(item => item.CorrectionState == CorrectionState.PendingBase)), now);
            decision.EvaluateAfterUtc = impact is null || impact.Label == DecisionImpactLabel.Inconclusive
                ? now.AddHours(2) : null;
        }
        await db.SaveChangesAsync();
    }

    private static (decimal Metric, int Sample, string Goal) ReadEvidenceMetric(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("evaluation", out var evaluation))
            {
                var revenue = evaluation.TryGetProperty("Revenue", out var pascalRevenue) ? pascalRevenue.GetDecimal()
                    : evaluation.TryGetProperty("revenue", out var camelRevenue) ? camelRevenue.GetDecimal() : 0m;
                var conversions = evaluation.TryGetProperty("Conversions", out var pascalConversions) ? pascalConversions.GetInt32()
                    : evaluation.TryGetProperty("conversions", out var camelConversions) ? camelConversions.GetInt32() : 0;
                return (revenue > 0 ? revenue : conversions, conversions, revenue > 0 ? "NetPaidValue" : "QualifiedOutcomeCount");
            }
        }
        catch (JsonException) { }
        return (0m, 0, "Unknown");
    }

    public async Task CreateTestsAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
            await WithProjectLease(projectId, "tests", TimeSpan.FromMinutes(5), async () =>
            {
                await whatsAppTests.CreateAsync(projectId);
                var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, CancellationToken.None);
                foreach (var commandId in commandIds)
                    jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
            });
    }

    public async Task AnalyzeStrategyAsync()
    {
        foreach (var projectId in await ActiveProjectIdsAsync())
        {
            var timezone = await ProjectTimezoneAsync(projectId);
            if (!AdvertisingSchedulePolicy.IsLocalWeeklyHour(DateTime.UtcNow, timezone, DayOfWeek.Monday, 6)) continue;
            await WithProjectLease(projectId, "strategy", TimeSpan.FromDays(6), async () =>
            {
                var since = DateTime.UtcNow.AddDays(-7);
                var spend = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x =>
                    x.ProjectId == projectId && x.IntervalEndUtc >= since && x.IsCurrent).SumAsync(x => x.Spend);
                var revenue = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.OccurredAtUtc >= since).SumAsync(x => x.CurrentValue ?? 0m);
                db.AdvertisingDecisions.Add(new AdvertisingDecision { ProjectId = projectId, ActionType = "StrategyReview", TargetType = "Project", EvidenceStartUtc = since, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = JsonSerializer.Serialize(new { spend, revenue, roas = spend > 0 ? revenue / spend : 0 }), ProposedChangeJson = "{}", State = DecisionState.Waiting, RiskClass = "None" });
                await db.SaveChangesAsync();
            });
        }
    }

    private async Task<List<Guid>> ActiveProjectIdsAsync()
    {
        var active = await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(x => x.State == EnvelopeState.Active)
            .Select(x => x.ProjectId).ToListAsync();
        var monitored = await db.AdvertisingDisableRequests.IgnoreQueryFilters().Where(item =>
            item.Mode == AutopilotDisableMode.LeaveRunning && item.CompletedAtUtc == null)
            .Select(item => item.ProjectId).ToListAsync();
        return active.Concat(monitored).Distinct().ToList();
    }

    private async Task<string> ProjectTimezoneAsync(Guid projectId)
    {
        var timezone = await db.ProjectAdvertisingContextProjections.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
            .Select(item => item.ReportingTimezoneIana).SingleOrDefaultAsync()
        ?? await db.AutonomyEnvelopes.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt).Select(item => item.ReportingTimezoneIana).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(timezone))
            throw new AdvertisingException("ADS_REPORTING_TIMEZONE_UNKNOWN", "A validated project reporting timezone is required.", 409);
        _ = AdvertisingSchedulePolicy.DayStartUtc(DateTime.UtcNow, timezone);
        return timezone;
    }

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
            var bucket = AdvertisingSchedulePolicy.BucketStartUtc(now, await ProjectTimezoneAsync(projectId), expiry);
            cycle = await db.AdvertisingCycleRuns.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.JobName == job && x.BucketStartUtc == bucket);
            if (!AdvertisingLeasePolicy.CanTakeCycle(cycle?.State, cycle?.StartedAtUtc, now, expiry)) return;
            if (cycle is null)
            {
                cycle = new AdvertisingCycleRun { ProjectId = projectId, JobName = job, BucketStartUtc = bucket,
                    BucketEndUtc = bucket.Add(expiry), ReportingTimezoneIana = await ProjectTimezoneAsync(projectId),
                    LeaseOwner = owner, StartedAtUtc = now };
                db.AdvertisingCycleRuns.Add(cycle);
            }
            else
            {
                cycle.State = "Running"; cycle.StartedAtUtc = now; cycle.CompletedAtUtc = null;
                cycle.ErrorType = null; cycle.LeaseOwner = owner;
            }
            await db.SaveChangesAsync();
            var currentOwner = await cache.StringGetAsync(key);
            if (!AdvertisingLeasePolicy.CanRelease(currentOwner.HasValue ? currentOwner.ToString() : null, owner))
                throw new InvalidOperationException("Advertising project lease was lost before the cycle could execute.");
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
        var result = await emergencyStopService.ActivateAsync(projectId, trigger, reason);
        foreach (var commandId in result.CommandIds)
            jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
    }
}

public static class AdvertisingSchedulePolicy
{
    public static DateTime DayStartUtc(DateTime utcNow, string timezoneIana)
    {
        var zone = ResolveTimezone(timezoneIana);
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone).Date;
        return TimeZoneInfo.ConvertTimeToUtc(localDate, zone);
    }

    public static DateTime BucketStartUtc(DateTime utcNow, string timezoneIana, TimeSpan cadence)
    {
        var zone = ResolveTimezone(timezoneIana);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var localBucket = new DateTime(local.Ticks / cadence.Ticks * cadence.Ticks, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(localBucket)) localBucket = localBucket.AddHours(1);
        if (zone.IsAmbiguousTime(localBucket))
        {
            var offset = zone.GetAmbiguousTimeOffsets(localBucket).Max();
            return new DateTimeOffset(localBucket, offset).UtcDateTime;
        }
        return TimeZoneInfo.ConvertTimeToUtc(localBucket, zone);
    }

    public static bool IsLocalHour(DateTime utcNow, string timezoneIana, int hour) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            ResolveTimezone(timezoneIana)).Hour == hour;

    public static bool IsLocalWeeklyHour(DateTime utcNow, string timezoneIana, DayOfWeek day, int hour)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            ResolveTimezone(timezoneIana));
        return local.DayOfWeek == day && local.Hour == hour;
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneIana)
    {
        if (string.IsNullOrWhiteSpace(timezoneIana))
            throw new AdvertisingException("ADS_REPORTING_TIMEZONE_UNKNOWN", "A validated reporting timezone is required.", 409);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneIana);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new AdvertisingException("ADS_REPORTING_TIMEZONE_INVALID", "The configured reporting timezone is unavailable.", 409);
        }
        catch (InvalidTimeZoneException)
        {
            throw new AdvertisingException("ADS_REPORTING_TIMEZONE_INVALID", "The configured reporting timezone is invalid.", 409);
        }
    }
}

public static class AdvertisingLeasePolicy
{
    public static bool CanRelease(string? currentOwner, string owner) =>
        string.Equals(currentOwner, owner, StringComparison.Ordinal);

    public static bool CanTakeCycle(string? state, DateTime? startedAtUtc, DateTime utcNow, TimeSpan leaseDuration) =>
        state != "Completed" && (state != "Running" || startedAtUtc is null
            || startedAtUtc <= utcNow.Subtract(leaseDuration));
}
