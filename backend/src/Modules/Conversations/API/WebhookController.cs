using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.Conversations.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Modules.Conversations.API
{
    [ApiController]
    [Route("api/webhooks/whatsapp")]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IMessageAggregator _messageAggregator;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IAssignmentEngine _assignmentEngine;
        private readonly IDatabase _redis;

        public WebhookController(
            AppDbContext context, 
            ITenantContext tenantContext, 
            IMessageAggregator messageAggregator, 
            IHubContext<NotificationHub> hubContext, 
            IAssignmentEngine assignmentEngine,
            IConnectionMultiplexer redisConnection)
        {
            _context = context;
            _tenantContext = tenantContext;
            _messageAggregator = messageAggregator;
            _hubContext = hubContext;
            _assignmentEngine = assignmentEngine;
            _redis = redisConnection.GetDatabase();
        }

        [HttpPost("message")]
        public async Task<IActionResult> ReceiveMessage([FromBody] IncomingMessagePayload payload)
        {
            // Set context tenant project id
            _tenantContext.SetProjectId(payload.ProjectId);
            var normalizedSender = NormalizeWhatsAppPhone(payload.Sender);
            var senderLid = string.IsNullOrWhiteSpace(payload.SenderLid) ? null : payload.SenderLid.Trim();

            // 1. Resolve Customer by PhoneNumber globally but within payload Project.
            // WhatsApp multi-device may send a @lid as remoteJid and the real phone in remoteJidAlt.
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.ProjectId == payload.ProjectId &&
                    (c.PhoneNumber == normalizedSender || (senderLid != null && c.PhoneNumber == senderLid)));

            if (customer == null)
            {
                customer = new Customer
                {
                    ProjectId = payload.ProjectId,
                    PhoneNumber = normalizedSender,
                    Name = !string.IsNullOrWhiteSpace(payload.Name) 
                        ? payload.Name.Trim() 
                        : $"WA Customer {normalizedSender.Substring(Math.Max(0, normalizedSender.Length - 4))}",
                    City = string.Empty,
                    Notes = string.Empty
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else
            {
                bool modified = false;
                if (customer.PhoneNumber.EndsWith("@lid", StringComparison.OrdinalIgnoreCase) && normalizedSender != customer.PhoneNumber)
                {
                    customer.PhoneNumber = normalizedSender;
                    modified = true;
                }

                if (!string.IsNullOrWhiteSpace(payload.Name) && 
                    (string.IsNullOrWhiteSpace(customer.Name) || customer.Name.StartsWith("WA Customer", StringComparison.OrdinalIgnoreCase)))
                {
                    customer.Name = payload.Name.Trim();
                    modified = true;
                }

                if (modified)
                {
                    _context.Entry(customer).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }

            // 2. Resolve or create active Conversation thread
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == payload.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    ProjectId = payload.ProjectId,
                    CustomerId = customer.Id,
                    Status = "Open",
                    LastMessageTimestamp = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageTimestamp = DateTime.UtcNow;
                if (conversation.Status != "Open")
                {
                    conversation.Status = "Open";
                }

                if (conversation.AssignedUserId.HasValue)
                {
                    bool reassign = false;

                    var redisDb = (StackExchange.Redis.IDatabase?)HttpContext.RequestServices.GetService(typeof(StackExchange.Redis.IDatabase)) 
                        ?? ((StackExchange.Redis.IConnectionMultiplexer?)HttpContext.RequestServices.GetService(typeof(StackExchange.Redis.IConnectionMultiplexer)))?.GetDatabase();
                    
                    if (redisDb != null)
                    {
                        var presenceKey = $"project:{payload.ProjectId}:agent:{conversation.AssignedUserId.Value}:presence";
                        var isOnlineVal = await redisDb.HashGetAsync(presenceKey, "IsOnline");
                        bool isOnline = isOnlineVal.HasValue && isOnlineVal.ToString() == "true";
                        
                        if (!isOnline)
                        {
                            Console.WriteLine($"[WebhookController] Agent {conversation.AssignedUserId.Value} is offline in Redis. Flagging for reassignment.");
                            reassign = true;
                        }
                    }

                    if (!reassign)
                    {
                        var lastAgentMessage = await _context.Messages
                            .Where(m => m.ConversationId == conversation.Id && m.Direction == "Outgoing")
                            .OrderByDescending(m => m.Timestamp)
                            .FirstOrDefaultAsync();

                        if (lastAgentMessage != null && (DateTime.UtcNow - lastAgentMessage.Timestamp).TotalMinutes > 10)
                        {
                            Console.WriteLine($"[WebhookController] Agent {conversation.AssignedUserId.Value} has been idle for >10 mins. Flagging for reassignment.");
                            reassign = true;
                        }
                    }

                    if (reassign)
                    {
                        conversation.AssignedUserId = null;
                    }
                }

                _context.Entry(conversation).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            if (conversation.AssignedUserId == null)
            {
                try
                {
                    var assignedAgentId = await _assignmentEngine.AssignConversationAsync(payload.ProjectId, conversation.Id);
                    if (assignedAgentId.HasValue)
                    {
                        conversation.AssignedUserId = assignedAgentId.Value;
                        _context.Entry(conversation).State = EntityState.Modified;
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebhookController] Auto-routing failed: {ex.Message}");
                }
            }

            // 3. Save individual incoming message
            var message = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = payload.MessageId,
                Direction = "Incoming",
                Content = payload.Content,
                MessageType = payload.MessageType ?? "Text",
                Timestamp = DateTime.UtcNow,
                AssetId = payload.AssetId
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Complete existing pending follow-ups for this customer
            var pendingFollowUps = await _context.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.CustomerId == customer.Id && f.Status == "Pending")
                .ToListAsync();

            foreach (var fu in pendingFollowUps)
            {
                fu.Status = "Completed";
                _context.Entry(fu).State = EntityState.Modified;
            }

            // Schedule default follow-up in 24 hours only if AI auto-reply is enabled and customer is not blacklisted
            var projSettings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == payload.ProjectId);

            bool shouldScheduleFollowUp = projSettings != null && projSettings.AiAutoReplyEnabled && !customer.IsBlacklisted;

            if (shouldScheduleFollowUp)
            {
                var defaultFollowUp = new Modules.CRM.Domain.FollowUp
                {
                    Id = Guid.NewGuid(),
                    ProjectId = payload.ProjectId,
                    CustomerId = customer.Id,
                    Type = "Nurturing",
                    DueDate = DateTime.UtcNow.AddHours(24),
                    Notes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                    Status = "Pending"
                };

                _context.FollowUps.Add(defaultFollowUp);
            }
            await _context.SaveChangesAsync();

            // 3.5 Broadcast via SignalR to the group
            await _hubContext.Clients.Group($"project_{payload.ProjectId}").SendAsync("ReceiveMessage", new
            {
                id = message.Id,
                conversationId = message.ConversationId,
                senderType = "Customer",
                content = message.Content,
                createdAt = message.Timestamp.ToString("o"),
                status = "Delivered",
                mediaUrl = (string)null,
                mediaType = message.MessageType == "Image" || message.MessageType == "Voice" ? message.MessageType : (string)null,
                assetId = message.AssetId,
                transcription = message.Transcription
            });

            if (payload.MessageType != "Reaction")
            {
                // 3.6 Broadcast AI typing if auto-reply is enabled
                var settings = await _context.ProjectSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.ProjectId == payload.ProjectId);
                if (settings != null && settings.AiAutoReplyEnabled && !customer.IsBlacklisted)
                {
                    var redisKey = $"ai_typing:{conversation.Id}";
                    try
                    {
                        await _redis.StringSetAsync(redisKey, "generating", TimeSpan.FromSeconds(120));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WebhookController] Redis set failed: {ex.Message}");
                    }

                    await _hubContext.Clients.Group($"project_{payload.ProjectId}").SendAsync("AITyping", new
                    {
                        conversationId = conversation.Id,
                        isTyping = true,
                        estimatedSeconds = 11,
                        stage = "generating"
                    });
                }

                // 4. Pass message to aggregator for grouping window
                await _messageAggregator.AggregateMessageAsync(payload.ProjectId, normalizedSender, payload.Content);
            }

            return Ok(new { status = "Received" });
        }

        private static string NormalizeWhatsAppPhone(string sender)
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return sender;
            }

            var trimmed = sender.Trim();
            if (trimmed.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Split('@')[0];
            }

            return trimmed;
        }
    }

    public class IncomingMessagePayload
    {
        public Guid ProjectId { get; set; }
        public string MessageId { get; set; } = default!;
        public string Sender { get; set; } = default!;
        public string? SenderJid { get; set; }
        public string? SenderLid { get; set; }
        public string? Name { get; set; }
        public string Content { get; set; } = default!;
        public string? MessageType { get; set; }
        public long Timestamp { get; set; }
        public Guid? AssetId { get; set; }
    }
}
