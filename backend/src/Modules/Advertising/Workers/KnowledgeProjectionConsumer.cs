using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed partial class KnowledgeProjectionConsumer(AppDbContext db) : IIntegrationEventHandler<KnowledgePublishedChangedEvent>
{
    public async Task HandleAsync(KnowledgePublishedChangedEvent @event)
    {
        var isPublished = @event.Status is "Published" or "Approved";
        var profiles = await db.AdvertisingProfiles.IgnoreQueryFilters().Where(x => x.ProjectId == @event.ProjectId).ToListAsync();
        foreach (var old in profiles) { old.Status = "Stale"; old.StaleAtUtc = DateTime.UtcNow; }
        if (!isPublished) { await db.SaveChangesAsync(); return; }

        var content = @event.Content.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{@event.DocumentId}:{@event.Version}:{content}"))).ToLowerInvariant();
        var extracted = AdvertisingProfileExtractor.Extract(@event.DocumentId, @event.Version, content);
        var profile = new AdvertisingProfile
        {
            ProjectId = @event.ProjectId, KnowledgeRevisionHash = hash, Status = extracted.Eligible ? "Ready" : "Blocked", OfferType = extracted.OfferType,
            FunnelJson = JsonSerializer.Serialize(AdvertisingProfileExtractor.Funnel(extracted.OfferType)), AudienceJson = "{}", BrandRulesJson = "{}", ProhibitedClaimsJson = "[]", GeneratedAtUtc = DateTime.UtcNow
        };
        var offer = new AdvertisingOffer
        {
            ProjectId = @event.ProjectId, ProfileId = profile.Id, Name = @event.Title, Type = extracted.OfferType, Price = extracted.Price, Currency = extracted.Currency,
            DestinationsJson = extracted.Destination is null ? "[]" : JsonSerializer.Serialize(new[] { extracted.Destination }),
            MarketsJson = content.Contains("مصر", StringComparison.OrdinalIgnoreCase) ? "[\"EG\"]" : "[]",
            State = extracted.Eligible ? "Eligible" : "Blocked"
        };
        db.AdvertisingProfiles.Add(profile); db.AdvertisingOffers.Add(offer);
        foreach (var fact in new Dictionary<string, string?> { ["OfferType"] = extracted.OfferType, ["Price"] = extracted.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Currency"] = extracted.Currency, ["Destination"] = extracted.Destination })
            if (!string.IsNullOrWhiteSpace(fact.Value)) db.AdvertisingFactSources.Add(new AdvertisingFactSource
            {
                ProjectId = @event.ProjectId, ProfileId = profile.Id, OfferId = offer.Id, FactName = fact.Key,
                FactValue = fact.Value, KnowledgeDocumentId = @event.DocumentId, KnowledgeVersion = @event.Version,
                Citation = extracted.SourceCitations[0]
            });
        await db.SaveChangesAsync();
    }

}
