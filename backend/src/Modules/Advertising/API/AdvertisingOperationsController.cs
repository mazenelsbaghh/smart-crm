using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Hangfire;
using Modules.Advertising.Jobs;
using Modules.Advertising.Workers;
using Microsoft.Extensions.Options;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager")]
public sealed class AdvertisingOperationsController(
    IProjectAuthorizationService authorization,
    AppDbContext db,
    AdvertisingReadinessService readiness,
    BudgetAllocator allocator,
    AdvertisingReportingWindowService reportingWindows,
    AdvertisingExperimentService experiments,
    AdvertisingEvidenceService evidence,
    AdvertisingDecisionService decisions,
    AdvertisingEmergencyStopService emergencyStops,
    AdvertisingDisableService disableService,
    IBackgroundJobClient backgroundJobs,
    IOptions<AdvertisingOptions> advertisingOptions) : AdvertisingControllerBase(authorization)
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var projectContext = await db.ProjectAdvertisingContextProjections.AsNoTracking()
            .SingleOrDefaultAsync(contextProjection => contextProjection.ProjectId == projectId, cancellationToken);
        var reportingTimezone = projectContext?.ReportingTimezoneIana;
        if (!TryResolveTimezone(reportingTimezone, out _))
            return Conflict(new { code = "ADS_REPORTING_TIMEZONE_UNKNOWN", message = "A validated reporting timezone is required." });
        var connection = await db.AdvertisingConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        var envelopeQuery = db.AutonomyEnvelopes.AsNoTracking().Where(candidate => candidate.ProjectId == projectId);
        var envelopeCandidates = await envelopeQuery.OrderByDescending(candidate => candidate.CreatedAt).Take(1)
            .Concat(envelopeQuery.Where(candidate => candidate.State == EnvelopeState.Active)
                .OrderByDescending(candidate => candidate.CreatedAt).Take(1))
            .ToListAsync(cancellationToken);
        var envelope = envelopeCandidates.OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();
        var activeEnvelope = envelopeCandidates.Where(candidate => candidate.State == EnvelopeState.Active)
            .OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();
        var currency = !string.IsNullOrWhiteSpace(connection?.AccountCurrency)
            ? connection.AccountCurrency
            : !string.IsNullOrWhiteSpace(envelope?.Currency) ? envelope.Currency : null;
        if (currency is null)
            return Conflict(new { code = "ADS_REPORTING_CURRENCY_UNKNOWN", message = "A provider-verified account currency is required." });
        var asOfUtc = DateTime.UtcNow;
        var todayStartUtc = AdvertisingSchedulePolicy.DayStartUtc(asOfUtc, reportingTimezone!);
        var reportingWindow = new AdvertisingOverviewWindow(todayStartUtc, asOfUtc);
        var insightMetrics = await AdvertisingOverviewQuery.InsightsAsync(db, projectId, reportingWindow, cancellationToken);
        var conversionMetrics = await AdvertisingOverviewQuery.ConversionsAsync(db, projectId, reportingWindow, cancellationToken);
        var advertisementMetrics = await AdvertisingOverviewQuery.AdvertisementsAsync(db, projectId, cancellationToken);
        var stop = await db.AdvertisingEmergencyStops.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null, cancellationToken);
        var latestDisable = await db.AdvertisingDisableRequests.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.RequestedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var disableMonitoringActive = latestDisable is not null && activeEnvelope is null;
        var managedDeliveryMayContinue = false;
        if (stop || disableMonitoringActive)
        {
            var managedOwnershipIds = db.AdvertisingManagedOwnership.AsNoTracking().Where(ownership =>
                ownership.ProjectId == projectId && ownership.RevokedAtUtc == null
                && (ownership.OwnershipKind == ManagedOwnershipKind.AutopilotCreated
                    || ownership.OwnershipKind == ManagedOwnershipKind.ImportedWithAuthority))
                .Select(ownership => ownership.Id);
            managedDeliveryMayContinue = await db.ManagedAdvertisements.AsNoTracking().AnyAsync(advertisement =>
                advertisement.ProjectId == projectId
                && advertisement.OwnershipRecordId != null
                && managedOwnershipIds.Contains(advertisement.OwnershipRecordId.Value)
                && (advertisement.ConfiguredStatus == ManagedDeliveryState.Active
                    || advertisement.EffectiveStatus == "ACTIVE")
                && advertisement.EffectiveStatus != "PAUSED"
                && advertisement.EffectiveStatus != "CAMPAIGN_PAUSED"
                && advertisement.EffectiveStatus != "ADSET_PAUSED"
                && advertisement.EffectiveStatus != "ARCHIVED"
                && advertisement.EffectiveStatus != "DELETED"
                && advertisement.EffectiveStatus != "DISAPPROVED", cancellationToken);
        }
        var disableCommands = latestDisable is null
            ? null
            : await AdvertisingOverviewQuery.DisableCommandsAsync(db, projectId, latestDisable.Id, cancellationToken);
        var liveDisable = AdvertisingOverviewQuery.DisableStatus(
            latestDisable, disableCommands, disableMonitoringActive && managedDeliveryMayContinue);
        var disableDeliveryMayContinue = disableMonitoringActive && managedDeliveryMayContinue
            && (latestDisable?.Mode == AutopilotDisableMode.LeaveRunning || liveDisable.PauseOngoing);
        var deliveryMayContinue = (stop && managedDeliveryMayContinue) || disableDeliveryMayContinue;
        var latestDecision = await db.AdvertisingDecisions.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var cycles = await db.AdvertisingCycleRuns.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.StartedAtUtc).Take(100).ToListAsync(cancellationToken);
        var latestCycles = cycles.GroupBy(x => x.JobName).Select(group => group.First()).OrderBy(x => x.JobName).ToList();
        var openIncidentQuery = db.TrackingIncidents.AsNoTracking()
            .Where(incident => incident.ProjectId == projectId && incident.State != IncidentState.Recovered);
        var incidentEvidence = await openIncidentQuery.OrderByDescending(incident => incident.DetectedAtUtc).Take(5)
            .Concat(openIncidentQuery.Where(incident => incident.Category == "ConversionTracking")
                .OrderByDescending(incident => incident.DetectedAtUtc).Take(1))
            .ToListAsync(cancellationToken);
        var openIncidents = incidentEvidence.DistinctBy(incident => incident.Id)
            .OrderByDescending(incident => incident.DetectedAtUtc).Take(5).ToList();
        var hasTrackingIncident = incidentEvidence.Any(incident => incident.Category == "ConversionTracking");
        var latestTracking = await db.AdvertisingTrackingHealthSnapshots.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.EvaluatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var currentReadiness = await readiness.GetForOverviewAsync(projectId,
            new(connection, activeEnvelope, latestTracking, hasTrackingIncident), cancellationToken);
        var currentCampaign = advertisementMetrics.CurrentCampaign;
        return Ok(new
        {
            asOfUtc,
            windowStartUtc = todayStartUtc,
            windowEndUtc = asOfUtc,
            spend = insightMetrics.Spend,
            revenue = conversionMetrics.Revenue,
            roas = insightMetrics.Spend > 0 ? decimal.Round(conversionMetrics.Revenue / insightMetrics.Spend, 2) : 0,
            leads = conversionMetrics.Leads,
            qualifiedLeads = conversionMetrics.QualifiedLeads,
            bookings = conversionMetrics.Bookings,
            purchases = conversionMetrics.Purchases,
            activeAds = advertisementMetrics.ActiveAds,
            totalAds = advertisementMetrics.TotalAds,
            autopilot = envelope?.State == EnvelopeState.Active && !stop,
            emergencyStop = stop,
            continuingSpend = deliveryMayContinue,
            pauseOngoing = liveDisable.PauseOngoing,
            deliveryMayContinue,
            disableState = liveDisable.State,
            dailyCap = envelope?.DailyCap ?? 0,
            usableCap = envelope is null ? 0 : allocator.Allocate(envelope.DailyCap, envelope.SafetyReservePercent, 1, true).Usable,
            aiModel = string.IsNullOrWhiteSpace(projectContext.AllowedAiModel) ? "الإعداد الافتراضي للنظام" : projectContext.AllowedAiModel,
            usesProjectApiKey = projectContext.AiConfigurationVersion > 0,
            reportingTimezone,
            currency,
            attributionWindow = "7d click-to-WhatsApp",
            truthSource = "Canonical business outcomes + Meta delivery",
            readiness = currentReadiness,
            operations = new
            {
                connection = connection is null ? null : new
                {
                    state = connection.State.ToString(),
                    connection.LastValidatedAtUtc,
                    connection.LastSyncAtUtc,
                    connection.LastErrorCode,
                    connection.LastErrorSummary,
                    connection.ExpiresAtUtc,
                    connected = connection.State == AdvertisingConnectionState.Ready
                },
                campaign = currentCampaign is null ? null : new
                {
                    currentCampaign.Name,
                    currentCampaign.DailyBudget,
                    currentCampaign.EffectiveStatus,
                    currentCampaign.LastSyncedAtUtc,
                    currentCampaign.ImportedAtUtc,
                    currentCampaign.ManagementSource
                },
                performance = new
                {
                    daysLoaded = insightMetrics.DaysLoaded,
                    snapshots = insightMetrics.Snapshots,
                    lastPulledAtUtc = insightMetrics.LastPulledAtUtc,
                    impressions = insightMetrics.Impressions,
                    clicks = insightMetrics.Clicks,
                    allTimeSpend = insightMetrics.AllTimeSpend
                },
                ai = new
                {
                    model = string.IsNullOrWhiteSpace(projectContext.AllowedAiModel) ? "الإعداد الافتراضي للنظام" : projectContext.AllowedAiModel,
                    usesProjectApiKey = projectContext.AiConfigurationVersion > 0,
                    latestDecision = latestDecision is null ? null : new { latestDecision.ActionType, state = latestDecision.State.ToString(), latestDecision.CreatedAt }
                },
                tracking = new
                {
                    healthy = AdvertisingOperationalPolicy.HasFreshHealthyTracking(latestTracking,
                        openIncidents.Count > 0, asOfUtc, TimeSpan.FromMinutes(30)),
                    state = latestTracking?.State.ToString() ?? "Unknown",
                    evaluatedAtUtc = latestTracking?.EvaluatedAtUtc,
                    mode = connection?.DatasetExternalId is null ? "UNSAFE_NO_DATASET" : "DATASET_AND_CRM",
                    openIncidents = openIncidents.Select(x => new { x.Category, x.Severity, x.Summary, x.DetectedAtUtc })
                },
                jobs = latestCycles.Select(x => new { x.JobName, x.State, x.StartedAtUtc, x.CompletedAtUtc, x.ErrorType }),
                lastFailure = cycles.FirstOrDefault(x => x.State == "Failed") is { } failedCycle
                    ? new { failedCycle.JobName, failedCycle.ErrorType, failedCycle.StartedAtUtc }
                    : null
            }
        });
    }

    private static bool TryResolveTimezone(string? timezoneId, out TimeZoneInfo? timezone)
    {
        timezone = null;
        if (string.IsNullOrWhiteSpace(timezoneId)) return false;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    [HttpPost("sync-now")]
    public IActionResult SyncNow(Guid projectId)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        backgroundJobs.Enqueue<AdvertisingRecurringJobs>(job => job.SynchronizeAsync());
        backgroundJobs.Enqueue<AdvertisingRecurringJobs>(job => job.PullInsightsAsync());
        return Accepted(new { queued = true, message = "تمت جدولة مزامنة الحملة وسحب الأداء الآن." });
    }

    [HttpPost("autopilot/disable")]
    public async Task<IActionResult> Disable(Guid projectId, [FromBody] DisableAutopilotRequest? request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var mode = Enum.TryParse<AutopilotDisableMode>(request?.Mode, true, out var parsed)
            ? parsed : AutopilotDisableMode.PauseManaged;
        var result = await disableService.DisableAsync(projectId, UserId ?? Guid.Empty, mode,
            request?.Reason ?? "Operator requested normal stop", request?.AcknowledgeContinuingSpend == true,
            cancellationToken);
        foreach (var commandId in result.CommandIds)
            backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
        return Accepted(new
        {
            operationId = result.RequestId,
            mode = result.Mode.ToString(),
            result.ContinuingSpend,
            result.PauseOngoing,
            result.DeliveryMayContinue,
            queuedPauseCommands = result.CommandIds.Count
        });
    }

    [HttpPost("autopilot/enable")]
    public async Task<IActionResult> Enable(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (!advertisingOptions.Value.Enabled || !advertisingOptions.Value.AllowRealActivation)
            return Conflict(new { code = "ADS_REAL_ACTIVATION_DISABLED" });
        var currentReadiness = await readiness.RefreshAsync(projectId, cancellationToken);
        if (!currentReadiness.Ready) return Conflict(new { code = "ADS_NOT_READY", items = currentReadiness.Items });
        if (await db.AdvertisingEmergencyStops.AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null, cancellationToken))
            return Conflict(new { code = "ADS_EMERGENCY_STOP_ACTIVE" });
        var envelope = await db.AutonomyEnvelopes.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Suspended, cancellationToken);
        if (envelope is null) return Conflict(new { code = "ADS_ENVELOPE_REQUIRED" });
        envelope.State = EnvelopeState.Active;
        await db.SaveChangesAsync(cancellationToken);
        var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, cancellationToken);
        foreach (var commandId in commandIds)
            backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
        return Ok(new { state = "Enabled", queuedCommands = commandIds.Count });
    }

    [HttpPost("emergency-stop")]
    public async Task<IActionResult> EmergencyStop(Guid projectId, [FromBody] StopRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var result = await emergencyStops.ActivateAsync(projectId, EmergencyTrigger.Manual,
            string.IsNullOrWhiteSpace(request.Reason) ? "Manual emergency stop" : request.Reason.Trim(), UserId, cancellationToken);
        foreach (var commandId in result.CommandIds)
            backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
        return Accepted(new
        {
            operationId = result.StopId,
            result.AlreadyActive,
            queuedProviderPauses = result.CommandIds.Count
        });
    }

    [HttpPost("emergency-stop/resume")]
    public async Task<IActionResult> Resume(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        await emergencyStops.ResumeAsync(projectId, UserId ?? Guid.Empty, cancellationToken);
        return Ok(new { state = "ReadyToEnable" });
    }

    [HttpGet("stop-state")]
    public async Task<IActionResult> StopState(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var stop = await emergencyStops.StateAsync(projectId, cancellationToken);
        var disableId = await db.AdvertisingDisableRequests.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.RequestedAtUtc).Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var disable = disableId is null ? null : await disableService.StateAsync(projectId, disableId.Value, cancellationToken);
        return Ok(new { emergencyStop = stop, disable });
    }

    [HttpGet("incidents")]
    public async Task<IActionResult> Incidents(Guid projectId, [FromQuery] string? state,
        CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var query = db.TrackingIncidents.AsNoTracking().Where(item => item.ProjectId == projectId);
        if (Enum.TryParse<IncidentState>(state, true, out var parsed)) query = query.Where(item => item.State == parsed);
        return Ok(await query.OrderByDescending(item => item.DetectedAtUtc).Take(200)
            .Select(item => new
            {
                item.Id,
                item.Category,
                item.Severity,
                item.Summary,
                item.EvidenceJson,
                state = item.State.ToString(),
                item.DetectedAtUtc,
                item.RecoveredAtUtc
            }).ToListAsync(cancellationToken));
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.ManagedAdvertisements.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new
            {
                x.Id,
                x.Name,
                media = x.CreativeId,
                status = x.ConfiguredStatus.ToString(),
                x.EffectiveStatus,
                x.DailyBudget,
                x.PublisherPlatform,
                x.ManagementSource,
                x.PositionsJson,
                x.CampaignExternalId,
                x.AdSetExternalId,
                x.AdExternalId,
                x.ProviderStateHash,
                x.LastSyncedAtUtc,
                x.ImportedAtUtc
            }).ToListAsync(cancellationToken));
    }

    [HttpGet("audiences")]
    public async Task<IActionResult> Audiences(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingAudienceStrategies.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id,
                x.OfferId,
                x.Version,
                x.IncludedGeoJson,
                x.ExcludedGeoJson,
                x.MinimumAge,
                x.RequiredLanguagesJson,
                x.CustomAudienceExclusionsJson,
                x.MaximumAgeSuggestion,
                x.AudienceSuggestionsJson,
                x.SpecialCategoryConstraintsJson,
                x.EstimatedReachJson,
                x.EvidenceJson,
                x.State
            }).ToListAsync(cancellationToken));
    }

    [HttpPost("experiments")]
    public async Task<IActionResult> CreateExperiment(Guid projectId, [FromBody] ExperimentDefinition request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId) && !IsAutopilot(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var experiment = await experiments.CreateAsync(projectId, request, cancellationToken);
        return Ok(new { experiment.Id, experiment.DefinitionHash, experiment.State });
    }

    [HttpGet("experiments")]
    public async Task<IActionResult> Experiments(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var items = await db.AdvertisingExperiments.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var ids = items.Select(item => item.Id).ToArray();
        var arms = await db.AdvertisingExperimentArms.AsNoTracking().Where(x => x.ProjectId == projectId && ids.Contains(x.ExperimentId)).ToListAsync(cancellationToken);
        return Ok(items.Select(item => new
        {
            item.Id,
            item.Name,
            item.Hypothesis,
            item.PrimaryVariable,
            item.BusinessOutcome,
            item.AttributionWindowDays,
            item.MinimumElapsedHours,
            item.MinimumSpend,
            item.MinimumAttributedOutcomes,
            item.MinimumAttributionCoverage,
            item.CorrectionLagHours,
            item.ConfidencePolicyJson,
            item.BudgetCap,
            item.StopRuleJson,
            item.State,
            item.StartedAtUtc,
            item.MaturedAtUtc,
            item.StoppedAtUtc,
            item.ConclusionJson,
            arms = arms.Where(arm => arm.ExperimentId == item.Id).Select(arm => new
            {
                arm.Id,
                arm.Name,
                arm.IsControl,
                arm.ChangedValueJson,
                arm.AllocatedBudget,
                arm.State,
                arm.EvidenceJson
            })
        }));
    }

    [HttpGet("budget/ledgers")]
    public async Task<IActionResult> Ledgers(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingBudgetLedgers.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.PeriodStartUtc).Select(x => new
            {
                x.Id,
                x.EnvelopeId,
                x.PeriodKind,
                x.PeriodStartUtc,
                x.PeriodEndUtc,
                x.AuthorizedCap,
                x.SafetyReserve,
                x.UsableCap,
                x.CommittedAmount,
                x.ObservedSpend,
                x.ReleasedAmount,
                x.DelayedSpendEstimate,
                x.ForecastSpend,
                remainingAuthority = Math.Max(0m, x.UsableCap - (x.ObservedSpend + Math.Max(0m, x.CommittedAmount - x.ReleasedAmount - x.ObservedSpend) + Math.Max(x.DelayedSpendEstimate, Math.Max(0m, x.ForecastSpend - x.ObservedSpend))))
            }).ToListAsync(cancellationToken));
    }

    [HttpGet("budget/allocations")]
    public async Task<IActionResult> Allocations(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingBudgetAllocations.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id,
                x.TargetType,
                x.TargetId,
                purpose = x.Purpose.ToString(),
                x.AllocatedAmount,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.State
            }).ToListAsync(cancellationToken));
    }

    [HttpGet("performance")]
    public async Task<IActionResult> Performance(Guid projectId, [FromQuery] DateTime startUtc, [FromQuery] DateTime endUtc,
        [FromQuery] Guid? targetId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var window = await reportingWindows.BuildAsync(projectId, startUtc, endUtc, targetId, cancellationToken);
        var evaluated = evidence.Evaluate(window.Insights, window.Outcomes, 50m, window.AsOfUtc);
        return Ok(new
        {
            window.StartUtc,
            window.EndUtc,
            window.AsOfUtc,
            currentInsightRevisions = window.Insights.Count,
            outcomes = window.Outcomes.Count,
            evaluated.Spend,
            evaluated.Revenue,
            evaluated.Conversions,
            evaluated.Roas,
            evaluated.Cpa,
            evaluated.Ctr,
            evaluated.Frequency,
            verdict = evaluated.Verdict.ToString(),
            evaluated.OutcomeLevel,
            evaluated.WaitReasons
        });
    }

    [HttpGet("decisions/{decisionId:guid}")]
    public async Task<IActionResult> Decision(Guid projectId, Guid decisionId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var decision = await db.AdvertisingDecisions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.Id == decisionId, cancellationToken);
        if (decision is null) return NotFound();
        var reviews = await db.AdvertisingDecisionReviews.AsNoTracking().Where(item =>
            item.ProjectId == projectId && item.DecisionId == decisionId).OrderBy(item => item.CreatedAt).ToListAsync(cancellationToken);
        var commands = await db.AdvertisingExecutionCommands.AsNoTracking().Where(item =>
            item.ProjectId == projectId && item.DecisionId == decisionId).OrderBy(item => item.CreatedAt).ToListAsync(cancellationToken);
        var impacts = await db.AdvertisingDecisionImpacts.AsNoTracking().Where(item =>
            item.ProjectId == projectId && item.DecisionId == decisionId).OrderBy(item => item.EvaluatedAtUtc).ToListAsync(cancellationToken);
        return Ok(new
        {
            decision.Id,
            decision.ActionType,
            decision.TargetType,
            decision.TargetId,
            state = decision.State.ToString(),
            decision.RiskClass,
            decision.EvidenceStartUtc,
            decision.EvidenceEndUtc,
            decision.EvidenceJson,
            decision.EvidenceHash,
            decision.ReasonCodesJson,
            decision.ProposedChangeJson,
            decision.EvaluateAfterUtc,
            reviews = reviews.Select(item => new
            {
                item.ReviewerType,
                verdict = item.Verdict.ToString(),
                item.ReasonsJson,
                item.EvidenceHash,
                item.ModelVersion,
                item.PromptVersion,
                item.ReviewedAtUtc
            }),
            commands = commands.Select(item => new
            {
                item.Id,
                item.CommandType,
                state = item.State.ToString(),
                item.AttemptCount,
                item.LastError,
                item.ClaimedAtUtc,
                item.SentAtUtc,
                item.CompletedAtUtc,
                item.ReconciledAtUtc,
                item.ReconciliationEvidenceJson
            }),
            impacts = impacts.Select(item => new
            {
                item.Id,
                label = item.Label.ToString(),
                item.Goal,
                item.EvaluatedAtUtc,
                item.RollbackCommandId
            })
        });
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(Guid projectId, [FromQuery] string? category,
        [FromQuery] long? afterTicks, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!CanRead(projectId)) return Forbid();
        limit = Math.Clamp(limit, 1, 200);
        var after = afterTicks is > 0 ? new DateTime(afterTicks.Value, DateTimeKind.Utc) : DateTime.MinValue;
        var query = db.AdvertisingAuditRecords.AsNoTracking().Where(item => item.ProjectId == projectId && item.OccurredAtUtc > after);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(item => item.Category == category);
        var items = await query.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id).Take(limit).ToListAsync(cancellationToken);
        return Ok(new
        {
            items = items.Select(item => new
            {
                item.Id,
                item.Category,
                item.Action,
                item.EntityType,
                item.EntityId,
                item.ActorType,
                item.SafeEvidenceJson,
                item.CorrelationId,
                item.OccurredAtUtc
            }),
            nextCursor = items.LastOrDefault()?.OccurredAtUtc.Ticks
        });
    }

    [HttpGet("changes")]
    public async Task<IActionResult> Changes(Guid projectId, [FromQuery] long? after,
        [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!CanRead(projectId)) return Forbid();
        limit = Math.Clamp(limit, 1, 200);
        var afterUtc = after is > 0 ? new DateTime(after.Value, DateTimeKind.Utc) : DateTime.MinValue;
        var items = await db.AdvertisingAuditRecords.AsNoTracking().Where(item => item.ProjectId == projectId
            && item.OccurredAtUtc > afterUtc).OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id)
            .Take(limit).Select(item => new
            {
                item.Id,
                type = item.Category,
                item.Action,
                item.EntityType,
                item.EntityId,
                atUtc = item.OccurredAtUtc
            }).ToListAsync(cancellationToken);
        return Ok(new { items, nextCursor = items.LastOrDefault()?.atUtc.Ticks });
    }
}

public sealed record StopRequest(string Reason);
public sealed record DisableAutopilotRequest(string? Mode, string? Reason, bool AcknowledgeContinuingSpend = false);
