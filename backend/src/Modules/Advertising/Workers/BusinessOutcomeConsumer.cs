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

    public Task HandleAsync(AdvertisingDealOutcomeChanged e) => Once(e.ProjectId, e.Id, async () =>
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "CRM", e.DealId.ToString("N"), e.Outcome == "Won" ? "DealWon" : "DealLost",
            e.OccurredOn, e.CustomerId.ToString("N"), e.Value, e.Currency, e.Outcome != "Won", "Deal marked lost")));

    public Task HandleAsync(AdvertisingBookingOutcomeChanged e) => Once(e.ProjectId, e.Id, async () =>
    {
        var type = e.IsAttended ? "AttendanceConfirmed" : e.IsPaid ? "EnrollmentPaid" : "BookingConfirmed";
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "Booking", e.BookingId.ToString("N"), type, e.OccurredOn.ToUniversalTime(),
            e.CustomerId.ToString("N"), e.Value, e.Currency));
    });

    public Task HandleAsync(AdvertisingQualifiedMessageChanged e) => Once(e.ProjectId, e.Id, async () =>
    {
        var qualifiedIntent = new[] { "Qualified", "BookingIntent", "PurchaseIntent" }
            .Contains(e.Classification, StringComparer.OrdinalIgnoreCase);
        if (!qualifiedIntent || e.Confidence < 0.80m) return;
        await ledger.RecordAsync(new(e.Id, e.ProjectId, "Conversation", e.ConversationId.ToString("N"), "QualifiedLead", e.OccurredOn,
            e.CustomerId.ToString("N"), null, null));
    });

    private async Task Once(Guid projectId, Guid eventId, Func<Task> handler)
    {
        if (await db.IntegrationInboxReceipts.AnyAsync(x => x.EventId == eventId && x.Consumer == Consumer)) return;
        await handler();
        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt
        {
            ProjectId = projectId, EventId = eventId, Consumer = Consumer, State = "Processed", ProcessedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
