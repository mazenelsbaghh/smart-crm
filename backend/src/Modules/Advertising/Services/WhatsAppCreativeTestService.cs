using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Workers;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record WhatsAppTestResult(int CreatedAds, string State, string Reason);

public sealed class WhatsAppCreativeTestService(
    AppDbContext db,
    MetaAdsClient meta,
    AdvertisingSecretVault vault,
    AdvertisingDecisionAi ai,
    AdvertisingDecisionService decisions,
    IBackgroundJobClient backgroundJobs)
{
    private const int MaximumVariants = 2;

    public async Task<WhatsAppTestResult> CreateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var hasWhatsAppDestination = await db.AdvertisingOffers.IgnoreQueryFilters()
            .AnyAsync(offer => offer.ProjectId == projectId && offer.State == "Eligible" && offer.DestinationsJson.Contains("wa.me"), cancellationToken);
        if (!hasWhatsAppDestination)
            return await WaitAsync(projectId, "لا توجد وجهة WhatsApp موثقة للمشروع، لذلك لن ينشئ النظام أي إعلان.", cancellationToken);

        var baseline = await LoadBaselineAsync(projectId, cancellationToken);
        if (baseline is null) return await WaitAsync(projectId, "لا توجد حملة WhatsApp نشطة تصلح كأساس للاختبار.", cancellationToken);

        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(
            current => current.ProjectId == projectId && current.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (connection?.ProtectedAccessToken is null || connection.AdAccountExternalId is null || connection.PageExternalId is null || baseline.AdSetExternalId is null)
            return await WaitAsync(projectId, "اتصال Facebook أو صفحة المشروع أو حملة WhatsApp غير متاحة للاختبار.", cancellationToken);

        var candidates = await LoadCandidatesAsync(projectId, baseline.CreativeId, connection, cancellationToken);
        if (candidates.Count == 0) return await WaitAsync(projectId, "لا توجد بوستات صفحة جديدة متاحة. سيحاول النظام تلقائيًا مع أول محتوى جديد.", cancellationToken);

        var evidence = JsonSerializer.Serialize(new
        {
            destination = "WhatsApp",
            baseline = new { baseline.Id, baseline.Name, baseline.AdSetExternalId, baseline.DailyBudget },
            candidates = candidates.Select(creative => new { creative.Id, creative.SourceExternalId, creative.MediaType, creative.RecommendationScore })
        });
        var review = await ai.ReviewActionAsync(projectId, "CreateTest", evidence, cancellationToken);
        var decision = CreateDecision(projectId, baseline.PromotionId, evidence, review);
        db.AdvertisingDecisions.Add(decision);
        AddReviews(projectId, decision, evidence, review);
        if (review.AuditorVerdict != DecisionVerdict.Approve)
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(0, "WAITING", review.Reason);
        }

        try
        {
            var token = vault.Unprotect(connection.ProtectedAccessToken);
            var createdAds = 0;
            var createdAdIds = new List<Guid>();
            foreach (var creative in candidates)
            {
                var providerAd = await meta.CreateExistingPostAdPausedAsync(token,
                    new MetaExistingPostAdRequest(connection.AdAccountExternalId, baseline.AdSetExternalId, creative.SourceExternalId!, $"AI WhatsApp Test · {creative.MediaType}"), cancellationToken);
                var managedAd = new ManagedAdvertisement
                {
                    ProjectId = projectId,
                    PromotionId = baseline.PromotionId,
                    CreativeId = creative.Id,
                    Name = $"AI WhatsApp Test · {creative.MediaType}",
                    CampaignExternalId = baseline.CampaignExternalId,
                    AdSetExternalId = baseline.AdSetExternalId,
                    AdExternalId = providerAd.AdId,
                    BudgetOwnerExternalId = baseline.BudgetOwnerExternalId ?? baseline.AdSetExternalId,
                    BudgetOwnerType = baseline.BudgetOwnerType,
                    PublisherPlatform = "facebook",
                    PositionsJson = baseline.PositionsJson,
                    DailyBudget = baseline.DailyBudget,
                    ConfiguredStatus = ManagedDeliveryState.Paused,
                    EffectiveStatus = "PAUSED"
                };
                db.ManagedAdvertisements.Add(managedAd);
                await db.SaveChangesAsync(cancellationToken);
                createdAds++;
                createdAdIds.Add(managedAd.Id);
            }
            decision.State = DecisionState.Executed;
            await db.SaveChangesAsync(cancellationToken);

            var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, cancellationToken, baseline.PromotionId, createdAdIds);
            foreach (var commandId in commandIds)
                backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
            return new(createdAds, commandIds.Count > 0 ? "ACTIVATION_QUEUED" : "PAUSED_PENDING_REVIEW", commandIds.Count > 0 ? "تمت مراجعة التفعيل." : "بانتظار مراجعة تفعيل مستقلة.");
        }
        catch (HttpRequestException ex)
        {
            decision.State = DecisionState.Failed;
            db.TrackingIncidents.Add(new TrackingIncident
            {
                ProjectId = projectId,
                Category = "WhatsAppCreativeTest",
                Severity = "Critical",
                Summary = "تعذر إكمال اختبار WhatsApp في Meta. أي إعلان تم إنشاؤه بقي متوقفًا لحين المراجعة.",
                EvidenceJson = JsonSerializer.Serialize(new { errorType = ex.GetType().Name }),
                DetectedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(0, "FAILED", "Meta رفضت إنشاء اختبار WhatsApp.");
        }
    }

    private async Task<ManagedAdvertisement?> LoadBaselineAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.ManagedAdvertisements.IgnoreQueryFilters().Where(ad => ad.ProjectId == projectId
            && ad.ConfiguredStatus == ManagedDeliveryState.Active && ad.AdSetExternalId != null && ad.AdExternalId != null)
            .OrderByDescending(ad => ad.LastSyncedAtUtc ?? ad.ImportedAtUtc ?? ad.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private async Task<List<AdvertisingCreative>> LoadCandidatesAsync(Guid projectId, Guid baselineCreativeId,
        AdvertisingConnection connection, CancellationToken cancellationToken)
    {
        var candidates = await EligibleCandidatesAsync(projectId, baselineCreativeId, cancellationToken);
        if (candidates.Count > 0) return candidates;

        var token = vault.Unprotect(connection.ProtectedAccessToken!);
        var pagePosts = await meta.GetPagePostsAsync(token, connection.PageExternalId!, cancellationToken);
        foreach (var post in pagePosts.OrderByDescending(post => post.CreatedAtUtc).Take(MaximumVariants * 2))
        {
            var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId}:{post.Id}"))).ToLowerInvariant();
            if (await db.AdvertisingCreatives.IgnoreQueryFilters().AnyAsync(creative => creative.ProjectId == projectId && creative.SourceHash == sourceHash, cancellationToken)) continue;
            var mediaType = post.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase) ? CreativeMediaType.Video : CreativeMediaType.Image;
            var rank = CreativeRankingService.Rank(mediaType, post.CreatedAtUtc, DateTime.UtcNow);
            db.AdvertisingCreatives.Add(new AdvertisingCreative
            {
                ProjectId = projectId, SourceType = CreativeSourceType.ExistingPagePost, SourceExternalId = post.Id, SourceHash = sourceHash,
                MediaType = mediaType, RightsState = "PageOwned", PolicyState = "PendingMetaReview", EligibilityState = CreativeEligibility.Eligible,
                RecommendationScore = rank.Score, RecommendationEvidenceJson = JsonSerializer.Serialize(new { source = "FacebookPagePost", rank.Explanation }),
                LastAnalyzedAtUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return await EligibleCandidatesAsync(projectId, baselineCreativeId, cancellationToken);
    }

    private async Task<List<AdvertisingCreative>> EligibleCandidatesAsync(Guid projectId, Guid baselineCreativeId, CancellationToken cancellationToken)
    {
        var testedCreativeIds = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(ad => ad.ProjectId == projectId)
            .Select(ad => ad.CreativeId).ToListAsync(cancellationToken);
        testedCreativeIds.Add(baselineCreativeId);
        return await db.AdvertisingCreatives.IgnoreQueryFilters().Where(creative => creative.ProjectId == projectId
            && creative.EligibilityState == CreativeEligibility.Eligible
            && creative.SourceType == CreativeSourceType.ExistingPagePost
            && creative.SourceExternalId != null
            && creative.RecommendationScore > 0
            && !testedCreativeIds.Contains(creative.Id))
            .OrderByDescending(creative => creative.RecommendationScore).ThenByDescending(creative => creative.LastAnalyzedAtUtc)
            .Take(MaximumVariants).ToListAsync(cancellationToken);
    }

    private async Task<WhatsAppTestResult> WaitAsync(Guid projectId, string reason, CancellationToken cancellationToken)
    {
        db.AdvertisingDecisions.Add(new AdvertisingDecision
        {
            ProjectId = projectId,
            ActionType = "CreateWhatsAppTest",
            TargetType = "Creative",
            EvidenceStartUtc = DateTime.UtcNow,
            EvidenceEndUtc = DateTime.UtcNow,
            EvidenceJson = JsonSerializer.Serialize(new { destination = "WhatsApp", reason }),
            ProposedChangeJson = "{}",
            RiskClass = "Financial",
            State = DecisionState.Waiting
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(0, "WAITING", reason);
    }

    private static AdvertisingDecision CreateDecision(Guid projectId, Guid promotionId, string evidence, AiActivationReview review) => new()
    {
        ProjectId = projectId,
        PromotionId = promotionId,
        ActionType = "CreateWhatsAppTest",
        TargetType = "Creative",
        EvidenceStartUtc = DateTime.UtcNow,
        EvidenceEndUtc = DateTime.UtcNow,
        EvidenceJson = evidence,
        ProposedChangeJson = "{\"destination\":\"WhatsApp\"}",
        RiskClass = "Financial",
        State = review.AuditorVerdict == DecisionVerdict.Approve ? DecisionState.Approved : DecisionState.Waiting
    };

    private void AddReviews(Guid projectId, AdvertisingDecision decision, string evidence, AiActivationReview review)
    {
        var evidenceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant();
        db.AdvertisingDecisionReviews.AddRange(
            new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Strategist", Verdict = review.StrategistVerdict, ReasonsJson = review.StrategistJson, EvidenceHash = evidenceHash },
            new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Auditor", Verdict = review.AuditorVerdict, ReasonsJson = review.AuditorJson, EvidenceHash = evidenceHash });
    }
}
