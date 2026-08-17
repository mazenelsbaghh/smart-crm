using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record ExistingAdCandidate(MetaExistingAd Ad, bool AlreadyManaged, string? IneligibleReason)
{
    public bool Eligible => !AlreadyManaged && IneligibleReason is null;
}

public sealed record ExistingCampaignImportResult(int ImportedAds, int ExistingAds, decimal ReservedDailyBudget);

public sealed class ExistingCampaignImportService(AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault, BudgetAllocator budgets)
{
    public async Task<IReadOnlyList<ExistingAdCandidate>> PreviewAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var (connection, token) = await FacebookConnectionAsync(projectId, cancellationToken);
        var providerAds = await meta.GetExistingAdsAsync(token, connection.AdAccountExternalId!, cancellationToken);
        var managedIds = await db.ManagedAdvertisements.IgnoreQueryFilters().AsNoTracking()
            .Where(ad => ad.ProjectId == projectId && ad.AdExternalId != null)
            .Select(ad => ad.AdExternalId!)
            .ToHashSetAsync(cancellationToken);
        return providerAds.Select(ad => new ExistingAdCandidate(ad, managedIds.Contains(ad.AdId), IneligibleReason(ad))).ToArray();
    }

    public async Task<ExistingCampaignImportResult> ImportAsync(Guid projectId, IReadOnlyCollection<string> requestedAdIds, CancellationToken cancellationToken)
    {
        if (requestedAdIds.Count is < 1 or > 50) throw new AdvertisingException("ADS_IMPORT_COUNT_INVALID", "Select between 1 and 50 Facebook ads.", 422);
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.State != EnvelopeState.Revoked, cancellationToken)
            ?? throw new AdvertisingException("ADS_ENVELOPE_REQUIRED", "Set the daily authorization cap before importing running ads.", 409);
        var candidates = await PreviewAsync(projectId, cancellationToken);
        var selectedIds = requestedAdIds.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var selected = candidates.Where(candidate => selectedIds.Contains(candidate.Ad.AdId)).ToArray();
        if (selected.Length != selectedIds.Count) throw new AdvertisingException("ADS_PROVIDER_AD_NOT_FOUND", "One or more selected Facebook ads no longer exist.", 409);
        if (selected.Any(candidate => candidate.IneligibleReason is not null))
            throw new AdvertisingException("ADS_FACEBOOK_ONLY_REQUIRED", "Only ads restricted to supported Facebook placements can be managed.", 422);
        var newAds = selected.Where(candidate => !candidate.AlreadyManaged).Select(candidate => candidate.Ad).ToArray();
        if (newAds.Length == 0) return new(0, selected.Length, 0m);

        var imported = BuildManagedEntities(projectId, envelope, newAds);
        db.AdvertisingPromotions.AddRange(imported.Promotions);
        db.AdvertisingCreatives.AddRange(imported.Creatives);
        db.ManagedAdvertisements.AddRange(imported.Ads);
        var budgetOwners = imported.Ads.GroupBy(ad => ad.BudgetOwnerExternalId ?? ad.AdSetExternalId ?? ad.AdExternalId!)
            .Select(group => group.First()).ToArray();
        var reservedDailyBudget = budgetOwners.Sum(ad => ad.DailyBudget);
        var batch = new BudgetReservationBatch(projectId, envelope.Id,
            budgetOwners.Select(ad => new BudgetReservationItem(ad.Id, BudgetPurpose.Winner, ad.DailyBudget)).ToArray());
        var reservation = await budgets.ReserveBatchAsync(db, batch, cancellationToken);
        if (!reservation.Reserved)
        {
            db.ChangeTracker.Clear();
            throw new AdvertisingException(reservation.Code, $"The selected ads need more daily budget. Available amount: {reservation.Available}.", 409);
        }
        AdvertisingAudit.Add(db, projectId, "ExistingFacebookAdsImported", "Project", projectId,
            new { importedAds = imported.Ads.Count, campaigns = imported.Promotions.Count, reservedDailyBudget });
        await db.SaveChangesAsync(cancellationToken);
        return new(imported.Ads.Count, selected.Count(candidate => candidate.AlreadyManaged), reservedDailyBudget);
    }

    private static ImportedEntities BuildManagedEntities(Guid projectId, AutonomyEnvelope envelope, IReadOnlyCollection<MetaExistingAd> providerAds)
    {
        var promotions = providerAds.GroupBy(ad => ad.CampaignId).ToDictionary(group => group.Key, group => new AdvertisingPromotion
        {
            ProjectId = projectId, EnvelopeId = envelope.Id, OfferId = envelope.OfferId ?? Guid.Empty, Name = group.First().CampaignName,
            Objective = group.First().Objective, OptimizationEvent = "ImportedFromMeta", State = group.Any(ad => ad.EffectiveStatus == "ACTIVE") ? PromotionState.Active : PromotionState.Paused,
            ReadinessJson = JsonSerializer.Serialize(new { source = "ExistingMetaCampaign", importedAtUtc = DateTime.UtcNow })
        });
        var creatives = new List<AdvertisingCreative>();
        var managedAds = new List<ManagedAdvertisement>();
        foreach (var providerAd in providerAds)
        {
            var creative = new AdvertisingCreative
            {
                ProjectId = projectId, SourceType = CreativeSourceType.ExistingPagePost, SourceExternalId = providerAd.ObjectStoryId,
                SourceHash = Hash($"{projectId}:{providerAd.AdId}:{providerAd.ObjectStoryId}"), MediaType = CreativeMediaType.Image,
                RightsState = "ExistingAdAccountCreative", PolicyState = "ProviderApproved", EligibilityState = CreativeEligibility.Eligible
            };
            creatives.Add(creative);
            managedAds.Add(new ManagedAdvertisement
            {
                ProjectId = projectId, PromotionId = promotions[providerAd.CampaignId].Id, CreativeId = creative.Id, Name = providerAd.AdName,
                CampaignExternalId = providerAd.CampaignId, AdSetExternalId = providerAd.AdSetId, AdExternalId = providerAd.AdId,
                BudgetOwnerExternalId = providerAd.BudgetOwnerId, BudgetOwnerType = providerAd.BudgetOwnerType,
                PublisherPlatform = "facebook", ManagementSource = "ImportedFromMeta",
                PositionsJson = JsonSerializer.Serialize(ManagedPositions(providerAd)),
                DailyBudget = providerAd.DailyBudget, ConfiguredStatus = providerAd.EffectiveStatus == "ACTIVE" ? ManagedDeliveryState.Active : ManagedDeliveryState.Paused,
                EffectiveStatus = providerAd.EffectiveStatus, LastSyncedAtUtc = DateTime.UtcNow, ImportedAtUtc = DateTime.UtcNow
            });
        }
        return new(promotions.Values.ToArray(), creatives, managedAds);
    }

    private async Task<(AdvertisingConnection Connection, string Token)> FacebookConnectionAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.State == AdvertisingConnectionState.Ready, cancellationToken)
            ?? throw new AdvertisingException("ADS_CONNECTION_NOT_READY", "Connect the Facebook ad account first.", 409);
        if (connection.AdAccountExternalId is null) throw new AdvertisingException("ADS_AD_ACCOUNT_REQUIRED", "Select a Facebook ad account first.", 409);
        return (connection, connection.ProtectedAccessToken is null ? "mock" : vault.Unprotect(connection.ProtectedAccessToken));
    }

    private static string? IneligibleReason(MetaExistingAd ad)
    {
        if (!ad.IsFacebookOnly) return "الحملة تستخدم مواضع غير Facebook أو مواضع غير مدعومة.";
        if (ad.DailyBudget <= 0) return "تعذّر تحديد الميزانية اليومية من Ad Set أو Campaign.";
        return null;
    }

    private static IReadOnlyList<string> ManagedPositions(MetaExistingAd providerAd) =>
        providerAd.FacebookPositions.Count > 0 ? providerAd.FacebookPositions :
        providerAd.Destination == "WhatsApp" ? ["feed"] : [];

    private static string Hash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    private sealed record ImportedEntities(IReadOnlyCollection<AdvertisingPromotion> Promotions, IReadOnlyCollection<AdvertisingCreative> Creatives, IReadOnlyCollection<ManagedAdvertisement> Ads);
}
