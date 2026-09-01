using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager")]
public sealed class AdvertisingPlanningController(IProjectAuthorizationService authorization, AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault,
    WhatsAppCreativeTestService whatsAppTests,
    AdvertisingEvidenceService evidence, AdvertisingReadinessService readinessService, CampaignPlanCompiler planCompiler,
    CampaignProvisioningService provisioning, MetaCreativeSourceClient creativeSources, AdvertisingCloneService cloneService,
    IOptions<AdvertisingOptions> advertisingOptions) : AdvertisingControllerBase(authorization)
{
    [HttpGet("facebook/page-posts")]
    public async Task<IActionResult> PagePosts(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var connection = await db.AdvertisingConnections.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        if (connection?.PageExternalId is null) return Conflict(new { code = "ADS_CONNECTION_NOT_READY" });
        var token = connection.ProtectedAccessToken is null ? "mock" : vault.Unprotect(connection.ProtectedAccessToken);
        return Ok(await meta.GetPageContentAsync(token, connection.PageExternalId, cancellationToken));
    }

    [HttpPost("creatives/import-posts")]
    public async Task<IActionResult> ImportPosts(Guid projectId, [FromBody] ImportPostsRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (request.Posts.Count is < 1 or > 12) return UnprocessableEntity(new { code = "ADS_CREATIVE_COUNT_INVALID" });
        var created = new List<Guid>();
        foreach (var post in request.Posts)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId}:{post.Id}"))).ToLowerInvariant();
            var existing = await db.AdvertisingCreatives.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceHash == hash, cancellationToken);
            if (existing is not null) { created.Add(existing.Id); continue; }
            var mediaType = post.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase) ? CreativeMediaType.Video : CreativeMediaType.Image;
            var rank = CreativeRankingService.Rank(mediaType, post.CreatedAtUtc, DateTime.UtcNow);
            var creative = new AdvertisingCreative
            {
                ProjectId = projectId, SourceType = CreativeSourceType.ExistingPagePost, SourceExternalId = post.Id, SourceHash = hash,
                MediaType = mediaType,
                RightsState = "PageOwned", PolicyState = "PendingMetaReview", EligibilityState = CreativeEligibility.Eligible,
                RecommendationScore = rank.Score, RecommendationEvidenceJson = JsonSerializer.Serialize(new { source = "FacebookPagePost", rank.Explanation, explanation = "صلة العرض والسياسة تحتاجان مراجعة العرض الموثق قبل الإطلاق" }), LastAnalyzedAtUtc = DateTime.UtcNow
            };
            db.AdvertisingCreatives.Add(creative); created.Add(creative.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { creativeIds = created });
    }

    [HttpPost("whatsapp-tests/start")]
    public async Task<IActionResult> StartWhatsAppTest(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        return Accepted(await whatsAppTests.CreateAsync(projectId, cancellationToken));
    }

    [HttpPost("plans/{planId:guid}/provision")]
    public async Task<IActionResult> Provision(Guid projectId, Guid planId, [FromBody] ProvisionPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId) && !IsAutopilot(projectId)) return Forbid();
        var actor = UserId ?? Guid.Empty;
        var result = await provisioning.ProvisionPausedAsync(projectId, planId, request.CreativeId,
            request.CreativeVariantId, actor, RequireIdempotencyKey(), cancellationToken);
        return AcceptedOperation(projectId, result.RootOperationId, result.RootOperationId, result.State);
    }

    [HttpGet("plans/{planId:guid}/activation-readiness")]
    public async Task<IActionResult> ActivationReadiness(Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var plan = await db.AdvertisingCampaignPlans.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == planId, cancellationToken);
        if (plan is null) return NotFound();
        var campaigns = await db.AdvertisingManagedCampaigns.AsNoTracking().Where(x => x.ProjectId == projectId && x.PlanId == planId).ToListAsync(cancellationToken);
        var adSets = await db.AdvertisingManagedAdSets.AsNoTracking().Where(x => x.ProjectId == projectId && x.PlanId == planId).ToListAsync(cancellationToken);
        var creatives = await db.AdvertisingManagedProviderCreatives.AsNoTracking().Where(x => x.ProjectId == projectId && x.PlanId == planId).ToListAsync(cancellationToken);
        var ads = await db.ManagedAdvertisements.AsNoTracking().Where(x => x.ProjectId == projectId && x.PlanId == planId).ToListAsync(cancellationToken);
        var ready = campaigns.Count > 0 && adSets.Count > 0 && creatives.Count > 0 && ads.Count > 0
            && campaigns.All(x => x.ReconciliationState == ProviderReconciliationState.VerifiedPaused)
            && adSets.All(x => x.ReconciliationState == ProviderReconciliationState.VerifiedPaused)
            && creatives.All(x => x.VerificationState == ProviderCreativeVerificationState.Verified)
            && ads.All(x => x.ReconciliationState == ProviderReconciliationState.VerifiedPaused);
        return Ok(new { ready, state = ready ? "VERIFIED_PAUSED" : "WAIT",
            activationEnabled = ready && advertisingOptions.Value.Enabled && advertisingOptions.Value.AllowRealActivation });
    }

    [HttpGet("creative-sources")]
    public async Task<IActionResult> CreativeSources(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await creativeSources.DiscoverAsync(projectId, cancellationToken));
    }

    [HttpPost("plans/{planId:guid}/clone")]
    public async Task<IActionResult> Clone(Guid projectId, Guid planId, [FromBody] ClonePlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var clone = await cloneService.CloneAsync(projectId, planId, request.Variable,
            request.ReplacementCreativeId, request.AudienceSuggestionsJson, cancellationToken);
        return Ok(new { clone.Id, clone.Version, clone.State });
    }

    [HttpGet("operations/{operationId:guid}")]
    public async Task<IActionResult> Operation(Guid projectId, Guid operationId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var operation = await db.AdvertisingProviderOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == operationId, cancellationToken);
        return operation is null ? NotFound() : Ok(new { operation.Id, operation.OperationType, operation.TargetType,
            state = operation.State.ToString(), operation.AttemptCount, operation.ErrorCode, operation.ErrorSummary,
            operation.ProviderTraceId, operation.CreatedAt, operation.CompletedAtUtc });
    }
    [HttpGet("offers")]
    public async Task<IActionResult> Offers(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingOffers.AsNoTracking().Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id, x.Name, x.Type, x.Price, x.Currency, x.State, x.DestinationsJson, x.MarketsJson }).ToListAsync(cancellationToken));
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var profile = await db.AdvertisingProfiles.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (profile is null) return Ok(new { state = "WAIT", blockingReasons = new[] { "ADS_PUBLISHED_KNOWLEDGE_REQUIRED" } });
        var facts = await db.AdvertisingFactSources.AsNoTracking().Where(x => x.ProjectId == projectId && x.ProfileId == profile.Id)
            .Select(x => new { x.FactName, x.FactValue, x.Confidence, x.Citation, x.KnowledgeVersion, x.IsContradictory, x.IsRequiredForLaunch })
            .ToListAsync(cancellationToken);
        return Ok(new { profile.Id, state = profile.Status, profile.KnowledgeRevisionHash, profile.GeneratedAtUtc, profile.StaleAtUtc, facts });
    }

    [HttpGet("strategy")]
    public async Task<IActionResult> Strategy(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var readiness = await readinessService.GetAsync(projectId, cancellationToken);
        var offers = await db.AdvertisingOffers.AsNoTracking().Where(x => x.ProjectId == projectId && x.State == "Eligible").ToListAsync(cancellationToken);
        var grants = await db.AdvertisingOfferDestinationGrants.AsNoTracking().Where(x => x.ProjectId == projectId && x.State == "Active").ToListAsync(cancellationToken);
        var candidates = offers.SelectMany(offer => grants.Where(grant => grant.OfferId == offer.Id)
            .Select(grant => new OfferStrategyCandidate(offer.Id, grant.DestinationId, 0.9m, offer.ContributionMargin, offer.CurrentCapacity, true, true)))
            .DistinctBy(candidate => (candidate.OfferId, candidate.DestinationId));
        var ranked = AdvertisingStrategyService.Rank(candidates);
        var offerLookup = offers.ToDictionary(offer => offer.Id);
        var latestPlan = await db.AdvertisingCampaignPlans.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id, x.OfferId, x.Name, x.BusinessGoal, x.Objective, x.OptimizationGoal, x.BidStrategy,
                x.BudgetMode, x.DailyBudget, x.Currency, placementMode = x.PlacementMode.ToString(),
                x.StartsAtUtc, x.EndsAtUtc, x.SpecialAdCategory, x.State
            }).FirstOrDefaultAsync(cancellationToken);
        var providerSteps = latestPlan is null
            ? []
            : await db.AdvertisingProviderOperations.AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.PlanId == latestPlan.Id)
                .OrderBy(x => x.CreatedAt)
                .Select(x => new { x.OperationType, state = x.State.ToString(), x.ErrorCode, x.ErrorSummary, x.ProviderTargetId })
                .ToArrayAsync(cancellationToken);
        var blockers = readiness.Items.Where(item => !item.Ready).Select(item => item.Reason ?? item.Key).Distinct().ToArray();
        return Ok(new
        {
            state = readiness.Ready && ranked.Count > 0 ? "READY" : "WAIT",
            blockingReasons = blockers,
            rankedOffers = ranked.Select(item =>
            {
                var offer = offerLookup[item.OfferId];
                return new
                {
                    item.OfferId, item.DestinationId, offer.Name, offer.Type,
                    primaryOutcome = string.IsNullOrWhiteSpace(offer.PrimaryOutcome) ? (offer.Type == "Course" ? "EnrollmentPaid" : "QualifiedLead") : offer.PrimaryOutcome,
                    offer.Price, offer.Currency, offer.ContributionMargin, offer.MaximumSustainableCost,
                    offer.CurrentCapacity, attributionWindowDays = offer.AttributionWindowDays <= 0 ? 7 : offer.AttributionWindowDays, item.Score, item.Reasons
                };
            }),
            plan = latestPlan,
            providerSteps
        });
    }

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingCampaignPlans.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.OfferId, x.DestinationId, x.Version, x.Name,
                x.BusinessGoal, x.Objective, x.OptimizationGoal, x.BidStrategy, placementMode = x.PlacementMode.ToString(),
                x.DailyBudget, x.Currency, x.PlanHash, x.ReadinessJson, x.State }).ToListAsync(cancellationToken));
    }

    [HttpPost("plans/compile")]
    public async Task<IActionResult> CompilePlan(Guid projectId, [FromBody] CompilePlanRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var result = await planCompiler.CompileAsync(projectId, request.OfferId, cancellationToken);
        return result.Ready
            ? Ok(new { result.Plan!.Id, result.Plan.Version, result.Plan.PlanHash, result.Plan.State })
            : UnprocessableEntity(new { state = "WAIT", blockingReasons = result.BlockingReasons });
    }

    [HttpGet("creatives")]
    public async Task<IActionResult> Creatives(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingCreatives.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.RecommendationScore)
            .Select(x => new { x.Id, sourceType = x.SourceType.ToString(), mediaType = x.MediaType.ToString(), eligibility = x.EligibilityState.ToString(), x.RecommendationScore, x.RecommendationEvidenceJson, x.FatigueState }).Take(100).ToListAsync(cancellationToken));
    }

    [HttpGet("creative-comparison")]
    public async Task<IActionResult> CreativeComparison(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var since = DateTime.UtcNow.AddDays(-7);
        var ads = await (from ad in db.ManagedAdvertisements.AsNoTracking()
                         join creative in db.AdvertisingCreatives.AsNoTracking() on ad.CreativeId equals creative.Id
                         where ad.ProjectId == projectId && ad.AdExternalId != null
                         orderby ad.CreatedAt descending
                         select new { Ad = ad, creative.MediaType, creative.SourceExternalId, creative.RecommendationEvidenceJson }).Take(30).ToListAsync(cancellationToken);
        var adIds = ads.Select(row => row.Ad.Id).ToList();
        var snapshots = await db.AdvertisingInsights.AsNoTracking().Where(snapshot => snapshot.ProjectId == projectId
            && adIds.Contains(snapshot.TargetId) && snapshot.IntervalEndUtc >= since).ToListAsync(cancellationToken);
        var conversions = await db.AdvertisingConversions.AsNoTracking().Where(conversion => conversion.ProjectId == projectId
            && conversion.AdvertisementId != null && adIds.Contains(conversion.AdvertisementId.Value) && conversion.OccurredAtUtc >= since).ToListAsync(cancellationToken);
        return Ok(ads.Select(row =>
        {
            var result = evidence.Evaluate(snapshots.Where(snapshot => snapshot.TargetId == row.Ad.Id),
                conversions.Where(conversion => conversion.AdvertisementId == row.Ad.Id), Math.Max(25m, row.Ad.DailyBudget));
            return new { row.Ad.Id, row.Ad.Name, mediaType = row.MediaType.ToString(), status = row.Ad.EffectiveStatus,
                row.SourceExternalId, row.RecommendationEvidenceJson, row.Ad.AdExternalId, row.Ad.CampaignExternalId, row.Ad.DailyBudget,
                spend = result.Spend, impressions = snapshots.Where(snapshot => snapshot.TargetId == row.Ad.Id).Sum(snapshot => snapshot.Impressions),
                clicks = snapshots.Where(snapshot => snapshot.TargetId == row.Ad.Id).Sum(snapshot => snapshot.Clicks),
                results = result.Conversions, cpa = result.Cpa, verdict = result.Verdict.ToString() };
        }));
    }

    [HttpGet("decisions")]
    public async Task<IActionResult> Decisions(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var decisions = await db.AdvertisingDecisions.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return Ok(decisions.Select(decision => new { decision.Id, decision.ActionType, decision.TargetType, decision.RiskClass, state = decision.State.ToString(),
            decision.EvidenceStartUtc, decision.EvidenceEndUtc, decision.EvaluateAfterUtc, decision.CreatedAt, reason = DecisionReason(decision.EvidenceJson) }));
    }

    private static string? DecisionReason(string evidenceJson)
    {
        using var document = JsonDocument.Parse(evidenceJson);
        return document.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
    }
}

public sealed record ImportPagePost(string Id, string MediaType, DateTime? CreatedAtUtc);
public sealed record ImportPostsRequest(IReadOnlyList<ImportPagePost> Posts);
public sealed record ProvisionPlanRequest(Guid CreativeId, Guid? CreativeVariantId);
public sealed record ClonePlanRequest(AdvertisingCloneVariable Variable, Guid? ReplacementCreativeId, string? AudienceSuggestionsJson);
public sealed record CompilePlanRequest(Guid OfferId);
