using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Hangfire;
using Modules.Advertising.Workers;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager")]
public sealed class AdvertisingOperationsController(
    IProjectAuthorizationService authorization,
    AppDbContext db,
    AdvertisingReadinessService readiness,
    BudgetAllocator allocator,
    AdvertisingDecisionService decisions,
    IBackgroundJobClient backgroundJobs) : AdvertisingControllerBase(authorization)
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var spend = await db.AdvertisingInsights.Where(x => x.ProjectId == projectId).SumAsync(x => x.Spend, cancellationToken);
        var conversions = await db.AdvertisingConversions.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        var ads = await db.ManagedAdvertisements.Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        var envelope = await db.AutonomyEnvelopes.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        var projectAi = await db.ProjectSettings.AsNoTracking().Where(settings => settings.ProjectId == projectId)
            .Select(settings => new { settings.GeminiModel, HasProjectApiKey = settings.GeminiApiKey != null && settings.GeminiApiKey != "" }).SingleOrDefaultAsync(cancellationToken);
        var stop = await db.AdvertisingEmergencyStops.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null, cancellationToken);
        var totalRevenue = conversions.Where(x => x.CurrentValue > 0).Sum(x => x.CurrentValue ?? 0);
        return Ok(new
        {
            asOfUtc = DateTime.UtcNow,
            spend,
            revenue = totalRevenue,
            roas = spend > 0 ? decimal.Round(totalRevenue / spend, 2) : 0,
            leads = conversions.Count(x => x.EventType is "Lead" or "QualifiedLead"),
            purchases = conversions.Count(x => x.EventType is "Purchase" or "SubscriptionStarted" or "EnrollmentPaid"),
            activeAds = ads.Count(x => x.ConfiguredStatus == ManagedDeliveryState.Active),
            totalAds = ads.Count,
            autopilot = envelope?.State == EnvelopeState.Active && !stop,
            emergencyStop = stop,
            dailyCap = envelope?.DailyCap ?? 0,
            usableCap = envelope is null ? 0 : allocator.Allocate(envelope.DailyCap, envelope.SafetyReservePercent, 1, true).Usable,
            aiModel = projectAi?.GeminiModel ?? "الإعداد الافتراضي للنظام",
            usesProjectApiKey = projectAi?.HasProjectApiKey ?? false,
            readiness = await readiness.GetAsync(projectId, cancellationToken)
        });
    }

    [HttpPost("autopilot/disable")]
    public async Task<IActionResult> Disable(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var envelopes = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Suspended;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { state = "Disabled", managedAds = "LastSafeState" });
    }

    [HttpPost("autopilot/enable")]
    public async Task<IActionResult> Enable(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var currentReadiness = await readiness.GetAsync(projectId, cancellationToken);
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
        if (!await db.AdvertisingEmergencyStops.AnyAsync(x => x.ProjectId == projectId && x.ResumedAtUtc == null, cancellationToken))
            db.AdvertisingEmergencyStops.Add(new EmergencyStopRecord { ProjectId = projectId, Trigger = EmergencyTrigger.Manual, ActivatedByUserId = UserId, ActivatedAtUtc = DateTime.UtcNow, Reason = string.IsNullOrWhiteSpace(request.Reason) ? "Manual emergency stop" : request.Reason.Trim() });
        AdvertisingAudit.Add(db, projectId, "EmergencyStopActivated", "Project", projectId, new { trigger = "Manual", reasonProvided = !string.IsNullOrWhiteSpace(request.Reason) }, UserId);
        var envelopes = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var envelope in envelopes) envelope.State = EnvelopeState.Suspended;
        var ads = await db.ManagedAdvertisements.Where(x => x.ProjectId == projectId && x.ConfiguredStatus == ManagedDeliveryState.Active).ToListAsync(cancellationToken);
        var stopDecision = new AdvertisingDecision { ProjectId = projectId, ActionType = "PauseAd", TargetType = "EmergencySet", EvidenceStartUtc = DateTime.UtcNow, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = "{\"trigger\":\"EmergencyStop\"}", ProposedChangeJson = "{\"status\":\"PAUSED\"}", RiskClass = "Protective", State = DecisionState.Approved };
        db.AdvertisingDecisions.Add(stopDecision);
        var pauseCommands = new List<Guid>();
        foreach (var ad in ads)
        {
            ad.ConfiguredStatus = ManagedDeliveryState.Paused;
            if (ad.AdExternalId is null) continue;
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = stopDecision.Id, IdempotencyKey = $"emergency:{stopDecision.Id:N}:{ad.Id:N}", CommandType = "SetAdPaused", TargetExternalId = ad.AdExternalId, DesiredStateJson = System.Text.Json.JsonSerializer.Serialize(new { adId = ad.Id, status = "PAUSED" }), RequestFingerprint = $"{ad.AdExternalId}:PAUSED" };
            db.AdvertisingExecutionCommands.Add(command); pauseCommands.Add(command.Id);
        }
        var commands = await db.AdvertisingExecutionCommands.Where(x => x.ProjectId == projectId && x.State == CommandState.Pending).ToListAsync(cancellationToken);
        foreach (var command in commands) command.State = CommandState.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        foreach (var commandId in pauseCommands)
            backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
        return Ok(new { state = "EmergencyStopped", pausedAds = ads.Count, queuedProviderPauses = pauseCommands.Count });
    }

    [HttpPost("emergency-stop/resume")]
    public async Task<IActionResult> Resume(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var connectionReady = await db.AdvertisingConnections.AnyAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken);
        var openTracking = await db.TrackingIncidents.AnyAsync(x => x.ProjectId == projectId && x.State != IncidentState.Recovered, cancellationToken);
        if (!connectionReady || openTracking) return Conflict(new { code = "ADS_RECOVERY_NOT_READY" });
        var records = await db.AdvertisingEmergencyStops.Where(x => x.ProjectId == projectId && x.ResumedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var record in records) { record.ResumedAtUtc = DateTime.UtcNow; record.ResumedByUserId = UserId; }
        AdvertisingAudit.Add(db, projectId, "EmergencyStopResumed", "Project", projectId, new { trackingHealthy = true, connectionReady = true }, UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { state = "ReadyToEnable" });
    }

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.ManagedAdvertisements.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).Take(100)
            .Select(x => new { x.Id, x.Name, media = x.CreativeId, status = x.ConfiguredStatus.ToString(), x.EffectiveStatus, x.DailyBudget,
                x.PublisherPlatform, x.ManagementSource, x.PositionsJson, x.LastSyncedAtUtc, x.ImportedAtUtc }).ToListAsync(cancellationToken));
    }
}

public sealed record StopRequest(string Reason);
