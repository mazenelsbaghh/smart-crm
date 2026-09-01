using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Shared.Infrastructure;

namespace Modules.WhatsApp.Services;

/// <summary>Resolves one active WhatsApp conversation slot per project/customer/account.</summary>
public sealed class WhatsAppConversationService(AppDbContext dbContext)
{
    public async Task<Conversation> ResolveOrCreateAsync(
        Guid projectId,
        Guid customerId,
        Guid whatsAppAccountId,
        DateTime lastMessageTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var active = await dbContext.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.ProjectId == projectId
                && conversation.CustomerId == customerId
                && conversation.Channel == "WhatsApp"
                && conversation.WhatsAppAccountId == whatsAppAccountId
                && conversation.Status != "Closed")
            .OrderByDescending(conversation => conversation.LastMessageTimestamp)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
        {
            if (lastMessageTimestampUtc > active.LastMessageTimestamp)
                active.LastMessageTimestamp = lastMessageTimestampUtc;
            return active;
        }

        var id = DeterministicId(
            $"whatsapp-conversation:{projectId:N}:{customerId:N}:{whatsAppAccountId:N}");
        var stable = await dbContext.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);
        if (stable is not null)
        {
            stable.CustomerId = customerId;
            stable.WhatsAppAccountId = whatsAppAccountId;
            stable.Status = "Open";
            if (lastMessageTimestampUtc > stable.LastMessageTimestamp)
                stable.LastMessageTimestamp = lastMessageTimestampUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return stable;
        }

        stable = new Conversation
        {
            Id = id,
            ProjectId = projectId,
            CustomerId = customerId,
            WhatsAppAccountId = whatsAppAccountId,
            Channel = "WhatsApp",
            Status = "Open",
            LastMessageTimestamp = lastMessageTimestampUtc
        };
        dbContext.Conversations.Add(stable);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return stable;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(stable).State = EntityState.Detached;
            return await dbContext.Conversations.IgnoreQueryFilters()
                .FirstAsync(conversation => conversation.Id == id, cancellationToken);
        }
    }

    private static Guid DeterministicId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
