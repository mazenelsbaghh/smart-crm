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
        private readonly Modules.Facebook.Services.IFacebookGraphService _facebookGraphService;

        public ConversationController(
            AppDbContext context, 
            Services.IAssignmentEngine assignmentEngine, 
            Shared.Queue.IEventBus eventBus, 
            IHubContext<NotificationHub> hubContext,
            IConfiguration configuration,
            StackExchange.Redis.IConnectionMultiplexer redis,
            Modules.Facebook.Services.IFacebookGraphService facebookGraphService)
        {
            _context = context;
            _assignmentEngine = assignmentEngine;
            _eventBus = eventBus;
            _hubContext = hubContext;
            _configuration = configuration;
            _httpClient = new HttpClient();
            _redis = redis.GetDatabase();
            _facebookGraphService = facebookGraphService;
        }

        [HttpGet("projects/{projectId}/conversations")]
        public async Task<IActionResult> ListConversations(
            Guid projectId,
            [FromQuery] string status = "All",
            [FromQuery] string channel = "WhatsApp",
            [FromQuery] string search = null,
            [FromQuery] DateTime? before = null,
            [FromQuery] int limit = 20)
        {
            IQueryable<Conversation> query = _context.Conversations
                .Where(c => c.ProjectId == projectId);

            // Filter by channel
            if (!string.IsNullOrEmpty(channel) && !channel.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.Channel == channel);
            }

            if (!string.IsNullOrEmpty(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.Status == status);
            }

            var joinedQuery = query.Join(_context.Customers,
                c => c.CustomerId,
                cust => cust.Id,
                (c, cust) => new
                {
                    Conversation = c,
                    Customer = cust
                });

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                joinedQuery = joinedQuery.Where(x => 
                    (x.Customer.Name != null && x.Customer.Name.ToLower().Contains(searchLower)) || 
                    (x.Customer.PhoneNumber != null && x.Customer.PhoneNumber.Contains(searchLower)));
            }

            if (before.HasValue)
            {
                var beforeUtc = before.Value.ToUniversalTime();
                joinedQuery = joinedQuery.Where(x => x.Conversation.LastMessageTimestamp < beforeUtc);
            }

            var conversations = await joinedQuery
                .OrderByDescending(x => x.Conversation.LastMessageTimestamp)
                .Take(limit)
                .Select(x => new
                {
                    x.Conversation.Id,
                    x.Conversation.ProjectId,
                    x.Conversation.Status,
                    x.Conversation.Channel,
                    x.Conversation.LastMessageTimestamp,
                    x.Conversation.CreatedAt,
                    x.Conversation.AssignedUserId,
                    customer = new
                    {
                        id = x.Customer.Id,
                        name = x.Customer.Name ?? x.Customer.PhoneNumber,
                        phone = x.Customer.PhoneNumber,
                        avatarUrl = (string)null,
                        label = x.Customer.Label,
                        facebookPSID = x.Customer.FacebookPSID,
                        facebookName = x.Customer.FacebookName
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
                fu.Status = "Bypassed";
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

            // Route to appropriate gateway based on channel
            if (conversation.Channel == "Messenger")
            {
                // Send via Facebook Messenger
                var customer = await _context.Customers.FindAsync(conversation.CustomerId);
                if (customer != null && !string.IsNullOrEmpty(customer.FacebookPSID))
                {
                    try
                    {
                        var connectedPage = await _context.ConnectedPages
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(cp => cp.ProjectId == conversation.ProjectId && cp.IsActive);
                        if (connectedPage != null)
                        {
                            await _facebookGraphService.SendMessageAsync(
                                connectedPage.FacebookPageId,
                                connectedPage.PageAccessToken,
                                customer.FacebookPSID,
                                request.Content);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ConversationController] Exception while calling Facebook Graph: {ex.Message}");
                    }
                }
            }
            else
            {
                // Forward to WhatsApp Gateway (default)
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
            }

            return Ok(payload);
        }

        /// <summary>
        /// Composite comment reply: public comment + private DM + reaction
        /// </summary>
        [HttpPost("projects/{projectId}/conversations/{id}/comment-reply")]
        public async Task<IActionResult> CommentReply(Guid projectId, Guid id, [FromBody] CommentReplyRequest request)
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null || conversation.Channel != "FacebookComment")
                return NotFound("Comment conversation not found");

            var connectedPage = await _context.ConnectedPages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cp => cp.ProjectId == projectId && cp.IsActive);

            if (connectedPage == null)
                return BadRequest(new { error = "No connected Facebook page" });

            // Get the latest incoming comment
            var lastComment = await _context.Messages
                .Where(m => m.ConversationId == id && m.Direction == "Incoming" && m.FacebookCommentId != null)
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefaultAsync();

            if (lastComment == null)
                return BadRequest(new { error = "No incoming comment found" });

            bool publicSent = false, dmSent = false, reactionApplied = false;

            // 1. Public comment reply
            if (!string.IsNullOrEmpty(request.PublicComment))
            {
                try
                {
                    await _facebookGraphService.ReplyToCommentAsync(
                        connectedPage.PageAccessToken,
                        lastComment.FacebookCommentId!,
                        request.PublicComment);
                    publicSent = true;

                    // Save as outgoing message
                    var publicMsg = new Message
                    {
                        ConversationId = id,
                        ExternalMessageId = $"msg_out_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = request.PublicComment,
                        MessageType = "Text",
                        FacebookPostId = lastComment.FacebookPostId,
                        FacebookCommentId = lastComment.FacebookCommentId,
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(publicMsg);
                    await _context.SaveChangesAsync();

                    // Broadcast via SignalR
                    await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveMessage", new
                    {
                        id = publicMsg.Id,
                        conversationId = id,
                        senderType = "Agent",
                        content = publicMsg.Content,
                        createdAt = publicMsg.Timestamp.ToString("o"),
                        status = "Sent",
                        channel = "FacebookComment",
                        facebookPostId = lastComment.FacebookPostId,
                        facebookCommentId = lastComment.FacebookCommentId
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommentReply] Public reply failed: {ex.Message}");
                }
            }

            // 2. Private DM
            if (!string.IsNullOrEmpty(request.PrivateDM))
            {
                try
                {
                    await _facebookGraphService.SendPrivateReplyAsync(
                        connectedPage.FacebookPageId,
                        connectedPage.PageAccessToken,
                        lastComment.FacebookCommentId!,
                        request.PrivateDM);
                    dmSent = true;

                    // Find or create Messenger conversation to save private DM
                    var messengerConv = await _context.Conversations
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.CustomerId == conversation.CustomerId && c.Channel == "Messenger"
                            && (c.Status == "Open" || c.Status == "Pending"));

                    if (messengerConv == null)
                    {
                        messengerConv = new Conversation
                        {
                            ProjectId = projectId,
                            CustomerId = conversation.CustomerId,
                            Channel = "Messenger",
                            Status = "Open",
                            LastMessageTimestamp = DateTime.UtcNow
                        };
                        _context.Conversations.Add(messengerConv);
                        await _context.SaveChangesAsync();
                    }

                    var privateMsg = new Message
                    {
                        ConversationId = messengerConv.Id,
                        ExternalMessageId = $"msg_out_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = request.PrivateDM,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(privateMsg);
                    await _context.SaveChangesAsync();

                    // Broadcast via SignalR to Messenger group
                    await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveMessage", new
                    {
                        id = privateMsg.Id,
                        conversationId = messengerConv.Id,
                        senderType = "Agent",
                        content = privateMsg.Content,
                        createdAt = privateMsg.Timestamp.ToString("o"),
                        status = "Sent",
                        channel = "Messenger"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommentReply] Private DM failed: {ex.Message}");
                }
            }

            // 3. Reaction
            if (!string.IsNullOrEmpty(request.Reaction))
            {
                try
                {
                    await _facebookGraphService.ReactToCommentAsync(
                        connectedPage.PageAccessToken,
                        lastComment.FacebookCommentId!,
                        request.Reaction);
                    reactionApplied = true;

                    // Save reaction message
                    var mappedReaction = Facebook.Services.FacebookGraphService.MapToFacebookReaction(request.Reaction);
                    var reactionMsg = new Message
                    {
                        ConversationId = id,
                        ExternalMessageId = $"msg_out_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = $"[تفاعل] {(mappedReaction == "LOVE" ? "❤️" : "👍")}",
                        MessageType = "Reaction",
                        Timestamp = DateTime.UtcNow
                    };
                    _context.Messages.Add(reactionMsg);
                    await _context.SaveChangesAsync();

                    // Broadcast via SignalR to Comment group
                    await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveMessage", new
                    {
                        id = reactionMsg.Id,
                        conversationId = id,
                        senderType = "Agent",
                        content = reactionMsg.Content,
                        createdAt = reactionMsg.Timestamp.ToString("o"),
                        status = "Sent",
                        messageType = "Reaction",
                        channel = "FacebookComment"
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommentReply] Reaction failed: {ex.Message}");
                }
            }

            conversation.LastMessageTimestamp = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                publicCommentSent = publicSent,
                privateDMSent = dmSent,
                reactionApplied = reactionApplied
            });
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
                ConversationId = id,
                ExternalMessageId = $"msg_reaction_{Guid.NewGuid():N}",
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
        public string? Channel { get; set; }
    }

    public class CommentReplyRequest
    {
        public string? PublicComment { get; set; }
        public string? PrivateDM { get; set; }
        public string? Reaction { get; set; }
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
