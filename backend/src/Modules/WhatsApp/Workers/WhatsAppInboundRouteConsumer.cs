using Microsoft.EntityFrameworkCore;
using Modules.WhatsApp.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.WhatsApp.Workers;

public sealed class WhatsAppInboundRouteConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<AdvertisingWhatsAppDestinationChanged>(db),
    IIntegrationEventHandler<AdvertisingWhatsAppDestinationChanged>
{
    protected override string ConsumerName => nameof(WhatsAppInboundRouteConsumer);

    public Task HandleAsync(AdvertisingWhatsAppDestinationChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        var route = await Db.WhatsAppInboundRouteProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId && x.DestinationId == message.DestinationId, cancellationToken);
        if (route is null)
        {
            route = new WhatsAppInboundRouteProjection { ProjectId = message.ProjectId, DestinationId = message.DestinationId };
            Db.WhatsAppInboundRouteProjections.Add(route);
        }
        route.DestinationVersion = message.DestinationVersion;
        route.Provider = message.Provider;
        route.WabaExternalId = message.WabaExternalId;
        route.PhoneNumberExternalId = message.PhoneNumberExternalId;
        route.IntegrationMode = message.IntegrationMode;
        route.State = message.IsTombstone ? "Revoked" : message.State;
        route.SourceEventId = message.Id;
        route.SourceAggregateVersion = message.SourceVersion;
        route.UpdatedAtUtc = DateTime.UtcNow;
    });
}
