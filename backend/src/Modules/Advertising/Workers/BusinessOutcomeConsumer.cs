using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Shared.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class BusinessOutcomeConsumer(AppDbContext db, ConversionLedgerService ledger) :
    IIntegrationEventHandler<AdvertisingDealOutcomeChanged>,
    IIntegrationEventHandler<AdvertisingBookingOutcomeChanged>,
    IIntegrationEventHandler<AdvertisingQualifiedMessageChanged>
{
    private const string Consumer = nameof(BusinessOutcomeConsumer);

    public Task HandleAsync(AdvertisingDealOutcomeChanged e) => Once(e.Id, async () =>
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "CRM", e.DealId.ToString("N"), e.Outcome == "Won" ? "DealWon" : "DealLost",
            e.OccurredOn, e.CustomerId.ToString("N"), e.Value, e.Currency, e.Outcome != "Won", "Deal marked lost")));

    public Task HandleAsync(AdvertisingBookingOutcomeChanged e) => Once(e.Id, async () =>
    {
        var type = e.IsAttended ? "AttendanceConfirmed" : e.IsPaid ? "EnrollmentPaid" : "BookingConfirmed";
        var correction = !e.IsPaid && !e.IsAttended;
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "Booking", e.BookingId.ToString("N"), type, e.OccurredOn,
            e.CustomerId.ToString("N"), e.Value, e.Currency, correction, correction ? "Payment or attendance reversed" : null));
    });

    public Task HandleAsync(AdvertisingQualifiedMessageChanged e) => Once(e.Id, async () =>
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "Conversation", e.ConversationId.ToString("N"), "QualifiedLead", e.OccurredOn,
            e.CustomerId.ToString("N"), null, null)));

    private async Task Once(Guid eventId, Func<Task> handler)
    {
        if (await db.IntegrationInboxReceipts.AnyAsync(x => x.EventId == eventId && x.Consumer == Consumer)) return;
        await handler();
        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt { EventId = eventId, Consumer = Consumer, ProcessedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
