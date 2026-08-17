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
using Hangfire;
using Modules.Advertising.Workers;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager")]
public sealed class AdvertisingPlanningController(IProjectAuthorizationService authorization, AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault,
    BudgetAllocator allocator, AdvertisingDecisionService decisions, IBackgroundJobClient backgroundJobs, WhatsAppCreativeTestService whatsAppTests,
    AdvertisingEvidenceService evidence) : AdvertisingControllerBase(authorization)
{
    private static readonly HashSet<string> AllowedObjectives = new(StringComparer.Ordinal) { "OUTCOME_SALES", "OUTCOME_LEADS", "OUTCOME_TRAFFIC", "OUTCOME_ENGAGEMENT" };
    private static readonly HashSet<string> AllowedOptimizationGoals = new(StringComparer.Ordinal) { "OFFSITE_CONVERSIONS", "LEAD_GENERATION", "LINK_CLICKS", "LANDING_PAGE_VIEWS", "REACH", "IMPRESSIONS" };
    [HttpGet("facebook/page-posts")]
    public async Task<IActionResult> PagePosts(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var connection = await db.AdvertisingConnections.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        if (connection?.PageExternalId is null) return Conflict(new { code = "ADS_CONNECTION_NOT_READY" });
        var token = connection.ProtectedAccessToken is null ? "mock" : vault.Unprotect(connection.ProtectedAccessToken);
        return Ok(await meta.GetPagePostsAsync(token, connection.PageExternalId, cancellationToken));
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

    [HttpPost("launch-plans/activate")]
    public async Task<IActionResult> Activate(Guid projectId, [FromBody] ActivateAdsRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (!AllowedObjectives.Contains(request.Objective) || !AllowedOptimizationGoals.Contains(request.OptimizationEvent))
            return UnprocessableEntity(new { code = "ADS_OPTIMIZATION_NOT_ALLOWED" });
        var envelope = await db.AutonomyEnvelopes.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Active, cancellationToken);
        var connection = await db.AdvertisingConnections.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken);
        var offer = await db.AdvertisingOffers.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == request.OfferId && x.State == "Eligible", cancellationToken);
        if (envelope is null || connection is null || offer is null) return Conflict(new { code = "ADS_NOT_READY" });
        var creatives = await db.AdvertisingCreatives.Where(x => x.ProjectId == projectId && request.CreativeIds.Contains(x.Id) && x.EligibilityState == CreativeEligibility.Eligible).OrderByDescending(x => x.RecommendationScore).ToListAsync(cancellationToken);
        if (creatives.Count == 0) return UnprocessableEntity(new { code = "ADS_NO_ELIGIBLE_CREATIVES" });
        if (creatives.Any(x => x.SourceType != CreativeSourceType.ExistingPagePost || x.SourceExternalId == null))
            return UnprocessableEntity(new { code = "ADS_SOURCE_NOT_PROVIDER_READY" });
        var maxTests = Math.Max(1, Math.Min(creatives.Count, (int)Math.Floor(envelope.DailyCap / 50m)));
        creatives = creatives.Take(maxTests).ToList();
        var allocation = allocator.Allocate(envelope.DailyCap, envelope.SafetyReservePercent, creatives.Count, false);
        var promotion = new AdvertisingPromotion { ProjectId = projectId, EnvelopeId = envelope.Id, OfferId = offer.Id, Name = request.Name.Trim(), Objective = request.Objective, DestinationUrl = request.DestinationUrl.Trim(), OptimizationEvent = request.OptimizationEvent, State = PromotionState.Canary, ActivatedAtUtc = DateTime.UtcNow };
        db.AdvertisingPromotions.Add(promotion);
        var perAd = decimal.Round(allocation.Usable / creatives.Count, 2, MidpointRounding.ToZero);
        foreach (var creative in creatives)
            db.ManagedAdvertisements.Add(new ManagedAdvertisement { ProjectId = projectId, PromotionId = promotion.Id, CreativeId = creative.Id, Name = $"{request.Name} · {creative.MediaType}", PublisherPlatform = "facebook", PositionsJson = creative.MediaType == CreativeMediaType.Video ? "[\"feed\",\"facebook_reels\"]" : "[\"feed\",\"story\"]", DailyBudget = perAd, ConfiguredStatus = ManagedDeliveryState.Paused, EffectiveStatus = "LOCAL_PAUSED" });
        await db.SaveChangesAsync(cancellationToken);
        var managedAds = await db.ManagedAdvertisements.Where(x => x.ProjectId == projectId && x.PromotionId == promotion.Id).ToListAsync(cancellationToken);
        var reservationIds = new List<Guid>();
        foreach (var managedAd in managedAds)
        {
            var reservation = await allocator.ReserveAsync(db, projectId, envelope.Id, managedAd.Id, BudgetPurpose.CreativeTest, managedAd.DailyBudget, cancellationToken: cancellationToken);
            if (!reservation.Reserved)
            {
                foreach (var reservationId in reservationIds)
                    await allocator.ReleaseAsync(db, projectId, reservationId, cancellationToken);
                promotion.State = PromotionState.Blocked;
                await db.SaveChangesAsync(cancellationToken);
                return Conflict(new { code = reservation.Code, available = reservation.Available });
            }
            reservationIds.Add(reservation.AllocationId!.Value);
        }
        var token = connection.ProtectedAccessToken is null ? "mock" : vault.Unprotect(connection.ProtectedAccessToken);
        try
        {
            var campaignId = await meta.CreateCampaignPausedAsync(token, connection.AdAccountExternalId!, request.Name, request.Objective, cancellationToken);
            var countries = JsonSerializer.Deserialize<string[]>(envelope.AllowedCountriesJson) ?? ["EG"];
            foreach (var managedAd in managedAds)
            {
                var creative = creatives.Single(x => x.Id == managedAd.CreativeId);
                var positions = JsonSerializer.Deserialize<string[]>(managedAd.PositionsJson) ?? ["feed"];
                var adSetId = await meta.CreateAdSetPausedAsync(token, new MetaAdSetRequest(connection.AdAccountExternalId!, campaignId, managedAd.Name, managedAd.DailyBudget, request.OptimizationEvent, countries, positions, connection.DatasetExternalId, request.CustomEventType), cancellationToken);
                var providerAd = await meta.CreateExistingPostAdPausedAsync(token, new MetaExistingPostAdRequest(connection.AdAccountExternalId!, adSetId, creative.SourceExternalId!, managedAd.Name), cancellationToken);
                managedAd.CampaignExternalId = campaignId; managedAd.AdSetExternalId = adSetId; managedAd.AdExternalId = providerAd.AdId;
                managedAd.BudgetOwnerExternalId = adSetId; managedAd.BudgetOwnerType = "AdSet";
                managedAd.EffectiveStatus = "PAUSED"; managedAd.LastSyncedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, cancellationToken, promotion.Id);
            foreach (var commandId in commandIds)
                backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
            return Accepted(new { promotion.Id, state = promotion.State.ToString(), ads = creatives.Count, allocation = allocation.Slices,
                providerState = commandIds.Count > 0 ? "ACTIVATION_QUEUED" : "PAUSED_PENDING_AI_REVIEW", queuedCommands = commandIds.Count });
        }
        catch (HttpRequestException ex)
        {
            foreach (var reservationId in reservationIds)
                await allocator.ReleaseAsync(db, projectId, reservationId, cancellationToken);
            db.TrackingIncidents.Add(new TrackingIncident { ProjectId = projectId, Category = "ProviderCreation", Severity = "Critical", Summary = "Meta creation result requires reconciliation before activation.", EvidenceJson = JsonSerializer.Serialize(new { errorType = ex.GetType().Name }), DetectedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, new { code = "ADS_PROVIDER_RECONCILIATION_REQUIRED", promotion.Id });
        }
    }
    [HttpGet("offers")]
    public async Task<IActionResult> Offers(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingOffers.AsNoTracking().Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id, x.Name, x.Type, x.Price, x.Currency, x.State, x.DestinationsJson, x.MarketsJson }).ToListAsync(cancellationToken));
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
                         select new { Ad = ad, creative.MediaType }).Take(30).ToListAsync(cancellationToken);
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
                spend = result.Spend, impressions = snapshots.Where(snapshot => snapshot.TargetId == row.Ad.Id).Sum(snapshot => snapshot.Impressions),
                clicks = snapshots.Where(snapshot => snapshot.TargetId == row.Ad.Id).Sum(snapshot => snapshot.Clicks),
                results = result.Conversions, cpa = result.Cpa, verdict = result.Verdict.ToString() };
        }));
    }

    [HttpGet("conversions")]
    public async Task<IActionResult> Conversions(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingConversions.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new { x.Id, x.EventType, x.OccurredAtUtc, x.CurrentValue, x.Currency, state = x.State.ToString(), x.AttributionMethod, x.AdvertisementId }).Take(100).ToListAsync(cancellationToken));
    }

    [HttpGet("decisions")]
    public async Task<IActionResult> Decisions(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingDecisions.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.ActionType, x.TargetType, x.RiskClass, state = x.State.ToString(), x.EvidenceStartUtc, x.EvidenceEndUtc, x.EvaluateAfterUtc, x.CreatedAt }).Take(100).ToListAsync(cancellationToken));
    }
}

public sealed record ImportPagePost(string Id, string MediaType, DateTime? CreatedAtUtc);
public sealed record ImportPostsRequest(IReadOnlyList<ImportPagePost> Posts);
public sealed record ActivateAdsRequest(Guid OfferId, IReadOnlyList<Guid> CreativeIds, string Name, string Objective, string DestinationUrl, string OptimizationEvent, string? CustomEventType);
