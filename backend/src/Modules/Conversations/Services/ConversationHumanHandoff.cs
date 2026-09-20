using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Conversations.Services;

public sealed class ConversationHumanHandoff(AppDbContext dbContext)
{
    public async Task RequestAsync(Conversation conversation, AIReplyGeneratedEvent acknowledgement)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (!await TryPauseAsync(conversation, acknowledgement.Id)) return;

        acknowledgement.IsHumanHandoffAcknowledgement = true;
        conversation.HumanHandoffReplyId = acknowledgement.Id;
        conversation.Status = "Pending";
        var customerName = await dbContext.Customers.IgnoreQueryFilters()
            .Where(customer => customer.Id == conversation.CustomerId && customer.ProjectId == conversation.ProjectId)
            .Select(customer => customer.Name).SingleAsync();
        dbContext.NotificationAlerts.Add(new NotificationAlert
        {
            ProjectId = conversation.ProjectId,
            UserId = conversation.AssignedUserId ?? Guid.Empty,
            Type = "HumanTransferRequest",
            Message = $"محادثة العميل {customerName} ({conversation.Id}) محتاجة متابعة من موظف. الرد الآلي متوقف لحين إعادة فتح المحادثة."
        });
        IntegrationOutbox.Enqueue(dbContext, acknowledgement);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<bool> TryPauseAsync(Conversation conversation, Guid acknowledgementId) =>
        await dbContext.Conversations.IgnoreQueryFilters()
            .Where(candidate => candidate.Id == conversation.Id && candidate.ProjectId == conversation.ProjectId
                && candidate.HumanHandoffReplyId == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(candidate => candidate.HumanHandoffReplyId, acknowledgementId)
                .SetProperty(candidate => candidate.Status, "Pending")) == 1;

    public static bool BlocksReply(Conversation conversation, AIReplyGeneratedEvent reply) =>
        (reply.IsHumanHandoffAcknowledgement || conversation.HumanHandoffReplyId.HasValue)
            && conversation.HumanHandoffReplyId != reply.Id;

    public async Task<bool> BlocksReplyAsync(AIReplyGeneratedEvent reply)
    {
        var conversations = dbContext.Conversations.IgnoreQueryFilters().AsNoTracking()
            .Where(conversation => conversation.ProjectId == reply.ProjectId && conversation.Channel == reply.Channel);
        if (reply.ConversationId is { } conversationId)
            conversations = conversations.Where(conversation => conversation.Id == conversationId);
        else
            conversations = conversations.Where(conversation => dbContext.Customers.IgnoreQueryFilters()
                .Any(customer => customer.Id == conversation.CustomerId && (reply.Channel == "WhatsApp"
                    ? customer.PhoneNumber == reply.Sender : customer.FacebookPSID == reply.Sender)));
        if (reply.Channel == "WhatsApp")
        {
            var accountId = reply.WhatsAppAccountId ?? reply.ProjectId;
            conversations = conversations.Where(conversation => (conversation.WhatsAppAccountId ?? conversation.ProjectId) == accountId);
        }
        if (reply.IsHumanHandoffAcknowledgement)
            return !await conversations.AnyAsync(conversation => conversation.HumanHandoffReplyId == reply.Id);
        return await conversations.AnyAsync(conversation => conversation.HumanHandoffReplyId.HasValue
                && conversation.HumanHandoffReplyId != reply.Id);
    }
}
