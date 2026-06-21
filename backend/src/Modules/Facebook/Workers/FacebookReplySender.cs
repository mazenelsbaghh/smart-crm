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
        private readonly StackExchange.Redis.IDatabase _redis;

        public FacebookReplySender(
            AppDbContext context,
            IFacebookGraphService graphService,
            ILogger<FacebookReplySender> logger,
            Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> hubContext,
            StackExchange.Redis.IConnectionMultiplexer redis)
        {
            _context = context;
            _graphService = graphService;
            _logger = logger;
            _hubContext = hubContext;
            _redis = redis.GetDatabase();
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
            // Find the conversation first to broadcast typing state
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.FacebookPSID == @event.Sender);

            Guid? conversationId = null;

            if (customer != null)
            {
                var conversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger"
                        && (c.Status == "Open" || c.Status == "Pending"));

                if (conversation != null)
                {
                    conversationId = conversation.Id;

                    // Transition typing stage from "generating" to "typing" (4 seconds)
                    var redisKey = $"ai_typing:{conversation.Id}";
                    try
                    {
                        await _redis.StringSetAsync(redisKey, "typing", TimeSpan.FromSeconds(30));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[FacebookReplySender] Redis set typing state failed: {Message}", ex.Message);
                    }

                    await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                    {
                        conversationId = conversation.Id,
                        isTyping = true,
                        estimatedSeconds = 4,
                        stage = "typing"
                    });
                }
            }

            // Simulate typing delay (4 seconds)
            await Task.Delay(4000);

            // Split the reply content into multiple messages by double newlines to send as separate messages
            var rawParts = @event.Content.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var p in rawParts)
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    parts.Add(trimmed);
                }
            }

            if (parts.Count == 0 && !string.IsNullOrEmpty(@event.Content))
            {
                parts.Add(@event.Content.Trim());
            }

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];

                // Send via Graph API
                await _graphService.SendMessageAsync(
                    connectedPage.FacebookPageId,
                    connectedPage.PageAccessToken,
                    @event.Sender,
                    part);

                if (customer != null && conversationId.HasValue)
                {
                    var msg = new Modules.Conversations.Domain.Message
                    {
                        ConversationId = conversationId.Value,
                        ExternalMessageId = $"msg_ai_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = part,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(msg);
                    await _context.SaveChangesAsync();

                    // Broadcast via SignalR
                    await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveMessage", new
                    {
                        id = msg.Id,
                        conversationId = conversationId.Value,
                        senderType = "AI",
                        content = part,
                        createdAt = msg.Timestamp,
                        status = "Sent",
                        channel = "Messenger"
                    });
                }

                // If there are more parts, wait a brief delay (e.g. 1.5 seconds) to make it feel natural
                if (i < parts.Count - 1)
                {
                    await Task.Delay(1500);
                }
            }

            if (conversationId.HasValue)
            {
                // Clear typing state in Redis and broadcast typing finished
                var redisKey = $"ai_typing:{conversationId.Value}";
                try
                {
                    await _redis.KeyDeleteAsync(redisKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[FacebookReplySender] Redis delete typing state failed: {Message}", ex.Message);
                }

                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                {
                    conversationId = conversationId.Value,
                    isTyping = false
                });
            }

            _logger.LogInformation("[FacebookReplySender] Messenger reply sent to {Sender} in {Count} messages", @event.Sender, parts.Count);
        }

        private async Task HandleCommentReply(Modules.Facebook.Domain.ConnectedPage connectedPage, AIReplyGeneratedEvent @event)
        {
            // Parse channel metadata for comment details
            string commentId = null;
            string postId = null;
            if (!string.IsNullOrEmpty(@event.ChannelMetadata))
            {
                try
                {
                    using var doc = JsonDocument.Parse(@event.ChannelMetadata);
                    if (doc.RootElement.TryGetProperty("commentId", out var cid))
                    {
                        commentId = cid.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("CommentId", out var cid2))
                    {
                        commentId = cid2.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("postId", out var pid))
                    {
                        postId = pid.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("PostId", out var pid2))
                    {
                        postId = pid2.GetString();
                    }
                }
                catch { /* ignore parse errors */ }
            }

            if (string.IsNullOrEmpty(commentId))
            {
                _logger.LogWarning("[FacebookReplySender] No commentId in metadata, skipping comment reply");
                return;
            }

            // Find customer
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.FacebookPSID == @event.Sender);

            Guid? commentConvId = null;

            if (customer != null)
            {
                var commentConv = await _context.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Channel == "FacebookComment"
                        && (c.Status == "Open" || c.Status == "Pending"));

                if (commentConv != null)
                {
                    commentConvId = commentConv.Id;

                    // Transition typing stage from "generating" to "typing" (4 seconds)
                    var redisKey = $"ai_typing:{commentConv.Id}";
                    try
                    {
                        await _redis.StringSetAsync(redisKey, "typing", TimeSpan.FromSeconds(30));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[FacebookReplySender] Redis set typing state failed: {Message}", ex.Message);
                    }

                    await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                    {
                        conversationId = commentConv.Id,
                        isTyping = true,
                        estimatedSeconds = 4,
                        stage = "typing"
                    });
                }
            }

            // Simulate typing delay (4 seconds)
            await Task.Delay(4000);

            // 1. Reply publicly to the comment
            var publicReply = !string.IsNullOrEmpty(@event.PublicCommentReply) 
                ? @event.PublicCommentReply 
                : "تم الرد في الخاص يا فندم! ❤️"; // Fallback to a default message

            await _graphService.ReplyToCommentAsync(connectedPage.PageAccessToken, commentId, publicReply);

            // 2. React to the comment (LOVE only as per user request)
            await _graphService.ReactToCommentAsync(connectedPage.PageAccessToken, commentId, "LOVE");

            // 3. Send private DM (only if not empty/null)
            bool sentPrivate = !string.IsNullOrEmpty(@event.Content);
            if (sentPrivate)
            {
                await _graphService.SendPrivateReplyAsync(
                    connectedPage.FacebookPageId,
                    connectedPage.PageAccessToken,
                    commentId,
                    @event.Content);
            }

            if (customer != null && commentConvId.HasValue)
            {
                // Clear typing state in Redis and broadcast typing finished
                var redisKey = $"ai_typing:{commentConvId.Value}";
                try
                {
                    await _redis.KeyDeleteAsync(redisKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[FacebookReplySender] Redis delete typing state failed: {Message}", ex.Message);
                }

                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                {
                    conversationId = commentConvId.Value,
                    isTyping = false
                });

                // 1. Save public comment reply to DB
                var publicMsg = new Modules.Conversations.Domain.Message
                {
                    ConversationId = commentConvId.Value,
                    ExternalMessageId = $"msg_ai_{Guid.NewGuid():N}",
                    Direction = "Outgoing",
                    Content = publicReply,
                    MessageType = "Text",
                    FacebookPostId = postId,
                    FacebookCommentId = commentId,
                    Timestamp = DateTime.UtcNow
                };
                _context.Messages.Add(publicMsg);

                // 2. Save reaction as Outgoing message to DB (LOVE ❤️)
                var reactMsg = new Modules.Conversations.Domain.Message
                {
                    ConversationId = commentConvId.Value,
                    ExternalMessageId = $"msg_ai_react_{Guid.NewGuid():N}",
                    Direction = "Outgoing",
                    Content = "[تفاعل] ❤️",
                    MessageType = "Reaction",
                    Timestamp = DateTime.UtcNow
                };
                _context.Messages.Add(reactMsg);

                await _context.SaveChangesAsync();

                // Broadcast public comment reply via SignalR
                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveMessage", new
                {
                    id = publicMsg.Id,
                    conversationId = commentConvId.Value,
                    senderType = "AI",
                    content = publicMsg.Content,
                    createdAt = publicMsg.Timestamp.ToString("o"),
                    status = "Sent",
                    channel = "FacebookComment",
                    facebookPostId = postId,
                    facebookCommentId = commentId
                });

                // Broadcast reaction via SignalR
                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveMessage", new
                {
                    id = reactMsg.Id,
                    conversationId = commentConvId.Value,
                    senderType = "AI",
                    content = reactMsg.Content,
                    createdAt = reactMsg.Timestamp.ToString("o"),
                    status = "Sent",
                    messageType = "Reaction",
                    channel = "FacebookComment"
                });

                // 3. Find/Create Messenger Conversation & Save private DM if sent
                if (sentPrivate)
                {
                    var messengerConv = await _context.Conversations
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger"
                            && (c.Status == "Open" || c.Status == "Pending"));

                    if (messengerConv == null)
                    {
                        messengerConv = new Modules.Conversations.Domain.Conversation
                        {
                            ProjectId = @event.ProjectId,
                            CustomerId = customer.Id,
                            Channel = "Messenger",
                            Status = "Open",
                            LastMessageTimestamp = DateTime.UtcNow
                        };
                        _context.Conversations.Add(messengerConv);
                        await _context.SaveChangesAsync();
                    }

                    var privateMsg = new Modules.Conversations.Domain.Message
                    {
                        ConversationId = messengerConv.Id,
                        ExternalMessageId = $"msg_ai_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = @event.Content,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(privateMsg);
                    await _context.SaveChangesAsync();

                    // Broadcast private DM via SignalR
                    await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveMessage", new
                    {
                        id = privateMsg.Id,
                        conversationId = messengerConv.Id,
                        senderType = "AI",
                        content = privateMsg.Content,
                        createdAt = privateMsg.Timestamp.ToString("o"),
                        status = "Sent",
                        channel = "Messenger"
                    });
                }
            }

            _logger.LogInformation("[FacebookReplySender] Comment reply (public+reaction LOVE, private DM sent: {SentPrivate}) sent for comment {CommentId}", sentPrivate, commentId);
        }
    }
}
