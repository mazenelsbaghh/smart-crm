using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record SafetyRequest(Guid ProjectId, string ActionType, decimal? CurrentBudget, decimal? ProposedBudget, string PublisherPlatform, IReadOnlyCollection<string> Positions);
public sealed record SafetyResult(DecisionVerdict Verdict, string Code, string Message)
{
    public static SafetyResult Approve() => new(DecisionVerdict.Approve, "ADS_SAFE", "Action is inside the active authorization envelope.");
    public static SafetyResult Reject(string code, string message) => new(DecisionVerdict.Reject, code, message);
    public static SafetyResult Wait(string code, string message) => new(DecisionVerdict.Wait, code, message);
}

public sealed class AdvertisingSafetyEngine(AppDbContext db)
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "IncreaseBudget", "DecreaseBudget", "PauseAd", "ResumeAd", "CreateCampaign", "CreateTest", "ReplaceCreative", "Rebalance"
    };

    public async Task<SafetyResult> EvaluateAsync(SafetyRequest request, CancellationToken cancellationToken = default)
    {
        if (!SupportedActions.Contains(request.ActionType)) return SafetyResult.Reject("ADS_UNSUPPORTED_ACTION", "Unsupported financial action fails closed.");
        if (!FacebookPlacementPolicy.IsAllowed(request.PublisherPlatform, request.Positions)) return SafetyResult.Reject("ADS_FORBIDDEN_PLACEMENT", "Only allowlisted Facebook placements are permitted.");
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId, cancellationToken);
        if (connection?.State != AdvertisingConnectionState.Ready) return SafetyResult.Reject("ADS_CONNECTION_NOT_READY", "Facebook authorization is unavailable.");
        if (request.ActionType.Equals("PauseAd", StringComparison.OrdinalIgnoreCase)) return SafetyResult.Approve();
        if (await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == request.ProjectId && x.ResumedAtUtc == null, cancellationToken))
            return SafetyResult.Reject("ADS_EMERGENCY_STOP_ACTIVE", "Emergency Stop is active.");
        if (await db.TrackingIncidents.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == request.ProjectId && x.State != IncidentState.Recovered, cancellationToken))
            return SafetyResult.Wait("ADS_TRACKING_UNHEALTHY", "Financial changes wait until tracking recovers.");
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.State == EnvelopeState.Active, cancellationToken);
        if (envelope is null || envelope.StartsAtUtc > DateTime.UtcNow || envelope.EndsAtUtc < DateTime.UtcNow) return SafetyResult.Reject("ADS_ENVELOPE_INACTIVE", "No active financial authorization exists.");
        if (request.ProposedBudget is < 0) return SafetyResult.Reject("ADS_INVALID_BUDGET", "Budget cannot be negative.");
        if (request.ProposedBudget > envelope.DailyCap) return SafetyResult.Reject("ADS_CAP_EXCEEDED", "Proposed budget exceeds the project daily cap.");
        if (request.ActionType.Equals("IncreaseBudget", StringComparison.OrdinalIgnoreCase) && request.CurrentBudget is > 0 && request.ProposedBudget is not null)
        {
            var max = request.CurrentBudget.Value * (1m + envelope.MaximumIncreasePercent / 100m);
            if (request.ProposedBudget > max) return SafetyResult.Reject("ADS_INCREASE_EXCEEDED", "Increase is outside the authorized step limit.");
        }
        return SafetyResult.Approve();
    }
}
