using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class MetaProviderReconciliationService(AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault)
{
    public async Task<IReadOnlyList<ProviderValidationFinding>> VerifyHierarchyAsync(Guid projectId, Guid planId, Guid operationId,
        MetaPlanPayload planned, string providerCampaignId, string providerAdSetId, string providerCreativeId,
        string providerAdId, CancellationToken cancellationToken = default)
    {
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.ProtectedAccessToken != null, cancellationToken);
        var token = vault.Unprotect(connection.ProtectedAccessToken!);
        var campaign = await meta.ReadObjectAsync(token, providerCampaignId,
            "id,status,effective_status,objective,bid_strategy,daily_budget,buying_type", cancellationToken);
        var effective = await meta.ReadObjectAsync(token, providerAdSetId,
            "id,status,effective_status,optimization_goal,bid_strategy,destination_type,promoted_object,targeting", cancellationToken);
        var creative = await meta.ReadObjectAsync(token, providerCreativeId,
            "id,name,object_story_id,object_story_spec", cancellationToken);
        var ad = await meta.ReadObjectAsync(token, providerAdId,
            "id,status,effective_status,adset_id,creative", cancellationToken);
        var violations = MetaProviderEquivalence.CompareWhatsAppAdSet(planned.AdSet, effective).ToList();
        Compare(campaign, "status", "PAUSED", "Campaign");
        Compare(campaign, "objective", planned.Campaign.Objective, "Campaign");
        Compare(campaign, "bid_strategy", planned.Campaign.BidStrategy, "Campaign");
        Compare(ad, "status", "PAUSED", "Ad");
        Compare(ad, "adset_id", providerAdSetId, "Ad");
        if (!ad.TryGetValue("creative", out var adCreative) || !adCreative.Contains(providerCreativeId, StringComparison.Ordinal))
            violations.Add(new("creative", "ADS_PROVIDER_FIELD_DRIFT", InvariantSeverity.Blocking, "Ad creative differs from the verified provider creative."));
        if (planned.Creative.SourceType == nameof(CreativeSourceType.ExistingPagePost) &&
            (!creative.TryGetValue("object_story_id", out var story) || story != planned.Creative.SourceExternalId))
            violations.Add(new("object_story_id", "ADS_CREATIVE_SOURCE_DRIFT", InvariantSeverity.Blocking, "Provider creative source differs from the selected Page post."));
        var findings = violations.Select(violation => new ProviderValidationFinding
        {
            ProjectId = projectId, PlanId = planId, OperationId = operationId, Severity = violation.Severity,
            Stage = "ReadBack", ObjectType = violation.Field.StartsWith("campaign.") ? "Campaign" : violation.Field.StartsWith("ad.") ? "Ad" : "AdSet",
            ObjectId = providerAdSetId, Field = violation.Field,
            Code = violation.Code, Message = violation.Message, NextSafeAction = "KeepPausedAndReview"
        }).ToList();
        db.AdvertisingProviderValidationFindings.AddRange(findings);
        AddSnapshot("Campaign", providerCampaignId, campaign);
        AddSnapshot("AdSet", providerAdSetId, effective);
        AddSnapshot("Creative", providerCreativeId, creative);
        AddSnapshot("Ad", providerAdId, ad);
        await db.SaveChangesAsync(cancellationToken);
        return findings;

        void Compare(IReadOnlyDictionary<string, string> state, string field, string expected, string objectType)
        {
            if (!state.TryGetValue(field, out var value) || !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                violations.Add(new($"{objectType.ToLowerInvariant()}.{field}", "ADS_PROVIDER_FIELD_DRIFT", InvariantSeverity.Blocking,
                    $"Effective {objectType} {field} differs from the approved plan."));
        }

        void AddSnapshot(string objectType, string objectId, IReadOnlyDictionary<string, string> state)
        {
            var normalized = JsonSerializer.Serialize(state);
            db.AdvertisingProviderObjectSnapshots.Add(new ProviderObjectSnapshot
            {
                ProjectId = projectId, ConnectionId = connection.Id, PlanId = planId, OperationId = operationId,
                ObjectType = objectType, ProviderObjectId = objectId, SnapshotType = "EffectiveReadBack",
                NormalizedStateJson = normalized, StateHash = AdvertisingAuditService.HashState(normalized), CapturedAtUtc = DateTime.UtcNow
            });
        }
    }
}
