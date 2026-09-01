using System;
using System.Threading.Tasks;

namespace Modules.Conversations.Services
{
    public interface IMessageAggregator
    {
        Task AggregateMessageAsync(
            Guid projectId,
            string sender,
            string content,
            Guid incomingMessageId,
            DateTime? sourceMessageTimestampUtc = null,
            DateTimeOffset? requiredWhatsAppConnectedAt = null,
            Guid? conversationId = null,
            Guid? whatsAppAccountId = null,
            CancellationToken cancellationToken = default,
            string channel = "WhatsApp",
            string? channelMetadata = null);
    }
}
