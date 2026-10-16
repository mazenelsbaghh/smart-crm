using System.Net;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Npgsql;
using Shared.Domain;
using Shared.Events;
using Shared.Infrastructure;

namespace Modules.Facebook.Workers;

public sealed class MessengerDeliveryReceipt(AppDbContext db)
{
    public async Task<IntegrationInboxReceipt?> ClaimAsync(AIReplyGeneratedEvent reply, int partIndex, Guid? conversationId)
    {
        var consumer = $"MessengerReply:{partIndex}";
        var receipt = await db.IntegrationInboxReceipts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.EventId == reply.Id && row.Consumer == consumer);
        if (receipt is null)
        {
            receipt = new IntegrationInboxReceipt { ProjectId = reply.ProjectId, EventId = reply.Id,
                Consumer = consumer, State = "Ready", SourceAggregateType = "Conversation",
                SourceAggregateId = conversationId ?? Guid.Empty };
            db.Add(receipt);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(receipt).State = EntityState.Detached;
                throw new InvalidOperationException("Another worker claimed this Messenger part.", exception);
            }
        }
        if (receipt.State == "Sent") return null;
        if (receipt.State == "Unknown" || receipt.State == "Sending" && receipt.ReceivedAtUtc < DateTime.UtcNow.AddMinutes(-2))
        {
            await RequireReviewAsync(receipt);
            return receipt;
        }
        if (receipt.State != "Ready") throw new InvalidOperationException("Messenger delivery is still in progress.");
        var startedAt = DateTime.UtcNow;
        if (db.Database.IsRelational() && await db.IntegrationInboxReceipts.IgnoreQueryFilters()
                .Where(row => row.Id == receipt.Id && row.State == "Ready")
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.State, "Sending")
                    .SetProperty(row => row.ReceivedAtUtc, startedAt)) != 1)
            throw new InvalidOperationException("Another worker claimed this Messenger part.");
        receipt.State = "Sending";
        receipt.ReceivedAtUtc = startedAt;
        await db.SaveChangesAsync();
        return receipt;
    }

    public async Task<bool> SendAsync(IntegrationInboxReceipt receipt, Func<Task> send)
    {
        try { await send(); }
        catch (HttpRequestException exception) when (exception.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            && exception.StatusCode != HttpStatusCode.RequestTimeout)
        {
            receipt.State = "Ready";
            await db.SaveChangesAsync();
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // A lost response cannot prove whether Meta delivered the part. Replaying could duplicate it.
            await RequireReviewAsync(receipt);
            return false;
        }
        receipt.State = "Sent";
        receipt.ProcessedAtUtc = DateTime.UtcNow;
        return true;
    }

    private async Task RequireReviewAsync(IntegrationInboxReceipt receipt)
    {
        receipt.State = "Unknown";
        receipt.FailureCode = "provider_delivery_unknown";
        var conversation = await db.Conversations.IgnoreQueryFilters().SingleOrDefaultAsync(row =>
            row.ProjectId == receipt.ProjectId && row.Id == receipt.SourceAggregateId);
        if (conversation is not null && conversation.HumanHandoffReplyId != receipt.Id)
        {
            conversation.Status = "Pending";
            conversation.HumanHandoffReplyId = receipt.Id;
            db.NotificationAlerts.Add(new NotificationAlert { ProjectId = receipt.ProjectId,
                UserId = conversation.AssignedUserId ?? Guid.Empty, Type = "HumanTransferRequest",
                Message = $"نتيجة إرسال رد ماسنجر في المحادثة {conversation.Id} غير مؤكدة. راجع المحادثة قبل إعادة الإرسال." });
        }
        await db.SaveChangesAsync();
    }
}
