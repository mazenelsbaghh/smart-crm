using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record AttributionResolution(AttributionState State, Guid? TouchId, Guid? AdvertisementId, string Method);

public sealed class AdvertisingAttributionService(AppDbContext db)
{
    public async Task<AttributionResolution> ResolveAsync(Guid projectId, Guid conversionId, int windowDays,
        CancellationToken cancellationToken = default)
    {
        var conversion = await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == projectId && item.Id == conversionId, cancellationToken);
        var journeyKey = conversion.CustomerReference;
        if (string.IsNullOrWhiteSpace(journeyKey))
        {
            var result = Unattributed(conversion); await db.SaveChangesAsync(cancellationToken); return result;
        }
        var earliest = conversion.OccurredAtUtc.AddDays(-windowDays);
        var touch = await db.AdvertisingAttributionTouches.IgnoreQueryFilters().Where(item => item.ProjectId == projectId
            && item.JourneyKey == journeyKey && item.TouchedAtUtc >= earliest && item.TouchedAtUtc <= conversion.OccurredAtUtc
            && item.ProtectedCtwaClid != null).OrderByDescending(item => item.TouchedAtUtc).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (touch is null)
        {
            var result = Unattributed(conversion); await db.SaveChangesAsync(cancellationToken); return result;
        }
        var advertisement = string.IsNullOrWhiteSpace(touch.ProviderAdExternalId) ? null
            : await db.ManagedAdvertisements.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.ProjectId == projectId
                && item.AdExternalId == touch.ProviderAdExternalId, cancellationToken);
        touch.ConversionId = conversion.Id;
        conversion.AttributionTouchId = touch.Id; conversion.AttributionState = AttributionState.Attributed;
        conversion.AttributionMethod = "WhatsAppCtwaClid"; conversion.AdvertisementId = advertisement?.Id;
        conversion.CreativeId = advertisement?.CreativeId; conversion.AttributionWindowEndsAtUtc = touch.TouchedAtUtc.AddDays(windowDays);
        await db.SaveChangesAsync(cancellationToken);
        return new(AttributionState.Attributed, touch.Id, advertisement?.Id, conversion.AttributionMethod);
    }

    private static AttributionResolution Unattributed(CanonicalConversion conversion)
    {
        conversion.AttributionState = AttributionState.Unattributed;
        conversion.AttributionMethod = "Unattributed";
        return new(AttributionState.Unattributed, null, null, "Unattributed");
    }
}
