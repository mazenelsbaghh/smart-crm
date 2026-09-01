using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class ConsentProjectionConsumer(AppDbContext db) :
    IntegrationProjectionConsumer<CustomerAdvertisingConsentChanged>(db),
    IIntegrationEventHandler<CustomerAdvertisingConsentChanged>
{
    protected override string ConsumerName => nameof(ConsentProjectionConsumer);

    public Task HandleAsync(CustomerAdvertisingConsentChanged message) => ConsumeAsync(message, async cancellationToken =>
    {
        var projection = await Db.CustomerAdvertisingConsentProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId && x.CustomerId == message.CustomerId, cancellationToken);
        if (projection is null)
        {
            projection = new CustomerAdvertisingConsentProjection { ProjectId = message.ProjectId, CustomerId = message.CustomerId };
            Db.CustomerAdvertisingConsentProjections.Add(projection);
        }
        projection.ConsentVersion = message.SourceVersion;
        projection.ConsentState = message.ConsentState;
        projection.LegalBasis = message.LegalBasis;
        projection.EffectiveAtUtc = message.EffectiveAtUtc;
        projection.UpdatedFromEventId = message.Id;
        projection.IsTombstoned = message.IsTombstone;
    });
}
