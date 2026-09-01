using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record CampaignPlanCompilation(bool Ready, CampaignPlan? Plan, IReadOnlyList<string> BlockingReasons);

public sealed class CampaignPlanCompiler(AppDbContext db, AdvertisingAuditService audit)
{
    public async Task<CampaignPlanCompilation> CompileAsync(Guid projectId, Guid offerId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        async Task<CampaignPlanCompilation> Block(string reason)
        {
            await audit.RecordPlanningDecisionAsync(projectId, offerId, "CampaignPlanWait",
                [reason], new { reason }, cancellationToken);
            return new(false, null, [reason]);
        }
        var offer = await db.AdvertisingOffers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offerId, cancellationToken);
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OfferId == offerId && x.State == EnvelopeState.Active, cancellationToken);
        if (offer is null || offer.State != "Eligible") return await Block("ADS_OFFER_NOT_ELIGIBLE");
        if (envelope is null) return await Block("ADS_ACTIVE_ENVELOPE_REQUIRED");
        var grant = await db.AdvertisingOfferDestinationGrants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.EnvelopeId == envelope.Id && x.OfferId == offerId && x.State == "Active", cancellationToken);
        if (grant is null || grant.AllowedFromUtc > now || grant.AllowedUntilUtc <= now) return await Block("ADS_OFFER_DESTINATION_GRANT_REQUIRED");
        var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == grant.DestinationId && x.State == AuthorizedDestinationState.Eligible, cancellationToken);
        if (destination?.CapabilitySnapshotId is not { } snapshotId) return await Block("ADS_WHATSAPP_CAPABILITY_MISSING");
        var capability = await db.AdvertisingCapabilitySnapshots.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == snapshotId, cancellationToken);
        if (capability is null) return await Block("ADS_WHATSAPP_CAPABILITY_MISSING");
        var capabilityDecision = AdvertisingCapabilityPolicy.CanProvisionWhatsApp(capability, now);
        if (!capabilityDecision.Ready) return await Block(capabilityDecision.Code);
        var profile = await db.AdvertisingProfiles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == offer.ProfileId && x.StaleAtUtc == null, cancellationToken);
        if (profile is null || profile.Status != "Ready") return await Block("ADS_PROFILE_STALE_OR_BLOCKED");
        var sources = await db.AdvertisingFactSources.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.ProfileId == profile.Id).ToListAsync(cancellationToken);
        var facts = sources.Select(source => new ExtractedAdvertisingFact(source.FactName, source.FactValue,
            source.KnowledgeDocumentId, source.KnowledgeVersion, source.Confidence, source.ObservedAtUtc,
            source.IsContradictory, source.IsRequiredForLaunch, source.Citation)).ToArray();
        var factValidation = AdvertisingFactValidator.Validate(facts);
        if (!factValidation.Eligible)
        {
            await audit.RecordPlanningDecisionAsync(projectId, offerId, "CampaignPlanWait",
                factValidation.BlockingReasons, new { sourceVersions = sources.Select(x => new { x.KnowledgeDocumentId, x.KnowledgeVersion }) }, cancellationToken);
            return new(false, null, factValidation.BlockingReasons);
        }

        var funnel = AdvertisingFunnelService.Infer(offer.Type, offer.PrimaryOutcome);
        var availableGoals = capability.OptimizationGoalsJson;
        var optimization = new[] { funnel.PrimaryOptimization }.Concat(funnel.FallbackOptimizations)
            .FirstOrDefault(goal => availableGoals.Contains(goal, StringComparison.OrdinalIgnoreCase));
        if (optimization is null) return await Block("ADS_MESSAGING_OPTIMIZATION_UNSUPPORTED");
        var objective = optimization == "MESSAGING_PURCHASE_CONVERSION" ? "OUTCOME_SALES" : "OUTCOME_ENGAGEMENT";
        var policy = MetaPolicyClassificationService.Classify(offer.Name, offer.AllowedClaimsJson);
        if (!policy.Resolved) return await Block("ADS_SPECIAL_CATEGORY_UNRESOLVED");

        var audience = new AudienceStrategy
        {
            ProjectId = projectId, OfferId = offerId, EnvelopeId = envelope.Id,
            IncludedGeoJson = envelope.HardIncludedGeoJson, ExcludedGeoJson = envelope.HardExcludedGeoJson,
            MinimumAge = envelope.HardMinimumAge, RequiredLanguagesJson = envelope.HardRequiredLanguagesJson,
            CustomAudienceExclusionsJson = envelope.HardCustomAudienceExclusionsJson,
            AudienceSuggestionsJson = "{\"mode\":\"AdvantagePlusBroad\"}",
            SpecialCategoryConstraintsJson = JsonSerializer.Serialize(policy),
            EvidenceJson = JsonSerializer.Serialize(new { envelope.DefinitionHash, profile.KnowledgeRevisionHash }),
            DefinitionHash = AdvertisingAuditService.HashState($"{envelope.DefinitionHash}:{policy.SpecialAdCategory}"), State = "Eligible"
        };
        db.AdvertisingAudienceStrategies.Add(audience);
        var version = (await db.AdvertisingCampaignPlans.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.OfferId == offerId)
            .Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0) + 1;
        var planSnapshot = new
        {
            offerId, destinationId = destination.Id, envelopeId = envelope.Id, envelope.Version,
            capabilitySnapshotId = capability.Id, objective, optimization,
            fallback = funnel.FallbackOptimizations, bidStrategy = "LOWEST_COST_WITHOUT_CAP",
            placementMode = "AdvantagePlusAutomatic", audience.DefinitionHash, policy.SpecialAdCategory,
            evidenceWindow = new { from = profile.GeneratedAtUtc, to = now }
        };
        var planJson = JsonSerializer.Serialize(planSnapshot);
        var plan = new CampaignPlan
        {
            ProjectId = projectId, ConnectionId = envelope.ConnectionId, EnvelopeId = envelope.Id, EnvelopeVersion = envelope.Version,
            OfferId = offerId, DestinationId = destination.Id, CapabilitySnapshotId = capability.Id, Version = version,
            Name = $"{offer.Name} · WhatsApp · v{version}", BusinessGoal = offer.PrimaryOutcome, Objective = objective,
            OptimizationGoal = optimization, OptimizationFallbackOrderJson = JsonSerializer.Serialize(funnel.FallbackOptimizations),
            BidStrategy = "LOWEST_COST_WITHOUT_CAP", BudgetMode = "Campaign", DailyBudget = envelope.DailyCap,
            Currency = envelope.Currency, StartsAtUtc = envelope.StartsAtUtc > now ? envelope.StartsAtUtc : now,
            EndsAtUtc = envelope.EndsAtUtc, SpecialAdCategory = policy.SpecialAdCategory, PlacementMode = PlacementPolicy.DynamicEligibleMeta,
            AudienceStrategyId = audience.Id, PlanJson = planJson, PlanHash = AdvertisingAuditService.HashState(planJson),
            ReadinessJson = "{\"state\":\"ReadyForPausedProvisioning\"}", State = "Ready", CreatedBy = "AI"
        };
        db.AdvertisingCampaignPlans.Add(plan);
        audit.Append(new(projectId, "Planning", "CampaignPlanCompiled", nameof(CampaignPlan), plan.Id.ToString(),
            "SystemAutopilot", null, JsonSerializer.Serialize(new { plan.PlanHash, plan.Version, plan.Objective,
                plan.OptimizationGoal, envelopeVersion = envelope.Version, capabilitySnapshotId = capability.Id,
                sourceVersions = sources.Select(x => new { x.KnowledgeDocumentId, x.KnowledgeVersion }) }), plan.Id));
        await db.SaveChangesAsync(cancellationToken);
        return new(true, plan, []);
    }

}
