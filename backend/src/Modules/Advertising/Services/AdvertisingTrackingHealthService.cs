using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record TrackingHealthMetrics(int Conversations, int Observations, int ValidReferrals, int ValidConversations,
    decimal? ExactMatchRate, decimal? ProviderMatchQuality, decimal? DeliveryAcceptanceRate,
    decimal? CorrectionRate, double? DelayP95Minutes, bool LiveReferralProof, DateTime? SourceFreshnessUtc);
public sealed record TrackingHealthDecision(TrackingHealthState State, IReadOnlyList<string> Reasons,
    decimal? ReferralCoverage, decimal? MissingReferralRate);

public sealed class AdvertisingTrackingHealthService(AppDbContext db, IOptions<AdvertisingOptions> options,
    WhatsAppGatewaySessionClient? gateway = null)
{
    public async Task<TrackingHealthSnapshot> EvaluateAsync(Guid projectId, Guid destinationId,
        CancellationToken cancellationToken = default)
    {
        var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == projectId && item.Id == destinationId, cancellationToken);
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == projectId && item.Id == destination.ConnectionId, cancellationToken);
        var policy = await PolicyAsync(projectId, cancellationToken);
        var end = DateTime.UtcNow; var start = end.AddDays(-7);
        var observations = await db.AdvertisingAttributionObservations.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.DestinationId == destinationId && item.MessageOccurredAtUtc >= start && item.MessageOccurredAtUtc < end).ToListAsync(cancellationToken);
        var valid = observations.Where(item => item.IdentifierState == ReferralIdentifierState.CtwaClid).ToArray();
        var managedProviderIds = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(item => item.ProjectId == projectId && item.AdExternalId != null)
            .Select(item => item.AdExternalId!).ToListAsync(cancellationToken);
        var exact = valid.Length == 0 ? null : (decimal?)valid.Count(item => item.ProviderAdExternalId != null && managedProviderIds.Contains(item.ProviderAdExternalId)) / valid.Length;
        var deliveries = await db.AdvertisingConversionDeliveries.IgnoreQueryFilters().Where(item => item.ProjectId == projectId).ToListAsync(cancellationToken);
        var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().Where(item => item.ProjectId == projectId && item.OccurredAtUtc >= start).ToListAsync(cancellationToken);
        var delays = observations.Select(item => Math.Max(0, (item.CreatedAt - item.MessageOccurredAtUtc).TotalMinutes)).Order().ToArray();
        var metrics = new TrackingHealthMetrics(observations.Select(item => item.ConversationId).Distinct().Count(), observations.Count,
            valid.Length, valid.Select(item => item.ConversationId).Distinct().Count(), exact, null,
            deliveries.Count == 0 ? null : (decimal?)deliveries.Count(item => item.State == ConversionDeliveryState.Accepted) / deliveries.Count,
            conversions.Count == 0 ? null : (decimal?)conversions.Count(item => item.CorrectionState != CorrectionState.None) / conversions.Count,
            delays.Length == 0 ? null : delays[(int)Math.Ceiling(delays.Length * .95) - 1],
            destination.WhatsAppIntegrationMode is WhatsAppIntegrationMode.CloudApi or WhatsAppIntegrationMode.CloudApiCoexistence
                && destination.ReferralCaptureState == ReferralProofState.CtwaClidObserved,
            observations.Count == 0 ? null : observations.Max(item => item.CreatedAt));
        var gatewayMode = destination.WhatsAppIntegrationMode == WhatsAppIntegrationMode.BaileysObservedExperimental;
        TrackingHealthDecision decision;
        if (gatewayMode)
        {
            var live = gateway is not null
                ? await gateway.GetAsync(
                    projectId,
                    destination.WhatsAppAccountId ?? projectId,
                    cancellationToken)
                : null;
            var connected = live?.Connected == true
                && string.Equals(WhatsAppGatewaySessionClient.NormalizePhone(live.PhoneNumber), destination.PhoneNumberExternalId, StringComparison.Ordinal);
            var stale = metrics.SourceFreshnessUtc is { } freshness
                && freshness < end.AddMinutes(-options.Value.Tracking.StaleMinutes);
            var reasons = new List<string>();
            if (!connected) reasons.Add("ADS_GATEWAY_NOT_CONNECTED");
            if (stale) reasons.Add("ADS_GATEWAY_LEADS_STALE");
            if (metrics.Conversations < policy.MinimumDenominator) reasons.Add("ADS_GATEWAY_LEARNING_SAMPLE_SMALL");
            decision = new(connected && !stale ? TrackingHealthState.Healthy : TrackingHealthState.Unsafe,
                reasons, null, null);
        }
        else
        {
            decision = Evaluate(policy, metrics);
        }
        var snapshot = new TrackingHealthSnapshot
        {
            ProjectId = projectId, ConnectionId = connection.Id, DestinationId = destinationId,
            TrackingHealthPolicyId = policy.Id, TrackingHealthPolicyVersion = policy.Version,
            WindowStartUtc = start, WindowEndUtc = end, InboundConversationCount = metrics.Conversations,
            ReferralObservationCount = metrics.Observations, ValidReferralCount = metrics.ValidReferrals,
            ReferralCoverage = decision.ReferralCoverage, MissingReferralRate = decision.MissingReferralRate,
            ExactMatchRate = metrics.ExactMatchRate, ProviderMatchQuality = metrics.ProviderMatchQuality,
            DeliveryAcceptanceRate = metrics.DeliveryAcceptanceRate, CorrectionRate = metrics.CorrectionRate,
            EventDelayMinutesP95 = metrics.DelayP95Minutes, SourceFreshnessUtc = metrics.SourceFreshnessUtc,
            State = decision.State, ReasonCodesJson = JsonSerializer.Serialize(decision.Reasons),
            EvidenceJson = JsonSerializer.Serialize(new { mode = gatewayMode ? "GatewayAggregate" : "CtwaClidExact", metrics }), EvaluatedAtUtc = end
        };
        db.AdvertisingTrackingHealthSnapshots.Add(snapshot); await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public static TrackingHealthDecision Evaluate(TrackingHealthPolicy policy, TrackingHealthMetrics metrics)
    {
        var coverage = metrics.Conversations == 0 ? null : (decimal?)metrics.ValidConversations / metrics.Conversations;
        var missing = coverage is null ? null : 1m - coverage;
        var reasons = new List<string>();
        if (!metrics.LiveReferralProof) reasons.Add("ADS_TRACKING_LIVE_REFERRAL_PROOF_MISSING");
        if (metrics.Conversations < policy.MinimumDenominator) reasons.Add("ADS_TRACKING_SAMPLE_TOO_SMALL");
        if (coverage < policy.MinimumReferralCoverage) reasons.Add("ADS_TRACKING_REFERRAL_COVERAGE_LOW");
        if (metrics.ExactMatchRate is { } exact && exact < policy.MinimumExactMatchRate) reasons.Add("ADS_TRACKING_EXACT_MATCH_LOW");
        if (metrics.DeliveryAcceptanceRate is not null && metrics.ProviderMatchQuality is null)
            reasons.Add("ADS_TRACKING_PROVIDER_MATCH_UNKNOWN");
        if (metrics.DeliveryAcceptanceRate is { } acceptance && acceptance < policy.MinimumDeliveryAcceptanceRate) reasons.Add("ADS_TRACKING_DELIVERY_ACCEPTANCE_LOW");
        if (metrics.CorrectionRate is { } correction && correction > policy.MaximumCorrectionRate) reasons.Add("ADS_TRACKING_CORRECTION_RATE_HIGH");
        if (metrics.DelayP95Minutes is { } delay && delay > policy.MaximumEventDelayMinutes) reasons.Add("ADS_TRACKING_DELAY_HIGH");
        var unsafeState = !metrics.LiveReferralProof || metrics.Conversations >= policy.MinimumDenominator && reasons.Any(reason => reason != "ADS_TRACKING_SAMPLE_TOO_SMALL");
        var state = reasons.Count == 0 ? TrackingHealthState.Healthy : unsafeState ? TrackingHealthState.Unsafe : TrackingHealthState.Degraded;
        return new(state, reasons, coverage, missing);
    }

    private async Task<TrackingHealthPolicy> PolicyAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var version = options.Value.Tracking.PolicyVersion;
        var existing = await db.AdvertisingTrackingPolicies.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Goal == "WhatsAppBusinessOutcomes" && item.Version == version, cancellationToken);
        if (existing is not null) return existing;
        var settings = options.Value.Tracking;
        var policy = new TrackingHealthPolicy
        {
            ProjectId = projectId, Goal = "WhatsAppBusinessOutcomes", Version = version,
            MinimumDenominator = settings.MinimumConversationDenominator,
            MinimumReferralCoverage = settings.MinimumReferralCoverage,
            MinimumExactMatchRate = settings.MinimumExactMatchRate,
            MinimumDeliveryAcceptanceRate = settings.MinimumDeliveryAcceptanceRate,
            MaximumCorrectionRate = settings.MaximumCorrectionRate, MaximumEventDelayMinutes = settings.StaleMinutes,
            EffectiveFromUtc = DateTime.UtcNow
        };
        policy.DefinitionHash = AdvertisingAuditService.HashState(JsonSerializer.Serialize(policy));
        db.AdvertisingTrackingPolicies.Add(policy); await db.SaveChangesAsync(cancellationToken); return policy;
    }
}
