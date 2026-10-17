using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.GroupAppointments.Services;
using Shared.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.WhatsApp.Workers;

public sealed class BookingLastMessageReadConsumer(IBackgroundJobClient jobs)
    : IIntegrationEventHandler<BookingLastMessageReadRequested>
{
    public Task HandleAsync(BookingLastMessageReadRequested request)
    {
        jobs.Enqueue<BookingLastMessageReadJob>(job => job.ExecuteAsync(request));
        return Task.CompletedTask;
    }
}

public sealed class BookingLastMessageReadJob(AppDbContext db, IHttpClientFactory httpClients,
    IConfiguration configuration, WhatsAppGatewaySessionClient sessions)
{
    [AutomaticRetry(Attempts = 10)]
    public async Task ExecuteAsync(BookingLastMessageReadRequested request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lockIdentity = $"booking-message-read:{request.ProjectId:N}:{request.MessageId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))");
        if (await db.IntegrationInboxReceipts.IgnoreQueryFilters().AnyAsync(receipt =>
                receipt.ProjectId == request.ProjectId && receipt.Consumer == nameof(BookingLastMessageReadJob)
                && (receipt.EventId == request.Id
                    || (receipt.SourceAggregateId == request.MessageId && receipt.State == "Processed"))))
            return;
        var target = await FindTargetAsync(request);
        if (target is not null) await SendReadReceiptAsync(request.ProjectId, target);
        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt
        {
            ProjectId = request.ProjectId, EventId = request.Id, Consumer = nameof(BookingLastMessageReadJob),
            SourceAggregateType = "Message", SourceAggregateId = request.MessageId, SourceVersion = 1,
            State = target is null ? "Skipped" : "Processed", ProcessedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<ReadTarget?> FindTargetAsync(BookingLastMessageReadRequested request)
    {
        if (!await db.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(booking =>
                booking.Id == request.BookingId && booking.ProjectId == request.ProjectId
                && booking.CustomerId == request.CustomerId))
            return null;
        var incoming = await db.Messages.IgnoreQueryFilters()
            .Join(db.Conversations.IgnoreQueryFilters(), message => message.ConversationId, conversation => conversation.Id,
                (message, conversation) => new { Message = message, Conversation = conversation })
            .Join(db.Customers.IgnoreQueryFilters(), entry => entry.Conversation.CustomerId, customer => customer.Id,
                (entry, customer) => new { entry.Message, entry.Conversation, Customer = customer })
            .Where(entry => entry.Message.Id == request.MessageId && entry.Message.Direction == "Incoming"
                && entry.Conversation.Id == request.ConversationId && entry.Conversation.ProjectId == request.ProjectId
                && entry.Customer.ProjectId == request.ProjectId && entry.Customer.Id == request.CustomerId
                && entry.Conversation.Channel == "WhatsApp" && entry.Conversation.WhatsAppDestinationId == null
                && entry.Conversation.WhatsAppAccountId != null)
            .Select(entry => new { entry.Message.ExternalMessageId, entry.Message.WhatsAppRemoteJid,
                entry.Conversation.WhatsAppAccountId, entry.Customer.PhoneNumber, entry.Customer.WhatsAppLid })
            .SingleOrDefaultAsync();
        if (incoming is null) return null;
        var remoteJid = incoming.WhatsAppRemoteJid ?? incoming.WhatsAppLid
            ?? (GroupBookingPhone.Normalize(incoming.PhoneNumber) is { } phone ? phone + "@s.whatsapp.net" : null);
        return IsCustomerJid(remoteJid) && !string.IsNullOrWhiteSpace(incoming.ExternalMessageId)
            ? new(incoming.WhatsAppAccountId!.Value, incoming.ExternalMessageId, remoteJid!)
            : null;
    }

    private async Task SendReadReceiptAsync(Guid projectId, ReadTarget target)
    {
        var session = await sessions.GetAsync(projectId, target.AccountId);
        if (!session.Connected || !session.ConnectedAt.HasValue)
            throw new HttpRequestException("WHATSAPP_READ_RECEIPT_CONNECTION_UNAVAILABLE");
        var gateway = (configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000").TrimEnd('/');
        using var client = httpClients.CreateClient(nameof(BookingLastMessageReadJob));
        using var response = await client.PostAsJsonAsync(gateway + "/api/whatsapp/read", new
        {
            projectId, whatsappAccountId = target.AccountId, messageId = target.ExternalMessageId,
            remoteJid = target.RemoteJid, expectedConnectedAt = session.ConnectedAt
        });
        response.EnsureSuccessStatusCode();
    }

    private static bool IsCustomerJid(string? jid) =>
        jid is not null && Regex.IsMatch(jid, @"^[0-9]{5,20}@(s\.whatsapp\.net|lid)$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private sealed record ReadTarget(Guid AccountId, string ExternalMessageId, string RemoteJid);
}
