using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Conversations.Services;
using Modules.Facebook.Services;
using Shared.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Facebook.API
{
    [ApiController]
    [Route("api/webhooks/facebook")]
    public class FacebookWebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IMessageAggregator _messageAggregator;
        private readonly Shared.Queue.IEventBus _eventBus;
        private readonly StackExchange.Redis.IDatabase _redis;
        private readonly IFacebookGraphService _graphService;

        public FacebookWebhookController(
            AppDbContext context,
            IConfiguration configuration,
            IHubContext<NotificationHub> hubContext,
            IMessageAggregator messageAggregator,
            Shared.Queue.IEventBus eventBus,
            StackExchange.Redis.IConnectionMultiplexer redis,
            IFacebookGraphService graphService)
        {
            _context = context;
            _configuration = configuration;
            _hubContext = hubContext;
            _messageAggregator = messageAggregator;
            _eventBus = eventBus;
            _redis = redis.GetDatabase();
            _graphService = graphService;
        }

        /// <summary>
        /// Webhook verification — Facebook sends GET with hub.verify_token challenge
        /// </summary>
        [HttpGet]
        public IActionResult Verify(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string verifyToken,
            [FromQuery(Name = "hub.challenge")] string challenge)
        {
            var expectedToken = _configuration["FACEBOOK_VERIFY_TOKEN"];

            if (mode == "subscribe" && verifyToken == expectedToken)
            {
                Console.WriteLine("[FacebookWebhook] Verification successful");
                return Ok(challenge);
            }

            Console.WriteLine("[FacebookWebhook] Verification failed — token mismatch");
            return StatusCode(403, "Verification failed");
        }

        /// <summary>
        /// Receive webhook events from Facebook (messages + feed/comments)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ReceiveEvent()
        {
            // Read body
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            // Validate signature
            var appSecret = _configuration["FACEBOOK_APP_SECRET"];
            if (!string.IsNullOrEmpty(appSecret))
            {
                var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
                if (!string.IsNullOrEmpty(signature) && !ValidateSignature(body, signature, appSecret))
                {
                    Console.WriteLine("[FacebookWebhook] Invalid signature");
                    return StatusCode(403, "Invalid signature");
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (!root.TryGetProperty("entry", out var entries))
                    return Ok("EVENT_RECEIVED");

                foreach (var entry in entries.EnumerateArray())
                {
                    var pageId = entry.GetProperty("id").GetString();

                    // Handle Messenger messages
                    if (entry.TryGetProperty("messaging", out var messagingArray))
                    {
                        foreach (var messaging in messagingArray.EnumerateArray())
                        {
                            await HandleMessengerMessage(pageId, messaging);
                        }
                    }

                    // Handle feed changes (comments)
                    if (entry.TryGetProperty("changes", out var changesArray))
                    {
                        foreach (var change in changesArray.EnumerateArray())
                        {
                            var field = change.GetProperty("field").GetString();
                            if (field == "feed" && change.TryGetProperty("value", out var value))
                            {
                                var item = value.TryGetProperty("item", out var itemProp) ? itemProp.GetString() : null;
                                if (item == "comment")
                                {
                                    var verb = value.TryGetProperty("verb", out var verbProp) ? verbProp.GetString() : "add";
                                    if (verb == "add")
                                    {
                                        await HandleCommentReceived(pageId, value);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FacebookWebhook] Error processing event: {ex.Message}");
            }

            return Ok("EVENT_RECEIVED");
        }

        private async Task HandleMessengerMessage(string pageId, JsonElement messaging)
        {
            if (!messaging.TryGetProperty("message", out var message)) return;
            if (!messaging.TryGetProperty("sender", out var sender)) return;

            var senderPSID = sender.GetProperty("id").GetString();
            var messageText = message.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;

            if (string.IsNullOrEmpty(senderPSID) || string.IsNullOrEmpty(messageText)) return;

            // Skip echo messages (sent by the page itself)
            if (message.TryGetProperty("is_echo", out var isEcho) && isEcho.GetBoolean()) return;

            // Resolve ConnectedPage
            var connectedPage = await _context.ConnectedPages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cp => cp.FacebookPageId == pageId && cp.IsActive);

            if (connectedPage == null)
            {
                Console.WriteLine($"[FacebookWebhook] No connected page found for pageId: {pageId}");
                return;
            }

            // Set tenant context for queries
            var projectId = connectedPage.ProjectId;

            // Resolve or create Customer
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.FacebookPSID == senderPSID);

            if (customer == null)
            {
                var realName = await _graphService.GetMessengerProfileNameAsync(senderPSID, connectedPage.PageAccessToken);
                customer = new Customer
                {
                    ProjectId = projectId,
                    PhoneNumber = "",
                    Name = realName ?? $"Messenger User {senderPSID.Substring(0, Math.Min(6, senderPSID.Length))}",
                    City = "",
                    FacebookPSID = senderPSID,
                    FacebookName = realName
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else if (customer.Name.StartsWith("Messenger User") && string.IsNullOrEmpty(customer.FacebookName))
            {
                var realName = await _graphService.GetMessengerProfileNameAsync(senderPSID, connectedPage.PageAccessToken);
                if (!string.IsNullOrEmpty(realName))
                {
                    customer.Name = realName;
                    customer.FacebookName = realName;
                    await _context.SaveChangesAsync();
                }
            }

            // Resolve or create Conversation
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.CustomerId == customer.Id && c.Channel == "Messenger"
                    && (c.Status == "Open" || c.Status == "Pending"));

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    ProjectId = projectId,
                    CustomerId = customer.Id,
                    Channel = "Messenger",
                    Status = "Open",
                    LastMessageTimestamp = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageTimestamp = DateTime.UtcNow;
                if (conversation.Status == "Resolved" || conversation.Status == "Closed")
                    conversation.Status = "Open";
            }

            // Save Message
            var mid = message.TryGetProperty("mid", out var midProp) ? midProp.GetString() : null;
            var msg = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = mid ?? $"msg_fb_{Guid.NewGuid():N}",
                Direction = "Incoming",
                Content = messageText,
                MessageType = "Text",
                Timestamp = DateTime.UtcNow
            };
            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            // Broadcast via SignalR
            await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveMessage", new
            {
                id = msg.Id,
                conversationId = conversation.Id,
                senderType = "Customer",
                content = messageText,
                createdAt = msg.Timestamp,
                status = "Delivered",
                channel = "Messenger"
            });

            // Broadcast AI typing if auto-reply is enabled for Messenger
            var settings = await _context.ProjectSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == projectId);
            if (settings != null && settings.MessengerAiAutoReplyEnabled && (customer == null || !customer.IsBlacklisted))
            {
                var redisKey = $"ai_typing:{conversation.Id}";
                try
                {
                    await _redis.StringSetAsync(redisKey, "generating", TimeSpan.FromSeconds(120));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FacebookWebhook] Redis set failed: {ex.Message}");
                }

                await _hubContext.Clients.Group($"project_{projectId}").SendAsync("AITyping", new
                {
                    conversationId = conversation.Id,
                    isTyping = true,
                    estimatedSeconds = 11,
                    stage = "generating"
                });
            }

            // Publish to aggregator for AI auto-reply
            await _eventBus.PublishAsync(new Shared.Events.MessageAggregatedEvent
            {
                ProjectId = projectId,
                Sender = senderPSID,
                Content = messageText,
                Channel = "Messenger",
                ChannelMetadata = JsonSerializer.Serialize(new { pageId, senderPSID })
            });
        }

        private async Task HandleCommentReceived(string pageId, JsonElement value)
        {
            var commentId = value.TryGetProperty("comment_id", out var cid) ? cid.GetString() : null;
            var postId = value.TryGetProperty("post_id", out var pid) ? pid.GetString() : null;
            var commentText = value.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : null;

            string senderPSID = null;
            string senderName = null;
            if (value.TryGetProperty("from", out var from))
            {
                senderPSID = from.TryGetProperty("id", out var fromId) ? fromId.GetString() : null;
                senderName = from.TryGetProperty("name", out var fromName) ? fromName.GetString() : null;
            }

            if (string.IsNullOrEmpty(commentId) || string.IsNullOrEmpty(senderPSID)) return;

            // Skip if the commenter is the page itself
            if (senderPSID == pageId) return;

            // Resolve ConnectedPage
            var connectedPage = await _context.ConnectedPages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cp => cp.FacebookPageId == pageId && cp.IsActive);

            if (connectedPage == null) return;

            var projectId = connectedPage.ProjectId;

            // Resolve or create Customer
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.FacebookPSID == senderPSID);

            if (customer == null)
            {
                customer = new Customer
                {
                    ProjectId = projectId,
                    PhoneNumber = "",
                    Name = senderName ?? $"FB User {senderPSID.Substring(0, Math.Min(6, senderPSID.Length))}",
                    City = "",
                    FacebookPSID = senderPSID,
                    FacebookName = senderName
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else if (!string.IsNullOrEmpty(senderName) && customer.FacebookName != senderName)
            {
                customer.FacebookName = senderName;
                customer.Name = senderName;
            }

            // Resolve or create Conversation (grouped by Post)
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.CustomerId == customer.Id && c.Channel == "FacebookComment"
                    && (c.Status == "Open" || c.Status == "Pending"));

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    ProjectId = projectId,
                    CustomerId = customer.Id,
                    Channel = "FacebookComment",
                    Status = "Open",
                    LastMessageTimestamp = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageTimestamp = DateTime.UtcNow;
            }

            // Save Message
            var msg = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = commentId ?? $"msg_comment_{Guid.NewGuid():N}",
                Direction = "Incoming",
                Content = commentText ?? "",
                MessageType = "Text",
                FacebookPostId = postId,
                FacebookCommentId = commentId,
                Timestamp = DateTime.UtcNow
            };
            _context.Messages.Add(msg);
            await _context.SaveChangesAsync();

            // Broadcast via SignalR
            await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveMessage", new
            {
                id = msg.Id,
                conversationId = conversation.Id,
                senderType = "Customer",
                content = commentText,
                createdAt = msg.Timestamp,
                status = "Delivered",
                channel = "FacebookComment",
                facebookPostId = postId,
                facebookCommentId = commentId
            });

            // Broadcast AI typing if auto-reply is enabled for Comments
            var settings = await _context.ProjectSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == projectId);
            if (settings != null && settings.CommentsAiAutoReplyEnabled && (customer == null || !customer.IsBlacklisted))
            {
                var redisKey = $"ai_typing:{conversation.Id}";
                try
                {
                    await _redis.StringSetAsync(redisKey, "generating", TimeSpan.FromSeconds(120));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FacebookWebhook] Redis set failed: {ex.Message}");
                }

                await _hubContext.Clients.Group($"project_{projectId}").SendAsync("AITyping", new
                {
                    conversationId = conversation.Id,
                    isTyping = true,
                    estimatedSeconds = 11,
                    stage = "generating"
                });
            }

            // Publish for AI auto-reply
            await _eventBus.PublishAsync(new Shared.Events.MessageAggregatedEvent
            {
                ProjectId = projectId,
                Sender = senderPSID,
                Content = commentText ?? "",
                Channel = "FacebookComment",
                ChannelMetadata = JsonSerializer.Serialize(new { pageId, commentId, postId, senderPSID })
            });
        }

        private static bool ValidateSignature(string payload, string signatureHeader, string appSecret)
        {
            if (!signatureHeader.StartsWith("sha256=")) return false;

            var expectedSignature = signatureHeader.Substring(7);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

            return computedSignature == expectedSignature;
        }
    }
}
