using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using Modules.Conversations.Domain;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Modules.Conversations.API
{
    [ApiController]
    [Route("api")]
    public class ConversationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Services.IAssignmentEngine _assignmentEngine;
        private readonly Shared.Queue.IEventBus _eventBus;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly StackExchange.Redis.IDatabase _redis;

        public ConversationController(
            AppDbContext context, 
            Services.IAssignmentEngine assignmentEngine, 
            Shared.Queue.IEventBus eventBus, 
            IHubContext<NotificationHub> hubContext,
            IConfiguration configuration,
            StackExchange.Redis.IConnectionMultiplexer redis)
        {
            _context = context;
            _assignmentEngine = assignmentEngine;
            _eventBus = eventBus;
            _hubContext = hubContext;
            _configuration = configuration;
            _httpClient = new HttpClient();
            _redis = redis.GetDatabase();
        }

        [HttpGet("projects/{projectId}/conversations")]
        public async Task<IActionResult> ListConversations(Guid projectId)
        {
            var conversations = await _context.Conversations
                .Where(c => c.ProjectId == projectId)
                .Join(_context.Customers,
                    c => c.CustomerId,
                    cust => cust.Id,
                    (c, cust) => new
                    {
                        c.Id,
                        c.ProjectId,
                        c.Status,
                        c.LastMessageTimestamp,
                        c.CreatedAt,
                        c.AssignedUserId,
                        customer = new
                        {
                            id = cust.Id,
                            name = cust.Name ?? cust.PhoneNumber,
                            phone = cust.PhoneNumber,
                            avatarUrl = (string)null,
                            label = cust.Label
                        }
                    })
                .ToListAsync();

            var mapped = conversations.Select(c => {
                var redisKey = $"ai_typing:{c.Id}";
                var remainingSec = 0;
                var isTyping = false;
                var stage = "generating";
                try
                {
                    var ttl = _redis.KeyTimeToLive(redisKey);
                    if (ttl.HasValue && ttl.Value.TotalSeconds > 0)
                    {
                        isTyping = true;
                        remainingSec = (int)Math.Ceiling(ttl.Value.TotalSeconds);
                        var val = _redis.StringGet(redisKey);
                        if (!val.IsNullOrEmpty)
                        {
                            stage = val.ToString();
                        }
                    }
                }
                catch
                {
                    // Fallback if Redis fails
                }

                return new
                {
                    id = c.Id,
                    projectId = c.ProjectId,
                    status = c.Status,
                    lastMessageAt = c.LastMessageTimestamp.ToString("o"),
                    createdAt = c.CreatedAt.ToString("o"),
                    unreadCount = 0,
                    assignedAgentId = c.AssignedUserId,
                    assignedAgentName = (string)null,
                    customer = c.customer,
                    isAiTyping = isTyping,
                    aiTypingCountdown = remainingSec,
                    aiTypingStage = stage
                };
            }).ToList();

            return Ok(mapped);
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> ListMessages(
            Guid conversationId,
            [FromQuery] DateTime? before = null,
            [FromQuery] int limit = 10)
        {
            var query = _context.Messages
                .Where(m => m.ConversationId == conversationId);

            if (before.HasValue)
            {
                var beforeUtc = before.Value.ToUniversalTime();
                query = query.Where(m => m.Timestamp < beforeUtc);
            }

            var messages = await query
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .Select(m => new
                {
                    id = m.Id,
                    conversationId = m.ConversationId,
                    senderType = m.Direction == "Incoming" ? "Customer" : (m.ExternalMessageId != null && m.ExternalMessageId.StartsWith("msg_ai_") ? "AI" : "Agent"),
                    content = m.Content,
                    createdAt = m.Timestamp.ToString("o"),
                    status = m.Direction == "Incoming" ? "Delivered" : "Sent",
                    mediaUrl = (string)null,
                    mediaType = m.MessageType == "Image" || m.MessageType == "Voice" ? m.MessageType : (string)null,
                    messageType = m.MessageType,
                    assetId = m.AssetId,
                    transcription = m.Transcription,
                    direction = m.Direction,
                    timestamp = m.Timestamp
                })
                .ToListAsync();

            messages.Reverse();
            return Ok(messages);
        }

        [HttpPost("conversations/{id}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest("Content is required.");
            }

            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null)
            {
                return NotFound($"Conversation {id} not found.");
            }

            // Create Outgoing message
            var message = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = $"msg_agent_{Guid.NewGuid().ToString("N")}",
                Direction = "Outgoing",
                Content = request.Content,
                MessageType = "Text",
                Timestamp = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            
            // Update conversation last message timestamp
            conversation.LastMessageTimestamp = DateTime.UtcNow;
            _context.Entry(conversation).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            // Complete existing pending follow-ups for this customer
            var pendingFollowUps = await _context.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.CustomerId == conversation.CustomerId && f.Status == "Pending")
                .ToListAsync();

            foreach (var fu in pendingFollowUps)
            {
                fu.Status = "Completed";
                _context.Entry(fu).State = EntityState.Modified;
            }

            // Schedule default follow-up in 24 hours only if AI auto-reply is enabled and customer is not blacklisted
            var settings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == conversation.ProjectId);
            var cust = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == conversation.CustomerId);

            bool shouldScheduleFollowUp = settings != null && settings.AiAutoReplyEnabled && (cust == null || !cust.IsBlacklisted);

            if (shouldScheduleFollowUp)
            {
                var defaultFollowUp = new Modules.CRM.Domain.FollowUp
                {
                    Id = Guid.NewGuid(),
                    ProjectId = conversation.ProjectId,
                    CustomerId = conversation.CustomerId,
                    Type = "Nurturing",
                    DueDate = DateTime.UtcNow.AddHours(24),
                    Notes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                    Status = "Pending"
                };

                _context.FollowUps.Add(defaultFollowUp);
            }
            await _context.SaveChangesAsync();

            // Broadcast message via SignalR to project group
            var payload = new
            {
                id = message.Id,
                conversationId = message.ConversationId,
                senderType = "Agent",
                content = message.Content,
                createdAt = message.Timestamp.ToString("o"),
                status = "Sent",
                mediaUrl = (string)null,
                mediaType = (string)null
            };

            await _hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", payload);

            // Forward to WhatsApp Gateway
            var customer = await _context.Customers.FindAsync(conversation.CustomerId);
            if (customer != null && !string.IsNullOrEmpty(customer.PhoneNumber))
            {
                var gatewayUrl = _configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
                var gatewayPayload = new
                {
                    projectId = conversation.ProjectId,
                    to = customer.PhoneNumber,
                    message = request.Content
                };
                
                var jsonPayload = JsonSerializer.Serialize(gatewayPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                try
                {
                    var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(_httpClient, $"{gatewayUrl}/api/whatsapp/send", jsonPayload);
                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[ConversationController] Gateway returned error code {response.StatusCode}: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConversationController] Exception while calling WhatsApp Gateway: {ex.Message}");
                }
            }

            return Ok(payload);
        }

        [HttpPost("conversations/{id}/assign")]
        public async Task<IActionResult> AssignConversation(Guid id, [FromBody] AssignConversationRequest request, [FromHeader(Name = "X-Project-Id")] Guid? projectIdHeader)
        {
            var projectId = projectIdHeader ?? _context.CurrentProjectId;
            if (projectId == Guid.Empty)
            {
                return BadRequest("Project Context Required");
            }

            try
            {
                var assignedAgentId = await _assignmentEngine.AssignConversationAsync(projectId, id, request?.AgentId);
                return Ok(new { conversationId = id, assignedUserId = assignedAgentId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("projects/{projectId}/agents/{agentId}/presence")]
        public async Task<IActionResult> UpdatePresence(Guid projectId, Guid agentId, [FromBody] PresenceUpdateRequest request)
        {
            await _assignmentEngine.UpdatePresenceAsync(projectId, agentId, request.IsOnline);
            return Ok(new { projectId, agentId, request.IsOnline });
        }

        [HttpGet("projects/{projectId}/agents/workload")]
        public async Task<IActionResult> GetWorkloadReport(Guid projectId)
        {
            var report = await _assignmentEngine.GetWorkloadReportAsync(projectId);
            return Ok(report);
        }

        [HttpPut("conversations/{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateConversationStatusRequest request)
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null) return NotFound();

            var oldStatus = conversation.Status;
            conversation.Status = request.Status;
            await _context.SaveChangesAsync();

            if (request.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase) && 
                !oldStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            {
                await _eventBus.PublishAsync(new Shared.Events.ConversationClosedEvent
                {
                    ProjectId = conversation.ProjectId,
                    CustomerId = conversation.CustomerId,
                    ConversationId = conversation.Id
                });
            }

            return Ok(conversation);
        }

        [HttpPost("conversations/{id}/messages/{messageId}/react")]
        public async Task<IActionResult> ReactToMessage(Guid id, string messageId, [FromBody] ReactToMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ReactionText))
            {
                return BadRequest("Reaction text is required.");
            }

            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null)
            {
                return NotFound($"Conversation {id} not found.");
            }

            // Find the message in the DB to get its details (direction, external message id, etc.)
            var targetMessage = await _context.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ConversationId == conversation.Id && m.ExternalMessageId == messageId);
            if (targetMessage == null)
            {
                return NotFound($"Message {messageId} not found in conversation {id}.");
            }

            // Save the outgoing reaction message (represented as an informational message or reaction)
            var reactionMessage = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = $"msg_agent_react_{Guid.NewGuid().ToString("N")}",
                Direction = "Outgoing",
                Content = $"[تفاعل] {request.ReactionText}",
                MessageType = "Reaction",
                Timestamp = DateTime.UtcNow
            };

            _context.Messages.Add(reactionMessage);
            await _context.SaveChangesAsync();

            // Forward to WhatsApp Gateway
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == conversation.CustomerId);
            if (customer != null && !string.IsNullOrEmpty(customer.PhoneNumber))
            {
                var gatewayUrl = _configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
                
                // In Baileys, we need to specify if the target message was sent by us (fromMe = true/false).
                // If Direction is "Outgoing", then targetFromMe is true. If "Incoming", then targetFromMe is false.
                bool targetFromMe = targetMessage.Direction == "Outgoing";

                var gatewayPayload = new
                {
                    projectId = conversation.ProjectId,
                    to = customer.PhoneNumber,
                    reactionText = request.ReactionText,
                    targetMessageId = targetMessage.ExternalMessageId,
                    targetFromMe = targetFromMe
                };
                
                var jsonPayload = JsonSerializer.Serialize(gatewayPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                try
                {
                    var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(_httpClient, $"{gatewayUrl}/api/whatsapp/react", jsonPayload);
                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[ConversationController] Gateway react returned error code {response.StatusCode}: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConversationController] Exception while calling WhatsApp Gateway react: {ex.Message}");
                }
            }

            // Broadcast via SignalR to project group
            var payload = new
            {
                id = reactionMessage.Id,
                conversationId = reactionMessage.ConversationId,
                senderType = "Agent",
                content = reactionMessage.Content,
                createdAt = reactionMessage.Timestamp.ToString("o"),
                status = "Sent",
                mediaUrl = (string)null,
                mediaType = (string)null,
                messageType = "Reaction"
            };

            await _hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", payload);

            return Ok(payload);
        }
    }

    public class ReactToMessageRequest
    {
        public string ReactionText { get; set; } = string.Empty;
    }

    public class SendMessageRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    public class AssignConversationRequest
    {
        public Guid? AgentId { get; set; }
    }

    public class PresenceUpdateRequest
    {
        public bool IsOnline { get; set; }
    }

    public class UpdateConversationStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
