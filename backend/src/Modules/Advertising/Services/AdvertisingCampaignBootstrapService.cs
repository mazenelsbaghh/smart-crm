using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record CampaignBootstrapResult(string State, Guid? PlanId = null, Guid? OperationId = null, string? Reason = null);

public sealed class AdvertisingCampaignBootstrapService(
    AppDbContext db,
    CampaignPlanCompiler compiler,
    CampaignProvisioningService provisioning)
{
    public async Task<CampaignBootstrapResult> EnsurePausedHierarchyAsync(Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var existingHierarchy = await db.AdvertisingManagedCampaigns.IgnoreQueryFilters()
            .AnyAsync(campaign => campaign.ProjectId == projectId && campaign.EffectiveStatus != "DELETED", cancellationToken);
        if (existingHierarchy)
        {
            var repaired = await provisioning.RetryFailedFinalAdAsync(projectId, cancellationToken);
            await provisioning.ApplyInitialCanaryBudgetAsync(projectId, cancellationToken);
            return new(repaired ? "FinalAdRepaired" : "HierarchyExists");
        }

        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters()
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.State == EnvelopeState.Active, cancellationToken);
        if (envelope?.OfferId is not { } offerId)
            return new("Wait", Reason: "ADS_ACTIVE_ENVELOPE_REQUIRED");

        var plan = await db.AdvertisingCampaignPlans.IgnoreQueryFilters()
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.OfferId == offerId && candidate.State == "Ready", cancellationToken);
        if (plan is null)
        {
            var compilation = await compiler.CompileAsync(projectId, offerId, cancellationToken);
            if (!compilation.Ready || compilation.Plan is null)
                return new("Wait", Reason: compilation.BlockingReasons.FirstOrDefault());
            plan = compilation.Plan;
        }

        var creative = await db.AdvertisingCreatives.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.MediaType == CreativeMediaType.Video
                && candidate.EligibilityState == CreativeEligibility.Eligible
                && candidate.SourceExternalId != null)
            .OrderByDescending(candidate => candidate.MediaType == CreativeMediaType.Video)
            .ThenByDescending(candidate => candidate.RecommendationScore)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (creative is null) return new("Wait", plan.Id, Reason: "ADS_ELIGIBLE_VIDEO_REQUIRED");

        var hierarchyVersion = await db.AdvertisingManagedCampaigns.IgnoreQueryFilters()
            .CountAsync(candidate => candidate.ProjectId == projectId, cancellationToken) + 1;
        var provisioned = await provisioning.ProvisionPausedAsync(projectId, plan.Id, creative.Id, null,
            Guid.Empty, $"autopilot-bootstrap:{plan.Id:N}:v{hierarchyVersion}", cancellationToken);
        return new("ProvisionedPaused", plan.Id, provisioned.RootOperationId);
    }
}
