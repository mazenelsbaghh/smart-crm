using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Facebook.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Facebook.Workers
{
    public class FacebookReplySender : IIntegrationEventHandler<AIReplyGeneratedEvent>
    {
        private readonly AppDbContext _context;
        private readonly IFacebookGraphService _graphService;
        private readonly ILogger<FacebookReplySender> _logger;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> _hubContext;

        public FacebookReplySender(
            AppDbContext context,
            IFacebookGraphService graphService,
            ILogger<FacebookReplySender> logger,
            Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> hubContext)
        {
            _context = context;
            _graphService = graphService;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task HandleAsync(AIReplyGeneratedEvent @event)
        {
            // Only handle Facebook channels
            var channel = @event.Channel ?? "WhatsApp";
            if (channel != "Messenger" && channel != "FacebookComment")
                return;

            _logger.LogInformation("[FacebookReplySender] Handling AI reply for channel {Channel}, sender {Sender}", channel, @event.Sender);

            try
            {
                // Find connected page for this project
                var connectedPage = await _context.ConnectedPages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(cp => cp.ProjectId == @event.ProjectId && cp.IsActive);

                if (connectedPage == null)
                {
                    _logger.LogWarning("[FacebookReplySender] No active connected page for project {ProjectId}", @event.ProjectId);
                    return;
                }

                if (channel == "Messenger")
                {
                    await HandleMessengerReply(connectedPage, @event);
                }
                else if (channel == "FacebookComment")
                {
                    await HandleCommentReply(connectedPage, @event);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FacebookReplySender] Error handling reply for channel {Channel}", channel);
            }
        }

        private async Task HandleMessengerReply(Modules.Facebook.Domain.ConnectedPage connectedPage, AIReplyGeneratedEvent @event)
        {
            // Simulate typing delay
            await Task.Delay(2000);

            // Send via Graph API
            await _graphService.SendMessageAsync(
                connectedPage.FacebookPageId,
                connectedPage.PageAccessToken,
                @event.Sender,
                @event.Content);

            // Find the conversation and save outgoing message
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.FacebookPSID == @event.Sender);

            if (customer != null)
            {
                var conversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger"
                        && (c.Status == "Open" || c.Status == "Pending"));

                if (conversation != null)
                {
                    var msg = new Modules.Conversations.Domain.Message
                    {
                        ConversationId = conversation.Id,
                        Direction = "Outgoing",
                        Content = @event.Content,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(msg);
                    await _context.SaveChangesAsync();

                    // Broadcast via SignalR
                    await _hubContext.Clients.Group(@event.ProjectId.ToString()).SendAsync("ReceiveMessage", new
                    {
                        id = msg.Id,
                        conversationId = conversation.Id,
                        senderType = "AI",
                        content = @event.Content,
                        createdAt = msg.Timestamp,
                        status = "Sent",
                        channel = "Messenger"
                    });
                }
            }

            _logger.LogInformation("[FacebookReplySender] Messenger reply sent to {Sender}", @event.Sender);
        }

        private async Task HandleCommentReply(Modules.Facebook.Domain.ConnectedPage connectedPage, AIReplyGeneratedEvent @event)
        {
            // Parse channel metadata for comment details
            string commentId = null;
            if (!string.IsNullOrEmpty(@event.ChannelMetadata))
            {
                try
                {
                    using var doc = JsonDocument.Parse(@event.ChannelMetadata);
                    commentId = doc.RootElement.TryGetProperty("commentId", out var cid) ? cid.GetString() : null;
                }
                catch { /* ignore parse errors */ }
            }

            if (string.IsNullOrEmpty(commentId))
            {
                _logger.LogWarning("[FacebookReplySender] No commentId in metadata, skipping comment reply");
                return;
            }

            // 1. Reply publicly to the comment
            await _graphService.ReplyToCommentAsync(connectedPage.PageAccessToken, commentId, @event.Content);

            // 2. React to the comment (auto-like)
            await _graphService.ReactToCommentAsync(connectedPage.PageAccessToken, commentId, "LIKE");

            // 3. Send private DM
            await _graphService.SendPrivateReplyAsync(
                connectedPage.FacebookPageId,
                connectedPage.PageAccessToken,
                commentId,
                @event.Content);

            _logger.LogInformation("[FacebookReplySender] Comment reply (public+DM+reaction) sent for comment {CommentId}", commentId);
        }
    }
}
