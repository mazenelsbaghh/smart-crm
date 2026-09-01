using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
using Modules.AI.Services;
using Modules.CRM.Services;
using Shared.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Modules.Conversations.API
{
    [ApiController]
    [Authorize]
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
        private readonly IAIBehaviorSettingsService _aiBehaviorSettingsService;
        private readonly IProjectAuthorizationService _projectAuthorization;

        public ConversationController(
            AppDbContext context, 
            Services.IAssignmentEngine assignmentEngine, 
            Shared.Queue.IEventBus eventBus, 
            IHubContext<NotificationHub> hubContext,
            IConfiguration configuration,
            StackExchange.Redis.IConnectionMultiplexer redis,
            Modules.Facebook.Services.IFacebookGraphService facebookGraphService,
            IAIBehaviorSettingsService aiBehaviorSettingsService,
            IProjectAuthorizationService projectAuthorization)
        {
            _context = context;
            _assignmentEngine = assignmentEngine;
            _eventBus = eventBus;
            _hubContext = hubContext;
            _configuration = configuration;
            _httpClient = new HttpClient();
            _redis = redis.GetDatabase();
            _facebookGraphService = facebookGraphService;
            _aiBehaviorSettingsService = aiBehaviorSettingsService;
            _projectAuthorization = projectAuthorization;
        }

        [HttpGet("projects/{projectId}/conversations")]
        public async Task<IActionResult> ListConversations(
            Guid projectId,
            [FromQuery] ConversationListQuery request)
        {
            if (!_projectAuthorization.CanRead(User, projectId)) return Forbid();
            var pageSize = Math.Clamp(request.Limit, 1, 100);
            IQueryable<Conversation> query = _context.Conversations
                .Where(c => c.ProjectId == projectId);

            if (request.ConversationId.HasValue)
            {
                query = query.Where(c => c.Id == request.ConversationId.Value);
            }

            if (request.CustomerId.HasValue)
            {
                query = query.Where(c => c.CustomerId == request.CustomerId.Value);
            }

            // Filter by channel
            if (!string.IsNullOrEmpty(request.Channel) && !request.Channel.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.Channel == request.Channel);
            }

            if (!string.IsNullOrEmpty(request.Status) && !request.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.Status == request.Status);
            }

            var joinedQuery = query.Join(_context.Customers,
                c => c.CustomerId,
                cust => cust.Id,
                (c, cust) => new
                {
                    Conversation = c,
                    Customer = cust
                });

            if (!string.IsNullOrEmpty(request.Search))
            {
                var searchLower = request.Search.ToLower();
                var phoneSearch = new string(request.Search.Where(char.IsDigit).ToArray());
                var internationalPhoneSearch = phoneSearch.StartsWith("0")
                    ? $"20{phoneSearch.Substring(1)}"
                    : phoneSearch;
                var localPhoneSearch = phoneSearch.StartsWith("20")
                    ? $"0{phoneSearch.Substring(2)}"
                    : phoneSearch;
                var hasPhoneSearch = !string.IsNullOrEmpty(phoneSearch);

                joinedQuery = joinedQuery.Where(x => 
                    (x.Customer.Name != null && x.Customer.Name.ToLower().Contains(searchLower)) || 
                    (hasPhoneSearch && x.Customer.PhoneNumber != null &&
                        (x.Customer.PhoneNumber.Contains(phoneSearch) ||
                         x.Customer.PhoneNumber.Contains(internationalPhoneSearch) ||
                         x.Customer.PhoneNumber.Contains(localPhoneSearch))));
            }

            if (request.Before.HasValue)
            {
                var beforeUtc = request.Before.Value.ToUniversalTime();
                joinedQuery = joinedQuery.Where(x => x.Conversation.LastMessageTimestamp < beforeUtc);
            }

            var conversations = await joinedQuery
                .OrderByDescending(x => x.Conversation.LastMessageTimestamp)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Conversation.Id,
                    x.Conversation.ProjectId,
                    x.Conversation.Status,
                    x.Conversation.Channel,
                    x.Conversation.WhatsAppAccountId,
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

            var whatsAppAccountIds = conversations
                .Where(conversation => conversation.WhatsAppAccountId.HasValue)
                .Select(conversation => conversation.WhatsAppAccountId!.Value)
                .Distinct()
                .ToArray();
            var whatsAppAccountNames = whatsAppAccountIds.Length == 0
                ? new Dictionary<Guid, string>()
                : await _context.WhatsAppAccounts
                    .IgnoreQueryFilters()
                    .Where(account => account.ProjectId == projectId
                        && whatsAppAccountIds.Contains(account.Id))
                    .ToDictionaryAsync(account => account.Id, account => account.Name);

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
                    channel = c.Channel,
                    whatsAppAccountId = c.WhatsAppAccountId,
                    whatsAppAccountName = c.WhatsAppAccountId.HasValue
                        && whatsAppAccountNames.TryGetValue(c.WhatsAppAccountId.Value, out var accountName)
                            ? accountName
                            : null,
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
            var projectId = await _context.Conversations
                .IgnoreQueryFilters()
                .Where(conversation => conversation.Id == conversationId)
                .Select(conversation => (Guid?)conversation.ProjectId)
                .FirstOrDefaultAsync();
            if (!projectId.HasValue) return NotFound();
            if (!_projectAuthorization.CanRead(User, projectId.Value)) return Forbid();

            var query = _context.Messages
                .IgnoreQueryFilters()
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
            if (!_projectAuthorization.CanRead(User, conversation.ProjectId)) return Forbid();
            if (conversation.WhatsAppDestinationId.HasValue)
                return Conflict(new { code = "WHATSAPP_CLOUD_OUTBOUND_NOT_CONFIGURED" });

            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == conversation.CustomerId
                    && candidate.ProjectId == conversation.ProjectId);
            if (customer is null) return NotFound("Customer not found.");

            string externalMessageId;
            Guid? whatsAppAccountId = null;
            if (conversation.Channel == "Messenger")
            {
                if (string.IsNullOrWhiteSpace(customer.FacebookPSID))
                    return BadRequest(new { code = "MESSENGER_RECIPIENT_NOT_AVAILABLE" });
                var connectedPage = await _context.ConnectedPages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(page => page.ProjectId == conversation.ProjectId && page.IsActive);
                if (connectedPage is null) return BadRequest(new { code = "MESSENGER_PAGE_NOT_CONNECTED" });
                try
                {
                    await _facebookGraphService.SendMessageAsync(
                        connectedPage.FacebookPageId,
                        connectedPage.PageAccessToken,
                        customer.FacebookPSID,
                        request.Content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConversationController] Facebook delivery failed: {ex.Message}");
                    return StatusCode(502, new { code = "MESSENGER_DELIVERY_FAILED" });
                }
                externalMessageId = $"msg_agent_{Guid.NewGuid():N}";
            }
            else if (conversation.Channel == "WhatsApp")
            {
                if (string.IsNullOrWhiteSpace(customer.PhoneNumber))
                    return BadRequest(new { code = "WHATSAPP_RECIPIENT_NOT_AVAILABLE" });
                whatsAppAccountId = conversation.WhatsAppAccountId ?? conversation.ProjectId;
                var sessionClient = HttpContext.RequestServices
                    .GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();
                var session = await sessionClient.GetAsync(conversation.ProjectId, whatsAppAccountId.Value);
                if (!session.Connected || !session.ConnectedAt.HasValue)
                    return StatusCode(503, new { code = "WHATSAPP_ACCOUNT_NOT_CONNECTED" });

                var clientCommandId = request.IdempotencyKey?.Trim();
                if (string.IsNullOrWhiteSpace(clientCommandId)
                    && Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                    clientCommandId = headerKey.ToString().Trim();
                if (string.IsNullOrWhiteSpace(clientCommandId))
                    return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED" });
                if (clientCommandId.Length > 64
                    || clientCommandId.Any(character => !char.IsLetterOrDigit(character)
                        && character != '-' && character != '_' && character != ':'))
                    return BadRequest(new { code = "INVALID_IDEMPOTENCY_KEY" });

                var gatewayDeliveryKey = $"manual:{conversation.Id:N}:{clientCommandId}";
                var gatewayPayload = new
                {
                    projectId = conversation.ProjectId,
                    whatsappAccountId = whatsAppAccountId,
                    to = customer.PhoneNumber,
                    message = request.Content,
                    idempotencyKey = gatewayDeliveryKey,
                    expectedConnectedAt = session.ConnectedAt
                };
                var jsonPayload = JsonSerializer.Serialize(
                    gatewayPayload,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                HttpResponseMessage response;
                try
                {
                    response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(
                        _httpClient,
                        $"{_configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000"}/api/whatsapp/send",
                        jsonPayload);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConversationController] WhatsApp delivery outcome is unknown: {ex.Message}");
                    await MarkDeliveryUnknownAsync(conversation, gatewayDeliveryKey);
                    return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
                }
                using (response)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 409 || (int)response.StatusCode >= 500
                            && (int)response.StatusCode != 503)
                            await MarkDeliveryUnknownAsync(conversation, gatewayDeliveryKey);
                        return StatusCode((int)response.StatusCode, new
                        {
                            code = (int)response.StatusCode is 412 or 503
                                ? "WHATSAPP_DELIVERY_DEFERRED"
                                : (int)response.StatusCode == 409 || (int)response.StatusCode >= 500
                                    ? "WHATSAPP_DELIVERY_UNKNOWN"
                                    : "WHATSAPP_DELIVERY_FAILED",
                            gatewayResponse = responseBody
                        });
                    }
                    externalMessageId = ProviderMessageId(responseBody)!;
                    if (string.IsNullOrWhiteSpace(externalMessageId))
                    {
                        await MarkDeliveryUnknownAsync(conversation, gatewayDeliveryKey);
                        return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
                    }
                }

                var existingMessage = await _context.Messages
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(existing => existing.ConversationId == conversation.Id
                        && existing.ExternalMessageId == externalMessageId);
                if (string.Equals(
                        conversation.WhatsAppDeliveryUnknownKey,
                        gatewayDeliveryKey,
                        StringComparison.Ordinal))
                {
                    conversation.WhatsAppDeliveryUnknownAt = null;
                    conversation.WhatsAppDeliveryUnknownKey = null;
                }
                if (existingMessage is not null)
                {
                    if (_context.ChangeTracker.HasChanges())
                        await _context.SaveChangesAsync();
                    return Ok(MessageResponse(existingMessage));
                }
            }
            else
            {
                return BadRequest(new { code = "UNSUPPORTED_CONVERSATION_CHANNEL" });
            }

            var sentAt = DateTime.UtcNow;
            var message = new Message
            {
                Id = whatsAppAccountId.HasValue
                    ? DeterministicMessageId(conversation.ProjectId, whatsAppAccountId.Value, externalMessageId)
                    : Guid.NewGuid(),
                ConversationId = conversation.Id,
                ExternalMessageId = externalMessageId,
                Direction = "Outgoing",
                Content = request.Content,
                MessageType = "Text",
                Timestamp = sentAt
            };
            _context.Messages.Add(message);
            conversation.LastMessageTimestamp = sentAt;
            _context.Entry(conversation).State = EntityState.Modified;

            // Complete existing pending follow-ups for this customer
            var pendingFollowUps = await _context.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.ProjectId == conversation.ProjectId
                    && f.CustomerId == conversation.CustomerId
                    && f.Status == "Pending"
                    && (f.ConversationId == conversation.Id
                        || (!f.ConversationId.HasValue
                            && f.Channel == conversation.Channel
                            && (conversation.Channel != "WhatsApp"
                                || (f.WhatsAppAccountId ?? f.ProjectId)
                                    == (conversation.WhatsAppAccountId ?? conversation.ProjectId)))))
                .ToListAsync();

            foreach (var fu in pendingFollowUps)
            {
                fu.Status = "Bypassed";
                _context.Entry(fu).State = EntityState.Modified;
            }

            // Schedule default follow-up in 24 hours only if AI auto-reply is enabled and customer is not blacklisted
            var settings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == conversation.ProjectId);
            bool shouldScheduleFollowUp = settings != null && settings.AiAutoReplyEnabled && !customer.IsBlacklisted;

            try
            {
                if (shouldScheduleFollowUp)
                {
                    var automationAccountId = conversation.Channel == "WhatsApp"
                        ? conversation.WhatsAppAccountId ?? conversation.ProjectId
                        : (Guid?)null;
                    await HttpContext.RequestServices
                        .GetRequiredService<AutomationFollowUpService>()
                        .UpsertPendingAutomationFollowUpAsync(
                            new PendingAutomationFollowUpRequest(
                                conversation.ProjectId,
                                conversation.CustomerId,
                                conversation.Channel == "WhatsApp"
                                    ? $"whatsapp-ai-nurture:{automationAccountId!.Value:N}:{conversation.Id:N}"
                                    : $"{conversation.Channel.ToLowerInvariant()}-ai-nurture:{conversation.Id:N}",
                                sentAt.AddHours(24),
                                "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                                ConversationId: conversation.Id,
                                WhatsAppAccountId: automationAccountId,
                                Channel: conversation.Channel),
                            HttpContext.RequestAborted);
                }
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException) when (whatsAppAccountId.HasValue)
            {
                _context.Entry(message).State = EntityState.Detached;
                var persistedMessage = await _context.Messages
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(existing => existing.Id == message.Id);
                if (persistedMessage is null) throw;
                return Ok(MessageResponse(persistedMessage));
            }

            var payload = MessageResponse(message);
            try
            {
                await _hubContext.Clients.Group($"project_{conversation.ProjectId}")
                    .SendAsync("ReceiveMessage", payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationController] Message was delivered, but SignalR notification failed: {ex.Message}");
            }
            return Ok(payload);
        }

        /// <summary>
        /// Composite comment reply: public comment + private DM + reaction
        /// </summary>
        [HttpPost("projects/{projectId}/conversations/{id}/comment-reply")]
        public async Task<IActionResult> CommentReply(Guid projectId, Guid id, [FromBody] CommentReplyRequest request)
        {
            if (!_projectAuthorization.CanRead(User, projectId)) return Forbid();
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null
                || conversation.ProjectId != projectId
                || conversation.Channel != "FacebookComment")
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
                var settings = await _context.ProjectSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.ProjectId == projectId);
                var aiBehavior = _aiBehaviorSettingsService.Resolve(settings, "FacebookComment");
                if (!_aiBehaviorSettingsService.IsReactionAllowed(aiBehavior, request.Reaction))
                {
                    return BadRequest(new { error = "Reaction is disabled or not allowed for this project/channel." });
                }

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
            var conversationProjectId = await _context.Conversations
                .IgnoreQueryFilters()
                .Where(conversation => conversation.Id == id)
                .Select(conversation => (Guid?)conversation.ProjectId)
                .FirstOrDefaultAsync();
            if (!conversationProjectId.HasValue) return NotFound();
            var projectId = conversationProjectId.Value;
            if (projectIdHeader.HasValue && projectIdHeader.Value != projectId) return Forbid();
            if (!_projectAuthorization.CanRead(User, projectId)) return Forbid();

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
            if (!_projectAuthorization.CanRead(User, projectId)) return Forbid();
            if (_projectAuthorization.GetUserId(User) != agentId
                && !_projectAuthorization.CanManageProject(User, projectId))
                return Forbid();
            await _assignmentEngine.UpdatePresenceAsync(projectId, agentId, request.IsOnline);
            return Ok(new { projectId, agentId, request.IsOnline });
        }

        [HttpGet("projects/{projectId}/agents/workload")]
        public async Task<IActionResult> GetWorkloadReport(Guid projectId)
        {
            if (!_projectAuthorization.CanRead(User, projectId)) return Forbid();
            var report = await _assignmentEngine.GetWorkloadReportAsync(projectId);
            return Ok(report);
        }

        [HttpPut("conversations/{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateConversationStatusRequest request)
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null) return NotFound();
            if (!_projectAuthorization.CanRead(User, conversation.ProjectId)) return Forbid();

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
            if (!_projectAuthorization.CanRead(User, conversation.ProjectId)) return Forbid();
            if (conversation.WhatsAppDestinationId.HasValue)
                return Conflict(new { code = "WHATSAPP_CLOUD_OUTBOUND_NOT_CONFIGURED" });

            // Find the message in the DB to get its details (direction, external message id, etc.)
            var targetMessage = await _context.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.ConversationId == conversation.Id && m.ExternalMessageId == messageId);
            if (targetMessage == null)
            {
                return NotFound($"Message {messageId} not found in conversation {id}.");
            }

            var settings = await _context.ProjectSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == conversation.ProjectId);
            var aiBehavior = _aiBehaviorSettingsService.Resolve(settings, conversation.Channel ?? "WhatsApp");
            if (!_aiBehaviorSettingsService.IsReactionAllowed(aiBehavior, request.ReactionText))
            {
                return BadRequest(new { error = "Reaction is disabled or not allowed for this project/channel." });
            }
            if (conversation.Channel != "WhatsApp")
                return BadRequest(new { code = "UNSUPPORTED_REACTION_CHANNEL" });

            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == conversation.CustomerId
                    && c.ProjectId == conversation.ProjectId);
            if (customer is null || string.IsNullOrWhiteSpace(customer.PhoneNumber))
                return BadRequest(new { code = "WHATSAPP_RECIPIENT_NOT_AVAILABLE" });

            var accountId = conversation.WhatsAppAccountId ?? conversation.ProjectId;
            var sessionClient = HttpContext.RequestServices
                .GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();
            var session = await sessionClient.GetAsync(conversation.ProjectId, accountId);
            if (!session.Connected || !session.ConnectedAt.HasValue)
                return StatusCode(503, new { code = "WHATSAPP_ACCOUNT_NOT_CONNECTED" });

            var clientCommandId = request.IdempotencyKey?.Trim();
            if (string.IsNullOrWhiteSpace(clientCommandId)
                && Request.Headers.TryGetValue("Idempotency-Key", out var reactionHeaderKey))
                clientCommandId = reactionHeaderKey.ToString().Trim();
            if (string.IsNullOrWhiteSpace(clientCommandId))
                return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED" });
            if (clientCommandId.Length > 64
                || clientCommandId.Any(character => !char.IsLetterOrDigit(character)
                    && character != '-' && character != '_' && character != ':'))
                return BadRequest(new { code = "INVALID_IDEMPOTENCY_KEY" });

            var gatewayPayload = new
            {
                projectId = conversation.ProjectId,
                whatsappAccountId = accountId,
                to = customer.PhoneNumber,
                reactionText = request.ReactionText,
                targetMessageId = targetMessage.ExternalMessageId,
                targetFromMe = targetMessage.Direction == "Outgoing",
                idempotencyKey = $"manual-reaction:{conversation.Id:N}:{clientCommandId}",
                expectedConnectedAt = session.ConnectedAt
            };
            var jsonPayload = JsonSerializer.Serialize(
                gatewayPayload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string responseBody;
            try
            {
                using var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(
                    _httpClient,
                    $"{_configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000"}/api/whatsapp/react",
                    jsonPayload);
                responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new
                    {
                        code = (int)response.StatusCode is 412 or 503
                            ? "WHATSAPP_DELIVERY_DEFERRED"
                            : (int)response.StatusCode == 409 || (int)response.StatusCode >= 500
                                ? "WHATSAPP_DELIVERY_UNKNOWN"
                                : "WHATSAPP_DELIVERY_FAILED",
                        gatewayResponse = responseBody
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConversationController] WhatsApp reaction outcome is unknown: {ex.Message}");
                return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
            }

            var externalMessageId = ProviderMessageId(responseBody);
            if (string.IsNullOrWhiteSpace(externalMessageId))
                return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
            var reactionMessage = await _context.Messages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(existing => existing.ConversationId == conversation.Id
                    && existing.ExternalMessageId == externalMessageId);
            if (reactionMessage is not null) return Ok(MessageResponse(reactionMessage));
            if (reactionMessage is null)
            {
                reactionMessage = new Message
                {
                    Id = DeterministicMessageId(conversation.ProjectId, accountId, externalMessageId),
                    ConversationId = id,
                    ExternalMessageId = externalMessageId,
                    Direction = "Outgoing",
                    Content = $"[تفاعل] {request.ReactionText}",
                    MessageType = "Reaction",
                    Timestamp = DateTime.UtcNow
                };
                _context.Messages.Add(reactionMessage);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _context.Entry(reactionMessage).State = EntityState.Detached;
                    reactionMessage = await _context.Messages
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(existing => existing.Id == DeterministicMessageId(
                            conversation.ProjectId,
                            accountId,
                            externalMessageId));
                    if (reactionMessage is null) throw;
                }
            }

            // Broadcast via SignalR to project group
            var payload = MessageResponse(reactionMessage);

            await _hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", payload);

            return Ok(payload);
        }

        private async Task MarkDeliveryUnknownAsync(
            Conversation conversation,
            string deliveryKey)
        {
            conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow;
            conversation.WhatsAppDeliveryUnknownKey = deliveryKey;
            await _context.SaveChangesAsync();
        }

        private static string? ProviderMessageId(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return document.RootElement.TryGetProperty("messageId", out var property)
                    && property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static Guid DeterministicMessageId(
            Guid projectId,
            Guid whatsAppAccountId,
            string externalMessageId)
        {
            var value = $"whatsapp-outgoing:{projectId:N}:{whatsAppAccountId:N}:{externalMessageId}";
            var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return new Guid(bytes.AsSpan(0, 16));
        }

        private static object MessageResponse(Message message) => new
        {
            id = message.Id,
            conversationId = message.ConversationId,
            senderType = "Agent",
            content = message.Content,
            createdAt = message.Timestamp.ToString("o"),
            status = "Sent",
            mediaUrl = (string?)null,
            mediaType = (string?)null,
            messageType = message.MessageType
        };
    }

    public class ReactToMessageRequest
    {
        public string ReactionText { get; set; } = string.Empty;
        public string? IdempotencyKey { get; set; }
    }

    public class ConversationListQuery
    {
        public string Status { get; set; } = "All";
        public string Channel { get; set; } = "WhatsApp";
        public string? Search { get; set; }
        public DateTime? Before { get; set; }
        public int Limit { get; set; } = 20;
        public Guid? ConversationId { get; set; }
        public Guid? CustomerId { get; set; }
    }

    public class SendMessageRequest
    {
        public string Content { get; set; } = string.Empty;
        public string? Channel { get; set; }
        public string? IdempotencyKey { get; set; }
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
