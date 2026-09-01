using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AdvertisingOwnershipPolicy(AppDbContext db)
{
    public async Task<List<ManagedAdvertisement>> ManagedAdsAsync(Guid projectId,
        bool activeOnly, CancellationToken cancellationToken = default) =>
        await ManagedAds(projectId, activeOnly).ToListAsync(cancellationToken);

    public Task<bool> IsManagedAsync(Guid projectId, Guid adId, CancellationToken cancellationToken = default) =>
        ManagedAds(projectId, activeOnly: false).AnyAsync(item => item.Id == adId, cancellationToken);

    private IQueryable<ManagedAdvertisement> ManagedAds(Guid projectId, bool activeOnly)
    {
        var ownershipIds = db.AdvertisingManagedOwnership.IgnoreQueryFilters().Where(item =>
            item.ProjectId == projectId && item.RevokedAtUtc == null
            && (item.OwnershipKind == ManagedOwnershipKind.AutopilotCreated
                || item.OwnershipKind == ManagedOwnershipKind.ImportedWithAuthority))
            .Select(item => item.Id);
        return db.ManagedAdvertisements.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.OwnershipRecordId != null && ownershipIds.Contains(item.OwnershipRecordId.Value)
            && item.EffectiveStatus != "DELETED" && item.EffectiveStatus != "ARCHIVED"
            && item.ConfiguredStatus != ManagedDeliveryState.Archived
            && (!activeOnly || item.ConfiguredStatus == ManagedDeliveryState.Active
                || item.EffectiveStatus == "ACTIVE"));
    }
}
