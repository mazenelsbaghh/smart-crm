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

public sealed partial class KnowledgeProjectionConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<AdvertisingKnowledgeChanged>(db),
    IIntegrationEventHandler<KnowledgePublishedChangedEvent>,
    IIntegrationEventHandler<AdvertisingKnowledgeChanged>
{
    protected override string ConsumerName => nameof(KnowledgeProjectionConsumer);

    public Task HandleAsync(AdvertisingKnowledgeChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        var projection = await Db.AdvertisingKnowledgeProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId && x.DocumentId == message.KnowledgeDocumentId, cancellationToken);
        if (projection is null)
        {
            projection = new AdvertisingKnowledgeProjection { ProjectId = message.ProjectId, DocumentId = message.KnowledgeDocumentId };
            Db.AdvertisingKnowledgeProjections.Add(projection);
        }
        projection.DocumentVersion = message.SourceVersion;
        projection.RevisionHash = message.RevisionHash;
        projection.State = message.State;
        projection.SafeFactsJson = message.SafeFactsJson;
        projection.AffectedOfferKeysJson = message.AffectedOfferKeysJson;
        projection.UpdatedFromEventId = message.Id;
        projection.IsTombstoned = message.IsTombstone;
        var profiles = await Db.AdvertisingProfiles.IgnoreQueryFilters()
            .Where(x => x.ProjectId == message.ProjectId && x.StaleAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            profile.Status = "Stale";
            profile.StaleAtUtc = DateTime.UtcNow;
        }
        if (!message.IsTombstone && message.State is "Published" or "Approved")
        {
            var facts = JsonSerializer.Deserialize<SafeKnowledgeFacts>(message.SafeFactsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (facts is not null)
            {
                var ready = facts.Confidence >= 0.8m && !string.IsNullOrWhiteSpace(facts.DestinationIntent) &&
                            (facts.Price is not null || facts.OfferType is "Service" or "Event");
                var profile = new AdvertisingProfile
                {
                    ProjectId = message.ProjectId, KnowledgeRevisionHash = message.RevisionHash,
                    Status = ready ? "Ready" : "Blocked", OfferType = facts.OfferType,
                    FunnelJson = JsonSerializer.Serialize(AdvertisingProfileExtractor.Funnel(facts.OfferType)),
                    GeneratedAtUtc = DateTime.UtcNow
                };
                var name = JsonSerializer.Deserialize<string[]>(message.AffectedOfferKeysJson)?.FirstOrDefault() ?? "WhatsApp Offer";
                var offer = new AdvertisingOffer
                {
                    ProjectId = message.ProjectId, ProfileId = profile.Id, Name = name, Type = facts.OfferType,
                    Price = facts.Price, Currency = facts.Currency, DestinationsJson = facts.DestinationIntent == "WhatsApp" ? "[\"WhatsApp\"]" : "[]",
                    PrimaryOutcome = facts.OfferType == "Course" ? "EnrollmentPaid" : "QualifiedLead",
                    State = ready ? "Eligible" : "Blocked", PolicyState = "PendingClassification"
                };
                Db.AdvertisingProfiles.Add(profile);
                Db.AdvertisingOffers.Add(offer);
                var citation = $"knowledge:{message.KnowledgeDocumentId:N}:v{message.SourceVersion}";
                var extracted = new Dictionary<string, string?>
                {
                    ["OfferType"] = facts.OfferType, ["Price"] = facts.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Currency"] = facts.Currency, ["Destination"] = facts.DestinationIntent
                };
                foreach (var fact in extracted.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
                    Db.AdvertisingFactSources.Add(new AdvertisingFactSource
                    {
                        ProjectId = message.ProjectId, ProfileId = profile.Id, OfferId = offer.Id,
                        FactName = fact.Key, FactValue = fact.Value!, KnowledgeDocumentId = message.KnowledgeDocumentId,
                        KnowledgeVersion = checked((int)message.SourceVersion), SourceVersion = message.RevisionHash,
                        Confidence = facts.Confidence, ObservedAtUtc = message.OccurredOn, IsRequiredForLaunch = true, Citation = citation
                    });
            }
        }
    });

    public async Task HandleAsync(KnowledgePublishedChangedEvent @event)
    {
        var isPublished = @event.Status is "Published" or "Approved";
        var profiles = await Db.AdvertisingProfiles.IgnoreQueryFilters().Where(x => x.ProjectId == @event.ProjectId).ToListAsync();
        foreach (var old in profiles) { old.Status = "Stale"; old.StaleAtUtc = DateTime.UtcNow; }
        if (!isPublished) { await Db.SaveChangesAsync(); return; }

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
        Db.AdvertisingProfiles.Add(profile); Db.AdvertisingOffers.Add(offer);
        foreach (var fact in new Dictionary<string, string?> { ["OfferType"] = extracted.OfferType, ["Price"] = extracted.Price?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Currency"] = extracted.Currency, ["Destination"] = extracted.Destination })
            if (!string.IsNullOrWhiteSpace(fact.Value)) Db.AdvertisingFactSources.Add(new AdvertisingFactSource
            {
                ProjectId = @event.ProjectId, ProfileId = profile.Id, OfferId = offer.Id, FactName = fact.Key,
                FactValue = fact.Value, KnowledgeDocumentId = @event.DocumentId, KnowledgeVersion = @event.Version,
                Citation = extracted.SourceCitations[0]
            });
        await Db.SaveChangesAsync();
    }

}

internal sealed record SafeKnowledgeFacts(string OfferType, decimal? Price, string? Currency, string? DestinationIntent, decimal Confidence);
