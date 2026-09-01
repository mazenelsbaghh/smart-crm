using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modules.Advertising.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AdvertisingCloneVariable { Creative, AudienceSuggestion }

public sealed class AdvertisingCloneService(AppDbContext db)
{
    public async Task<CampaignPlan> CloneAsync(Guid projectId, Guid sourcePlanId, AdvertisingCloneVariable variable,
        Guid? replacementCreativeId, string? audienceSuggestionsJson, CancellationToken cancellationToken = default)
    {
        var source = await db.AdvertisingCampaignPlans.IgnoreQueryFilters().AsNoTracking().SingleAsync(
            plan => plan.ProjectId == projectId && plan.Id == sourcePlanId, cancellationToken);
        var cloneHash = AdvertisingAuditService.HashState(JsonSerializer.Serialize(new
        {
            SourcePlanId = sourcePlanId,
            SourcePlanHash = source.PlanHash,
            Variable = variable,
            ReplacementCreativeId = replacementCreativeId,
            AudienceSuggestionsJson = audienceSuggestionsJson ?? string.Empty
        }));
        var existingClone = await db.AdvertisingCampaignPlans.IgnoreQueryFilters().SingleOrDefaultAsync(
            plan => plan.ProjectId == projectId && plan.PlanHash == cloneHash, cancellationToken);
        if (existingClone is not null)
            return existingClone;
        var sourceAudience = await db.AdvertisingAudienceStrategies.IgnoreQueryFilters().AsNoTracking().SingleAsync(
            audience => audience.ProjectId == projectId && audience.Id == source.AudienceStrategyId, cancellationToken);
        var audience = CopyAudience(sourceAudience);
        if (variable == AdvertisingCloneVariable.AudienceSuggestion)
            audience.AudienceSuggestionsJson = string.IsNullOrWhiteSpace(audienceSuggestionsJson) ? "{}" : audienceSuggestionsJson;
        db.AdvertisingAudienceStrategies.Add(audience);

        var clone = CopyPlan(source, audience.Id, cloneHash);
        db.AdvertisingCampaignPlans.Add(clone);
        var sourceCreatives = await db.AdvertisingCampaignPlanCreatives.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.PlanId == sourcePlanId).ToListAsync(cancellationToken);
        foreach (var item in sourceCreatives)
        {
            var creativeId = variable == AdvertisingCloneVariable.Creative && replacementCreativeId is { } replacement
                ? replacement : item.CreativeId;
            db.AdvertisingCampaignPlanCreatives.Add(new CampaignPlanCreative
            {
                ProjectId = projectId, PlanId = clone.Id, CreativeId = creativeId,
                CreativeVariantId = item.CreativeVariantId, Role = item.Role, ConceptKey = item.ConceptKey,
                HookKey = item.HookKey, PlacementCompatibilityJson = item.PlacementCompatibilityJson, State = item.State
            });
        }
        if (sourceCreatives.Count == 0 && replacementCreativeId is { } selectedCreativeId)
            db.AdvertisingCampaignPlanCreatives.Add(new CampaignPlanCreative
            {
                ProjectId = projectId, PlanId = clone.Id, CreativeId = selectedCreativeId,
                Role = "Variant", State = "Selected"
            });
        await db.SaveChangesAsync(cancellationToken);
        return clone;
    }

    private static CampaignPlan CopyPlan(CampaignPlan source, Guid audienceId, string cloneHash) => new()
    {
        ProjectId = source.ProjectId, ConnectionId = source.ConnectionId, EnvelopeId = source.EnvelopeId,
        EnvelopeVersion = source.EnvelopeVersion, OfferId = source.OfferId, DestinationId = source.DestinationId,
        CapabilitySnapshotId = source.CapabilitySnapshotId, Version = source.Version + 1, Name = source.Name + " · Test",
        BusinessGoal = source.BusinessGoal, Objective = source.Objective, OptimizationGoal = source.OptimizationGoal,
        OptimizationFallbackOrderJson = source.OptimizationFallbackOrderJson, BidStrategy = source.BidStrategy,
        BudgetMode = source.BudgetMode, DailyBudget = source.DailyBudget, Currency = source.Currency,
        StartsAtUtc = source.StartsAtUtc, EndsAtUtc = source.EndsAtUtc, SpecialAdCategory = source.SpecialAdCategory,
        PlacementMode = source.PlacementMode, AudienceStrategyId = audienceId, ExperimentId = source.ExperimentId,
        PlanJson = source.PlanJson, PlanHash = cloneHash, ReadinessJson = source.ReadinessJson,
        State = "Ready", CreatedBy = "Clone"
    };

    private static AudienceStrategy CopyAudience(AudienceStrategy source) => new()
    {
        ProjectId = source.ProjectId, OfferId = source.OfferId, EnvelopeId = source.EnvelopeId, Version = source.Version + 1,
        IncludedGeoJson = source.IncludedGeoJson, ExcludedGeoJson = source.ExcludedGeoJson, MinimumAge = source.MinimumAge,
        MaximumAgeSuggestion = source.MaximumAgeSuggestion, RequiredLanguagesJson = source.RequiredLanguagesJson,
        CustomAudienceExclusionsJson = source.CustomAudienceExclusionsJson, AudienceSuggestionsJson = source.AudienceSuggestionsJson,
        AuthorizedSourceGrantIdsJson = source.AuthorizedSourceGrantIdsJson,
        SpecialCategoryConstraintsJson = source.SpecialCategoryConstraintsJson, EstimatedReachJson = source.EstimatedReachJson,
        DefinitionHash = source.DefinitionHash, EvidenceJson = source.EvidenceJson, State = source.State
    };
}
