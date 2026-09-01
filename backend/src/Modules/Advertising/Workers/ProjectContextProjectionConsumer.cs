using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class ProjectContextProjectionConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<ProjectAdvertisingContextChanged>(db),
    IIntegrationEventHandler<ProjectAdvertisingContextChanged>
{
    protected override string ConsumerName => nameof(ProjectContextProjectionConsumer);

    public Task HandleAsync(ProjectAdvertisingContextChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(message.ReportingTimezoneIana); }
        catch (TimeZoneNotFoundException) { throw new IntegrationProjectionValidationException("REPORTING_TIMEZONE_INVALID"); }
        catch (InvalidTimeZoneException) { throw new IntegrationProjectionValidationException("REPORTING_TIMEZONE_INVALID"); }
        var projection = await Db.ProjectAdvertisingContextProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId, cancellationToken);
        projection ??= new ProjectAdvertisingContextProjection { ProjectId = message.ProjectId };
        if (projection.Id != Guid.Empty && Db.Entry(projection).State == EntityState.Detached)
            Db.ProjectAdvertisingContextProjections.Add(projection);
        projection.LifecycleState = message.LifecycleState;
        projection.ReportingTimezoneIana = message.ReportingTimezoneIana;
        projection.AiConfigurationVersion = Math.Max(projection.AiConfigurationVersion, message.AiConfigurationVersion);
        projection.SourceVersion = message.SourceVersion;
        projection.UpdatedFromEventId = message.Id;
        projection.UpdatedAtUtc = DateTime.UtcNow;
    });

}

public sealed class ProjectAiConfigurationProjectionConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<ProjectAiConfigurationChanged>(db),
    IIntegrationEventHandler<ProjectAiConfigurationChanged>
{
    protected override string ConsumerName => nameof(ProjectAiConfigurationProjectionConsumer);

    public Task HandleAsync(ProjectAiConfigurationChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        var projection = await Db.ProjectAdvertisingContextProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId, cancellationToken);
        if (projection is null)
        {
            projection = new ProjectAdvertisingContextProjection { ProjectId = message.ProjectId };
            Db.ProjectAdvertisingContextProjections.Add(projection);
        }
        projection.AiConfigurationVersion = message.ConfigurationVersion;
        projection.AllowedAiModel = message.AllowedModel;
        projection.AiSettingsHash = message.SettingsHash;
        projection.UpdatedFromEventId = message.Id;
        projection.UpdatedAtUtc = DateTime.UtcNow;
    });
}

public sealed class OfferAvailabilityProjectionConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<OfferAvailabilityChanged>(db),
    IIntegrationEventHandler<OfferAvailabilityChanged>
{
    protected override string ConsumerName => nameof(OfferAvailabilityProjectionConsumer);

    public Task HandleAsync(OfferAvailabilityChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        var offer = await Db.AdvertisingOffers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId && x.Id == message.OfferId, cancellationToken);
        if (offer is null) throw new IntegrationProjectionValidationException("OFFER_NOT_FOUND");
        offer.DailyCapacity = message.DailyCapacity;
        offer.CurrentCapacity = message.CurrentCapacity;
        offer.CapacityUpdatedAtUtc = message.EffectiveAtUtc;
        if (message.IsTombstone || message.AvailabilityState != "Available") offer.State = "Unavailable";
        else if (offer.State == "Unavailable") offer.State = "Eligible";
    });
}
