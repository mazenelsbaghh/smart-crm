using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record SafetyRequest(Guid ProjectId, string ActionType, decimal? CurrentBudget, decimal? ProposedBudget,
    string PublisherPlatform, IReadOnlyCollection<string> Positions, Guid? TargetId = null,
    Guid? DestinationId = null, string? ExpectedStateHash = null);
public sealed record SafetyResult(DecisionVerdict Verdict, string Code, string Message)
{
    public static SafetyResult Approve() => new(DecisionVerdict.Approve, "ADS_SAFE", "Action is inside the active authorization envelope.");
    public static SafetyResult Reject(string code, string message) => new(DecisionVerdict.Reject, code, message);
    public static SafetyResult Wait(string code, string message) => new(DecisionVerdict.Wait, code, message);
}

public sealed class AdvertisingSafetyEngine(AppDbContext db)
{
    public async Task<SafetyResult> EvaluateAsync(SafetyRequest request, CancellationToken cancellationToken = default)
    {
        if (!AdvertisingDecisionPolicy.IsSupported(request.ActionType)) return SafetyResult.Reject("ADS_UNSUPPORTED_ACTION", "Unsupported financial action fails closed.");
        var protectivePause = AdvertisingDecisionPolicy.IsProtectivePause(request.ActionType);
        ManagedAdvertisement? target = null;
        if (request.TargetId is { } targetId)
        {
            target = await db.ManagedAdvertisements.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.ProjectId == request.ProjectId && item.Id == targetId, cancellationToken);
            if (target?.OwnershipRecordId is null) return SafetyResult.Reject("ADS_TARGET_NOT_MANAGED", "The target is not owned by Autopilot.");
            var owned = await db.AdvertisingManagedOwnership.IgnoreQueryFilters().AnyAsync(item => item.ProjectId == request.ProjectId
                && item.Id == target.OwnershipRecordId && item.RevokedAtUtc == null
                && item.OwnershipKind != ManagedOwnershipKind.ManualUnowned, cancellationToken);
            if (!owned) return SafetyResult.Reject("ADS_TARGET_NOT_MANAGED", "The target is outside managed ownership.");
            if (!protectivePause && !string.Equals(target.DestinationType, "WHATSAPP", StringComparison.OrdinalIgnoreCase))
                return SafetyResult.Reject("ADS_DESTINATION_NOT_WHATSAPP", "Every managed advertisement must open WhatsApp.");
            if (!protectivePause && !string.IsNullOrWhiteSpace(request.ExpectedStateHash)
                && !string.Equals(request.ExpectedStateHash, target.ProviderStateHash ?? target.EffectiveStateHash, StringComparison.Ordinal))
                return SafetyResult.Wait("ADS_EXPECTED_STATE_STALE", "Provider state changed after the decision was created.");
        }
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId, cancellationToken);
        // Losing activation eligibility must not prevent stopping owned delivery with retained credentials.
        if (protectivePause)
            return connection is not null && connection.State != AdvertisingConnectionState.Revoked
                ? SafetyResult.Approve()
                : SafetyResult.Reject("ADS_CONNECTION_NOT_READY", "Facebook authorization is unavailable.");
        var automaticPlacements = request.Positions.Count == 0 && request.PublisherPlatform is "AdvantagePlus" or "Automatic" or "Meta";
        if (!automaticPlacements && !FacebookPlacementPolicy.IsAllowed(request.PublisherPlatform, request.Positions))
            return SafetyResult.Reject("ADS_FORBIDDEN_PLACEMENT", "Placement configuration is not an authorized Meta delivery mode.");
        var destinationId = request.DestinationId ?? target?.DestinationId;
        AuthorizedWhatsAppDestination? destination = null;
        if (destinationId is { } selectedDestination)
        {
            destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == request.ProjectId && item.Id == selectedDestination, cancellationToken);
            if (destination?.State != AuthorizedDestinationState.Eligible)
                return SafetyResult.Reject("ADS_DESTINATION_NOT_READY", "The authorized WhatsApp destination is unavailable.");
        }
        if (connection?.State != AdvertisingConnectionState.Ready) return SafetyResult.Reject("ADS_CONNECTION_NOT_READY", "Facebook authorization is unavailable.");
        if (!string.IsNullOrWhiteSpace(connection.AccountStatus)
            && connection.AccountStatus is not ("ACTIVE" or "1"))
            return SafetyResult.Reject("ADS_ACCOUNT_UNHEALTHY", "The Meta ad account is not active.");
        if (!string.IsNullOrWhiteSpace(connection.FundingStatus)
            && connection.FundingStatus is not ("ACTIVE" or "VALID" or "FUNDED"))
            return SafetyResult.Reject("ADS_FUNDING_UNHEALTHY", "The Meta funding source is not healthy.");
        var capability = await db.AdvertisingCapabilitySnapshots.IgnoreQueryFilters().Where(item =>
            item.ProjectId == request.ProjectId && (destinationId == null || item.DestinationId == destinationId))
            .OrderByDescending(item => item.CheckedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (capability?.State != AdvertisingCapabilityState.Healthy || capability.ExpiresAtUtc <= DateTime.UtcNow)
            return SafetyResult.Wait("ADS_CAPABILITY_STALE", "A current healthy Meta capability proof is required.");
        if (await db.AdvertisingEmergencyStops.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == request.ProjectId && x.ResumedAtUtc == null, cancellationToken))
            return SafetyResult.Reject("ADS_EMERGENCY_STOP_ACTIVE", "Emergency Stop is active.");
        var hasTrackingIncident = await db.TrackingIncidents.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == request.ProjectId
            && x.Category == "ConversionTracking" && x.State != IncidentState.Recovered, cancellationToken);
        var latestTracking = await db.AdvertisingTrackingHealthSnapshots.IgnoreQueryFilters()
            .Where(item => item.ProjectId == request.ProjectId && (destinationId == null || item.DestinationId == destinationId))
            .OrderByDescending(item => item.EvaluatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var trackingHealthy = destination?.WhatsAppIntegrationMode != WhatsAppIntegrationMode.BaileysObservedExperimental
            && AdvertisingOperationalPolicy.HasFreshHealthyTracking(latestTracking, hasTrackingIncident, DateTime.UtcNow, TimeSpan.FromMinutes(30));
        if (!trackingHealthy)
            return SafetyResult.Wait("ADS_TRACKING_UNHEALTHY", "A fresh healthy tracking snapshot is required.");
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.State == EnvelopeState.Active, cancellationToken);
        if (envelope is null || envelope.StartsAtUtc > DateTime.UtcNow || envelope.EndsAtUtc < DateTime.UtcNow) return SafetyResult.Reject("ADS_ENVELOPE_INACTIVE", "No active financial authorization exists.");
        if (request.ProposedBudget is < 0) return SafetyResult.Reject("ADS_INVALID_BUDGET", "Budget cannot be negative.");
        if (request.ProposedBudget > envelope.DailyCap) return SafetyResult.Reject("ADS_CAP_EXCEEDED", "Proposed budget exceeds the project daily cap.");
        var currentLedgers = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(item => item.ProjectId == request.ProjectId
            && item.EnvelopeId == envelope.Id && item.PeriodStartUtc <= DateTime.UtcNow && item.PeriodEndUtc > DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        if (currentLedgers.Count == 0 || currentLedgers.Any(item => item.LastReconciledAtUtc == null || item.LastReconciledAtUtc < DateTime.UtcNow.AddMinutes(-15)))
            return SafetyResult.Wait("ADS_SPEND_STALE", "Fresh reconciled spend is required before financial mutation.");
        if (currentLedgers.Any(ledger => AdvertisingSpendGuard.ObservedExposure(ledger) >= ledger.UsableCap))
            return SafetyResult.Reject("ADS_HARD_CAP_RISK", "Observed or delayed spend has exhausted an authorized period.");
        if (request.ActionType is "ResumeAd" && currentLedgers.Any(ledger => AdvertisingSpendGuard.RemainingAuthority(ledger) <= 0))
        {
            var alreadyReserved = target is not null && await db.AdvertisingBudgetAllocations.IgnoreQueryFilters().AnyAsync(allocation =>
                allocation.ProjectId == request.ProjectId && allocation.TargetId == target.Id && allocation.State == "Active"
                && allocation.StartsAtUtc <= DateTime.UtcNow && allocation.EndsAtUtc > DateTime.UtcNow, cancellationToken);
            if (!alreadyReserved) return SafetyResult.Reject("ADS_HARD_CAP_RISK", "No remaining spend authority is available for reactivation.");
        }
        if (request.ActionType.Equals("IncreaseBudget", StringComparison.OrdinalIgnoreCase) && request.CurrentBudget is > 0 && request.ProposedBudget is not null)
        {
            var max = request.CurrentBudget.Value * (1m + envelope.MaximumIncreasePercent / 100m);
            if (request.ProposedBudget > max) return SafetyResult.Reject("ADS_INCREASE_EXCEEDED", "Increase is outside the authorized step limit.");
            var authority = AdvertisingSpendGuard.CanApply(currentLedgers, request.ProposedBudget.Value - request.CurrentBudget.Value);
            if (!authority.Allowed) return SafetyResult.Reject(authority.Code, "The increase would exceed remaining spend authority.");
        }
        return SafetyResult.Approve();
    }
}
