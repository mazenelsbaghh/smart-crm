using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Workers;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record WhatsAppTestResult(int CreatedAds, string State, string Reason);

public sealed class WhatsAppCreativeTestService(
    AppDbContext db,
    AdvertisingCloneService clones,
    AdvertisingExperimentService experiments,
    CampaignProvisioningService provisioning,
    AdvertisingDecisionService decisions,
    IBackgroundJobClient backgroundJobs,
    BudgetAllocator budgets,
    AdvertisingOwnershipPolicy ownership)
{
    private const int DefaultAttributionWindowDays = 7;

    public async Task<WhatsAppTestResult> CreateAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var baseline = await EligibleBaselineAsync(projectId, cancellationToken);
        if (baseline?.PlanId is null)
            return Wait("لا توجد حملة WhatsApp مدارة ونشطة تصلح كذراع Control.");
        var sourcePlan = await db.AdvertisingCampaignPlans.IgnoreQueryFilters()
            .SingleAsync(plan => plan.ProjectId == projectId && plan.Id == baseline.PlanId, cancellationToken);
        var existing = await PendingExperimentAsync(projectId, sourcePlan.OfferId, cancellationToken);
        if (existing is not null)
            return await ProvisionExperimentAsync(projectId, existing, cancellationToken);

        var candidate = await BestUnusedCreativeAsync(projectId, baseline.CreativeId, cancellationToken);
        if (candidate is null)
            return Wait("لا يوجد محتوى جديد مؤهل بحقوق وسياسة واضحتين؛ سيعيد النظام الفحص عند وصول محتوى جديد.");
        var envelope = await ActiveEnvelopeAsync(projectId, sourcePlan.EnvelopeId, cancellationToken);
        if (envelope is null) return Wait("لا يوجد تفويض ميزانية نشط للاختبار.");

        var testBudget = CreativeTestBudget(envelope);
        if (testBudget <= 0) return Wait("لا توجد سلطة صرف كافية لاختبار محتوى داخل هامش الأمان.");
        var variantPlan = await clones.CloneAsync(projectId, sourcePlan.Id, AdvertisingCloneVariable.Creative,
            candidate.Id, null, cancellationToken);
        var experiment = await experiments.CreateAsync(projectId,
            CreativeExperimentDefinition(sourcePlan, variantPlan, candidate, envelope, testBudget), cancellationToken);
        variantPlan.ExperimentId = experiment.Id;
        await db.SaveChangesAsync(cancellationToken);
        return await ProvisionExperimentAsync(projectId, experiment, cancellationToken);
    }

    private async Task<WhatsAppTestResult> ProvisionExperimentAsync(Guid projectId,
        AdvertisingExperiment experiment, CancellationToken cancellationToken)
    {
        if (experiment.State == "Running") return Wait("يوجد اختبار محتوى يعمل بالفعل؛ لن ينشئ النظام نسخة مكررة.");
        var variantArm = await db.AdvertisingExperimentArms.IgnoreQueryFilters().SingleAsync(arm =>
            arm.ProjectId == projectId && arm.ExperimentId == experiment.Id && !arm.IsControl, cancellationToken);
        var creativeId = CreativeId(variantArm.ChangedValueJson);
        try
        {
            var hierarchy = await provisioning.ProvisionPausedAsync(projectId, variantArm.PlanId, creativeId,
                null, Guid.Empty, $"creative-test:{experiment.Id:N}", cancellationToken);
            if (!string.Equals(hierarchy.State, ProviderReconciliationState.VerifiedPaused.ToString(), StringComparison.Ordinal))
                return await NeedsAttentionAsync(experiment, "لم يثبت التطابق الفعلي للحملة المتوقفة في Meta.", cancellationToken);
            if (!await ReserveVariantBudgetAsync(projectId, experiment, variantArm, hierarchy.AdvertisementId, cancellationToken))
                return await NeedsAttentionAsync(experiment, "بقي الإعلان متوقفًا لأن سلطة الصرف المتاحة لا تكفي الاختبار.", cancellationToken, "BlockedBudget");
            var commandIds = await decisions.ProposeCanaryActivationAsync(projectId, cancellationToken,
                adIds: [hierarchy.AdvertisementId]);
            foreach (var commandId in commandIds)
                backgroundJobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, commandId, CancellationToken.None));
            experiment.State = commandIds.Count > 0 ? "Running" : "Planned";
            experiment.StartedAtUtc = commandIds.Count > 0 ? DateTime.UtcNow : null;
            await db.SaveChangesAsync(cancellationToken);
            return new(1, commandIds.Count > 0 ? "ACTIVATION_QUEUED" : "PAUSED_PENDING_REVIEW",
                commandIds.Count > 0 ? "اجتاز الاختبار المراجعات وبدأ داخل السقف." : "تم إنشاء الذراع وقراءته كمتوقف؛ القرار المالي ما زال WAIT.");
        }
        catch (AdvertisingException exception)
        {
            experiment.State = "NeedsAttention";
            experiment.ConclusionJson = JsonSerializer.Serialize(new { code = exception.Code });
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<bool> ReserveVariantBudgetAsync(Guid projectId, AdvertisingExperiment experiment,
        AdvertisingExperimentArm variantArm, Guid advertisementId, CancellationToken cancellationToken)
    {
        if (await db.AdvertisingBudgetAllocations.IgnoreQueryFilters().AnyAsync(allocation => allocation.ProjectId == projectId
            && allocation.TargetId == advertisementId && allocation.Purpose == BudgetPurpose.CreativeTest
            && allocation.State == "Active", cancellationToken)) return true;
        var reservation = await budgets.ReserveBatchAsync(db, new BudgetReservationBatch(projectId,
            experiment.EnvelopeId, [new(advertisementId, BudgetPurpose.CreativeTest, variantArm.AllocatedBudget)]), cancellationToken);
        if (!reservation.Reserved) return false;
        var allocations = await db.AdvertisingBudgetAllocations.IgnoreQueryFilters()
            .Where(allocation => reservation.AllocationIds.Contains(allocation.Id)).ToListAsync(cancellationToken);
        foreach (var allocation in allocations) { allocation.ExperimentId = experiment.Id; allocation.PlanId = variantArm.PlanId; }
        return true;
    }

    private async Task<WhatsAppTestResult> NeedsAttentionAsync(AdvertisingExperiment experiment, string reason,
        CancellationToken cancellationToken, string state = "NeedsAttention")
    {
        experiment.State = state;
        experiment.ConclusionJson = JsonSerializer.Serialize(new { reason });
        await db.SaveChangesAsync(cancellationToken);
        return Wait(reason);
    }

    private async Task<ManagedAdvertisement?> EligibleBaselineAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await ownership.ManagedAdsAsync(projectId, activeOnly: true, cancellationToken))
            .Where(ad => ad.PlanId != null && ad.DestinationType == "WHATSAPP")
            .OrderByDescending(ad => ad.LastSyncedAtUtc ?? ad.CreatedAt)
            .FirstOrDefault();

    private Task<AdvertisingExperiment?> PendingExperimentAsync(Guid projectId, Guid offerId, CancellationToken cancellationToken) =>
        db.AdvertisingExperiments.IgnoreQueryFilters().Where(experiment => experiment.ProjectId == projectId
            && experiment.OfferId == offerId && experiment.PrimaryVariable == "creative"
            && (experiment.State == "Planned" || experiment.State == "Running" || experiment.State == "BlockedBudget"))
            .OrderByDescending(experiment => experiment.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    private Task<AdvertisingCreative?> BestUnusedCreativeAsync(Guid projectId, Guid baselineCreativeId,
        CancellationToken cancellationToken) => db.AdvertisingCreatives.IgnoreQueryFilters()
        .Where(creative => creative.ProjectId == projectId && creative.Id != baselineCreativeId
            && creative.MediaType == CreativeMediaType.Video
            && creative.EligibilityState == CreativeEligibility.Eligible && creative.RecommendationScore > 0
            && creative.SourceExternalId != null
            && (creative.SourceType != CreativeSourceType.ExistingPagePost || creative.SourceExternalId.Contains("_"))
            && creative.RightsState != "Rejected" && creative.PolicyState != "Rejected")
        .OrderByDescending(creative => creative.MediaType == CreativeMediaType.Video)
        .ThenByDescending(creative => creative.RecommendationScore).ThenByDescending(creative => creative.LastAnalyzedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    private Task<AutonomyEnvelope?> ActiveEnvelopeAsync(Guid projectId, Guid envelopeId, CancellationToken cancellationToken) =>
        db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(envelope => envelope.ProjectId == projectId
            && envelope.Id == envelopeId && envelope.State == EnvelopeState.Active, cancellationToken);

    private decimal CreativeTestBudget(AutonomyEnvelope envelope) => budgets.Allocate(envelope.DailyCap,
        envelope.SafetyReservePercent, 1, false).Slices.Single(slice => slice.Purpose == BudgetPurpose.CreativeTest).Amount;

    private static ExperimentDefinition CreativeExperimentDefinition(CampaignPlan controlPlan, CampaignPlan variantPlan,
        AdvertisingCreative creative, AutonomyEnvelope envelope, decimal testBudget) => new(
        controlPlan.OfferId, controlPlan.DestinationId, envelope.Id, $"Creative test · {creative.Id:N}",
        "مقارنة محتوى جديد مع الـControl على نتيجة واتساب المؤهلة", "creative", "QualifiedLeadOrPurchase",
        DefaultAttributionWindowDays, 72, testBudget, 10, .8m, 24, testBudget,
        "{\"maximumLossPercent\":25}",
        [new("Control", true, controlPlan.Id, "{}", testBudget / 2),
         new("Variant", false, variantPlan.Id, JsonSerializer.Serialize(new { creative = creative.Id }), testBudget / 2)]);

    private static Guid CreativeId(string changedValueJson)
    {
        using var document = JsonDocument.Parse(changedValueJson);
        return document.RootElement.GetProperty("creative").GetGuid();
    }

    private static WhatsAppTestResult Wait(string reason) => new(0, "WAITING", reason);
}
