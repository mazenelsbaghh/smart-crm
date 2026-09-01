using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Shared.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

/// <summary>
/// Records a real inbound Gateway conversation as an unqualified lead. Attribution stays
/// separate: a missing/opaque referral never becomes an ad-attributed lead.
/// </summary>
public sealed class GatewayLeadObservationConsumer(AppDbContext db, ConversionLedgerService ledger) :
    IIntegrationEventHandler<WhatsAppAttributionObserved>
{
    private const string Consumer = nameof(GatewayLeadObservationConsumer);

    public async Task HandleAsync(WhatsAppAttributionObserved message)
    {
        if (await db.IntegrationInboxReceipts.AnyAsync(item => item.EventId == message.Id && item.Consumer == Consumer)) return;

        if (message.IsFirstConversationMessage
            && message.GatewayType.StartsWith("Baileys", StringComparison.OrdinalIgnoreCase))
        {
            await ledger.RecordAsync(new(message.Id, message.ProjectId, "WhatsAppGateway",
                message.ConversationId.ToString("N"), "Lead", message.MessageOccurredAtUtc,
                message.CustomerId.ToString("N"), null, null));
        }

        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt
        {
            ProjectId = message.ProjectId, EventId = message.Id, Consumer = Consumer,
            State = "Processed", ProcessedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
