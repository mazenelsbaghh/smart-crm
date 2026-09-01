using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.Conversations.Hubs;
using Modules.CRM.Services;
using Modules.WhatsApp.Services;
using Microsoft.AspNetCore.SignalR;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Linq;
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
        private readonly CustomerOptOutService _customerOptOutService;
        private readonly IDatabase _redis;
        private readonly WhatsAppInboundEventPublisher _inboundPublisher;
        private readonly WhatsAppAccountService _whatsAppAccounts;
        private readonly WhatsAppCustomerMergeService _customerMerge;
        private readonly AutomationFollowUpService _automationFollowUps;

        public WebhookController(
            AppDbContext context, 
            ITenantContext tenantContext, 
            IMessageAggregator messageAggregator, 
            IHubContext<NotificationHub> hubContext, 
            IAssignmentEngine assignmentEngine,
            CustomerOptOutService customerOptOutService,
            IConnectionMultiplexer redisConnection,
            WhatsAppInboundEventPublisher inboundPublisher,
            WhatsAppAccountService whatsAppAccounts,
            WhatsAppCustomerMergeService customerMerge,
            AutomationFollowUpService automationFollowUps)
        {
            _context = context;
            _tenantContext = tenantContext;
            _messageAggregator = messageAggregator;
            _hubContext = hubContext;
            _assignmentEngine = assignmentEngine;
            _customerOptOutService = customerOptOutService;
            _redis = redisConnection.GetDatabase();
            _inboundPublisher = inboundPublisher;
            _whatsAppAccounts = whatsAppAccounts;
            _customerMerge = customerMerge;
            _automationFollowUps = automationFollowUps;
        }

        [HttpPost("message")]
        [WhatsAppGatewayAuthenticated]
        public async Task<IActionResult> ReceiveMessage([FromBody] IncomingMessagePayload payload)
        {
            // Set context tenant project id
            _tenantContext.SetProjectId(payload.ProjectId);
            // A gateway version that predates multi-account support can only be the
            // stable legacy session. Missing identity must never follow a later UI
            // default switch to a different phone number.
            var inboundAccountId = payload.WhatsAppAccountId
                ?? WhatsAppAccountService.LegacyAccountId(payload.ProjectId);
            var whatsAppAccount = await _whatsAppAccounts.ResolveAsync(
                payload.ProjectId,
                inboundAccountId,
                HttpContext.RequestAborted);
            if (whatsAppAccount is null)
                return BadRequest(new { code = "WHATSAPP_ACCOUNT_NOT_IN_PROJECT" });
            var whatsAppAccountId = whatsAppAccount.Id;
            if (string.IsNullOrWhiteSpace(payload.MessageId)) return BadRequest(new { code = "WHATSAPP_MESSAGE_ID_REQUIRED" });
            var receivedAtUtc = DateTime.UtcNow;
            var hasValidProviderTimestamp = TryProviderTimestampUtc(
                payload.Timestamp,
                receivedAtUtc,
                out var messageTimestampUtc);
            var arrivedDuringCurrentConnection = hasValidProviderTimestamp
                && payload.ConnectionOpenedAt.HasValue
                && WhatsAppConnectionEpoch.Includes(messageTimestampUtc, payload.ConnectionOpenedAt.Value);
            var duplicate = await _context.Messages.IgnoreQueryFilters()
                .Join(
                    _context.Conversations.IgnoreQueryFilters(),
                    message => message.ConversationId,
                    conversation => conversation.Id,
                    (message, conversation) => new { message, conversation })
                .Join(
                    _context.Customers.IgnoreQueryFilters(),
                    row => row.conversation.CustomerId,
                    customer => customer.Id,
                    (row, customer) => new { row.message, row.conversation, customer })
                .FirstOrDefaultAsync(row => row.message.ExternalMessageId == payload.MessageId
                    && row.conversation.ProjectId == payload.ProjectId
                    && (row.conversation.WhatsAppAccountId == whatsAppAccountId
                        || (row.conversation.WhatsAppAccountId == null
                            && whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId))));
            if (duplicate is not null)
            {
                if (!duplicate.customer.IsBlacklisted
                    && duplicate.message.MessageType != "Reaction"
                    && payload.ConnectionOpenedAt.HasValue
                    && WhatsAppConnectionEpoch.Includes(
                        duplicate.message.Timestamp,
                        payload.ConnectionOpenedAt.Value))
                {
                    await ResumeAutomationAsync(
                        duplicate.message,
                        duplicate.conversation,
                        duplicate.customer,
                        whatsAppAccountId,
                        payload.ConnectionOpenedAt!.Value,
                        HttpContext.RequestAborted);
                }
                return Ok(new { status = "duplicate", messageId = payload.MessageId });
            }
            var normalizedSender = NormalizeWhatsAppPhone(payload.Sender);
            var senderLid = !string.IsNullOrWhiteSpace(payload.SenderLid)
                ? payload.SenderLid.Trim()
                : normalizedSender.EndsWith("@lid", StringComparison.OrdinalIgnoreCase) ? normalizedSender : null;
            var sharedContact = WhatsAppSharedContactParser.ExtractOwnContact(payload.Content);
            var sharedOwnPhone = sharedContact?.PhoneNumber;
            var sharedOwnName = sharedContact?.Name;

            // 1. A phone number identifies one shared project customer. LIDs/JIDs are
            // account-scoped and must never match an identity from another account.
            var senderIdentity = senderLid
                ?? (normalizedSender.EndsWith("@lid", StringComparison.OrdinalIgnoreCase)
                    ? normalizedSender
                    : null);
            var resolvedPhone = sharedOwnPhone
                ?? (!normalizedSender.EndsWith("@lid", StringComparison.OrdinalIgnoreCase)
                    ? normalizedSender
                    : null);
            var phoneCustomer = resolvedPhone is null
                ? null
                : await _customerMerge.ResolveByPhoneAsync(
                    payload.ProjectId,
                    resolvedPhone,
                    HttpContext.RequestAborted);
            Customer? customer = phoneCustomer;

            Modules.WhatsApp.Domain.WhatsAppCustomerIdentity? accountIdentity = null;
            if (senderIdentity is not null)
            {
                accountIdentity = await _context.WhatsAppCustomerIdentities
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(identity =>
                        identity.ProjectId == payload.ProjectId
                        && identity.WhatsAppAccountId == whatsAppAccountId
                        && identity.ExternalId == senderIdentity);
                if (customer is null && accountIdentity is not null)
                {
                    customer = await _context.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(candidate =>
                            candidate.ProjectId == payload.ProjectId
                            && candidate.Id == accountIdentity.CustomerId);
                }
                else if (phoneCustomer is not null
                    && accountIdentity is not null
                    && accountIdentity.CustomerId != phoneCustomer.Id)
                {
                    customer = await _customerMerge.BindPhoneAsync(
                        payload.ProjectId,
                        accountIdentity.CustomerId,
                        resolvedPhone!,
                        HttpContext.RequestAborted);
                    accountIdentity.CustomerId = customer.Id;
                }
            }

            // Compatibility for a legacy default-account LID before identity backfill.
            if (customer is null && whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId))
            {
                customer = await _context.Customers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == payload.ProjectId
                        && (c.WhatsAppLid == normalizedSender
                            || (senderLid != null && c.WhatsAppLid == senderLid)));
            }

            if (customer == null)
            {
                var stableCustomerIdentity = sharedOwnPhone is not null
                    ? $"phone:{sharedOwnPhone}"
                    : senderIdentity is not null
                        ? $"lid:{whatsAppAccountId:N}:{senderIdentity}"
                        : $"phone:{normalizedSender}";
                customer = new Customer
                {
                    Id = DeterministicId($"whatsapp-customer:{payload.ProjectId:N}:{stableCustomerIdentity}"),
                    ProjectId = payload.ProjectId,
                    PhoneNumber = sharedOwnPhone ?? normalizedSender,
                    WhatsAppLid = whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId)
                        ? senderLid
                        : null,
                    Name = sharedOwnName ?? (!string.IsNullOrWhiteSpace(payload.Name)
                        ? payload.Name.Trim() 
                        : $"WA Customer {normalizedSender.Substring(Math.Max(0, normalizedSender.Length - 4))}"),
                    City = string.Empty,
                    Notes = string.Empty
                };
                _context.Customers.Add(customer);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _context.Entry(customer).State = EntityState.Detached;
                    customer = await _context.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(candidate => candidate.Id == DeterministicId(
                            $"whatsapp-customer:{payload.ProjectId:N}:{stableCustomerIdentity}"));
                    if (customer is null) throw;
                }
            }
            else
            {
                bool modified = false;
                bool replacedLidPhone = false;
                if (senderLid != null
                    && whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId)
                    && customer.WhatsAppLid != senderLid)
                {
                    customer.WhatsAppLid = senderLid;
                    modified = true;
                }

                if (customer.PhoneNumber.EndsWith("@lid", StringComparison.OrdinalIgnoreCase)
                    && resolvedPhone is not null
                    && resolvedPhone != customer.PhoneNumber)
                {
                    customer.PhoneNumber = resolvedPhone;
                    replacedLidPhone = true;
                    modified = true;
                }

                if (sharedOwnName != null && customer.Name != sharedOwnName)
                {
                    customer.Name = sharedOwnName;
                    modified = true;
                }

                if (replacedLidPhone || sharedOwnName != null)
                {
                    var bookings = await _context.GroupAppointmentBookings
                        .IgnoreQueryFilters()
                        .Where(booking => booking.ProjectId == payload.ProjectId && booking.CustomerId == customer.Id)
                        .ToListAsync();
                    foreach (var booking in bookings)
                    {
                        if (replacedLidPhone) booking.CustomerPhone = resolvedPhone!;
                        if (sharedOwnName != null) booking.CustomerName = sharedOwnName;
                    }
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

            if (resolvedPhone is not null)
            {
                customer = await _customerMerge.BindPhoneAsync(
                    payload.ProjectId,
                    customer.Id,
                    resolvedPhone,
                    HttpContext.RequestAborted);
                phoneCustomer = customer;
            }

            if (senderIdentity is not null)
            {
                if (accountIdentity is null)
                {
                    accountIdentity = new Modules.WhatsApp.Domain.WhatsAppCustomerIdentity
                    {
                        Id = DeterministicId($"whatsapp-identity:{payload.ProjectId:N}:{whatsAppAccountId:N}:{senderIdentity}"),
                        ProjectId = payload.ProjectId,
                        WhatsAppAccountId = whatsAppAccountId,
                        CustomerId = customer.Id,
                        ExternalId = senderIdentity,
                        Kind = "Lid"
                    };
                    _context.WhatsAppCustomerIdentities.Add(accountIdentity);
                }
                else if (accountIdentity.CustomerId != customer.Id)
                {
                    accountIdentity.CustomerId = customer.Id;
                    accountIdentity.UpdatedAt = DateTime.UtcNow;
                }
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _context.Entry(accountIdentity).State = EntityState.Detached;
                    var concurrentIdentity = await _context.WhatsAppCustomerIdentities
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(identity => identity.ProjectId == payload.ProjectId
                            && identity.WhatsAppAccountId == whatsAppAccountId
                            && identity.ExternalId == senderIdentity);
                    if (concurrentIdentity is null) throw;
                    accountIdentity = concurrentIdentity;
                    if (concurrentIdentity.CustomerId != customer.Id)
                    {
                        if (phoneCustomer is not null)
                        {
                            customer = await _customerMerge.BindPhoneAsync(
                                payload.ProjectId,
                                concurrentIdentity.CustomerId,
                                resolvedPhone!,
                                HttpContext.RequestAborted);
                            accountIdentity = await _context.WhatsAppCustomerIdentities
                                .IgnoreQueryFilters()
                                .FirstAsync(identity => identity.Id == concurrentIdentity.Id);
                        }
                        else
                        {
                            customer = await _context.Customers
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(candidate => candidate.ProjectId == payload.ProjectId
                                    && candidate.Id == concurrentIdentity.CustomerId);
                            if (customer is null) throw;
                        }
                    }
                }
            }

            await _customerOptOutService.ApplyIfRequestedAsync(customer, payload.Content, HttpContext.RequestAborted);

            // 2. Resolve or create active Conversation thread
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == payload.ProjectId
                    && c.CustomerId == customer.Id
                    && c.Channel == "WhatsApp"
                    && (c.WhatsAppAccountId == whatsAppAccountId
                        || (c.WhatsAppAccountId == null
                            && whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId)))
                    && c.Status != "Closed");

            if (conversation == null)
            {
                var stableConversationId = DeterministicId(
                    $"whatsapp-conversation:{payload.ProjectId:N}:{customer.Id:N}:{whatsAppAccountId:N}");
                var stableConversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(candidate => candidate.Id == stableConversationId);
                if (stableConversation is not null)
                {
                    conversation = stableConversation;
                    conversation.Status = "Open";
                    if (messageTimestampUtc > conversation.LastMessageTimestamp)
                        conversation.LastMessageTimestamp = messageTimestampUtc;
                    conversation.WhatsAppAccountId = whatsAppAccountId;
                    _context.Entry(conversation).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    conversation = new Conversation
                    {
                        Id = stableConversationId,
                        ProjectId = payload.ProjectId,
                        CustomerId = customer.Id,
                        WhatsAppAccountId = whatsAppAccountId,
                        Channel = "WhatsApp",
                        Status = "Open",
                        LastMessageTimestamp = messageTimestampUtc
                    };
                    _context.Conversations.Add(conversation);
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (DbUpdateException)
                    {
                        _context.Entry(conversation).State = EntityState.Detached;
                        conversation = await _context.Conversations
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(candidate => candidate.Id == stableConversationId);
                        if (conversation is null) throw;
                    }
                }
            }
            else
            {
                // Rollout compatibility: claim a pre-migration WhatsApp thread for
                // the stable legacy account instead of creating a duplicate thread.
                conversation.WhatsAppAccountId ??= whatsAppAccountId;
                if (messageTimestampUtc > conversation.LastMessageTimestamp)
                {
                    conversation.LastMessageTimestamp = messageTimestampUtc;
                }
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
            var isFirstConversationMessage = !await _context.Messages.IgnoreQueryFilters()
                .AnyAsync(existing => existing.ConversationId == conversation.Id);
            var message = new Message
            {
                Id = DeterministicId(
                    $"whatsapp-message:{payload.ProjectId:N}:{whatsAppAccountId:N}:{payload.MessageId}"),
                ConversationId = conversation.Id,
                ExternalMessageId = payload.MessageId,
                Direction = "Incoming",
                Content = payload.Content,
                MessageType = payload.MessageType ?? "Text",
                Timestamp = messageTimestampUtc,
                AssetId = payload.AssetId
            };
            await using var messageTransaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(HttpContext.RequestAborted)
                : null;

            if (arrivedDuringCurrentConnection
                || (hasValidProviderTimestamp
                    && conversation.WhatsAppDeliveryUnknownAt.HasValue
                    && messageTimestampUtc > conversation.WhatsAppDeliveryUnknownAt.Value))
            {
                conversation.WhatsAppDeliveryUnknownAt = null;
                conversation.WhatsAppDeliveryUnknownKey = null;
            }

            _context.Messages.Add(message);
            var destinations = await _context.AdvertisingWhatsAppDestinations.IgnoreQueryFilters()
                .Where(item => item.ProjectId == payload.ProjectId
                    && item.State == Modules.Advertising.Domain.AuthorizedDestinationState.Eligible
                    && (item.WhatsAppAccountId == whatsAppAccountId
                        || (item.WhatsAppAccountId == null
                            && whatsAppAccountId == WhatsAppAccountService.LegacyAccountId(payload.ProjectId))))
                .OrderByDescending(item => item.LastValidatedAtUtc).Take(2).ToListAsync();
            var destination = destinations.Count == 1 ? destinations[0] : null;
            if (destination is not null && payload.AdvertisingContext is not null)
                _inboundPublisher.PublishObservation(payload.ProjectId, conversation.Id, customer.Id, destination.Id,
                    destination.Version, payload.MessageId, messageTimestampUtc,
                    new InboundAdvertisingContext(payload.AdvertisingContext.IdentifierState ?? "Missing",
                        payload.AdvertisingContext.CtwaClid, payload.AdvertisingContext.ProviderAdId,
                        null, "BaileysExperimental"), isFirstConversationMessage);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (messageTransaction is not null)
                {
                    await messageTransaction.RollbackAsync(HttpContext.RequestAborted);
                    await messageTransaction.DisposeAsync();
                }
                _context.ChangeTracker.Clear();
                var duplicatePersisted = await _context.Messages
                    .IgnoreQueryFilters()
                    .Join(
                        _context.Conversations.IgnoreQueryFilters(),
                        persisted => persisted.ConversationId,
                        persistedConversation => persistedConversation.Id,
                        (persisted, persistedConversation) => new { persisted, persistedConversation })
                    .Join(
                        _context.Customers.IgnoreQueryFilters(),
                        row => row.persistedConversation.CustomerId,
                        persistedCustomer => persistedCustomer.Id,
                        (row, persistedCustomer) => new
                        {
                            row.persisted,
                            row.persistedConversation,
                            persistedCustomer
                        })
                    .FirstOrDefaultAsync(row => row.persisted.Id == DeterministicId(
                        $"whatsapp-message:{payload.ProjectId:N}:{whatsAppAccountId:N}:{payload.MessageId}"));
                if (duplicatePersisted is null) throw;
                if (!duplicatePersisted.persistedCustomer.IsBlacklisted
                    && duplicatePersisted.persisted.MessageType != "Reaction"
                    && payload.ConnectionOpenedAt.HasValue
                    && WhatsAppConnectionEpoch.Includes(
                        duplicatePersisted.persisted.Timestamp,
                        payload.ConnectionOpenedAt.Value))
                {
                    await ResumeAutomationAsync(
                        duplicatePersisted.persisted,
                        duplicatePersisted.persistedConversation,
                        duplicatePersisted.persistedCustomer,
                        whatsAppAccountId,
                        payload.ConnectionOpenedAt!.Value,
                        HttpContext.RequestAborted);
                }
                return Ok(new { status = "duplicate", messageId = payload.MessageId });
            }

            if (arrivedDuringCurrentConnection)
            {
                await ResumeAutomationAsync(
                    message,
                    conversation,
                    customer,
                    whatsAppAccountId,
                    payload.ConnectionOpenedAt!.Value,
                    HttpContext.RequestAborted);
            }
            await _context.SaveChangesAsync();
            if (messageTransaction is not null)
                await messageTransaction.CommitAsync(HttpContext.RequestAborted);

            // 3.5 Broadcast via SignalR to the group
            await _hubContext.Clients.Group($"project_{payload.ProjectId}").SendAsync("ReceiveMessage", new
            {
                id = message.Id,
                conversationId = message.ConversationId,
                whatsAppAccountId,
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
                if (arrivedDuringCurrentConnection
                    && settings != null
                    && settings.AiAutoReplyEnabled
                    && !customer.IsBlacklisted)
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

            }

            return Ok(new { status = "Received" });
        }

        private async Task ResumeAutomationAsync(
            Message message,
            Conversation conversation,
            Customer customer,
            Guid whatsAppAccountId,
            DateTimeOffset requiredConnectedAt,
            CancellationToken cancellationToken)
        {
            if (customer.IsBlacklisted || message.MessageType == "Reaction") return;

            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? ownedTransaction = null;
            if (_context.Database.IsRelational() && _context.Database.CurrentTransaction is null)
                ownedTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _messageAggregator.AggregateMessageAsync(
                    conversation.ProjectId,
                    customer.PhoneNumber,
                    message.Content,
                    message.Id,
                    message.Timestamp,
                    requiredConnectedAt,
                    conversation.Id,
                    whatsAppAccountId,
                    cancellationToken);

                var projectSettings = await _context.ProjectSettings.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(settings => settings.ProjectId == conversation.ProjectId, cancellationToken);
                if (projectSettings?.AiAutoReplyEnabled == true)
                {
                    await _automationFollowUps.UpsertPendingAutomationFollowUpAsync(
                        new PendingAutomationFollowUpRequest(
                            conversation.ProjectId,
                            customer.Id,
                            $"whatsapp-ai-nurture:{whatsAppAccountId:N}:{conversation.Id:N}",
                            DateTime.UtcNow.AddHours(24),
                            "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                            ConversationId: conversation.Id,
                            WhatsAppAccountId: whatsAppAccountId,
                            Channel: "WhatsApp"),
                        cancellationToken);
                }

                if (ownedTransaction is not null)
                    await ownedTransaction.CommitAsync(cancellationToken);
            }
            catch
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.RollbackAsync(cancellationToken);
                throw;
            }
            finally
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.DisposeAsync();
            }
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

        private static bool TryProviderTimestampUtc(
            long unixSeconds,
            DateTime receivedAtUtc,
            out DateTime timestampUtc)
        {
            timestampUtc = receivedAtUtc;
            if (unixSeconds <= 0) return false;
            try
            {
                var providerTimestampUtc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
                if (providerTimestampUtc > receivedAtUtc.AddMinutes(5)) return false;
                timestampUtc = providerTimestampUtc;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static Guid DeterministicId(string value)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value));
            return new Guid(bytes.AsSpan(0, 16));
        }

    }

    public class IncomingMessagePayload
    {
        public Guid ProjectId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("whatsappAccountId")]
        public Guid? WhatsAppAccountId { get; set; }
        public string MessageId { get; set; } = default!;
        public string Sender { get; set; } = default!;
        public string? SenderJid { get; set; }
        public string? SenderLid { get; set; }
        public string? Name { get; set; }
        public string Content { get; set; } = default!;
        public string? MessageType { get; set; }
        public long Timestamp { get; set; }
        public DateTimeOffset? ConnectionOpenedAt { get; set; }
        public Guid? AssetId { get; set; }
        public IncomingAdvertisingContext? AdvertisingContext { get; set; }
    }

    public class IncomingAdvertisingContext
    {
        public string? IdentifierState { get; set; }
        public string? CtwaClid { get; set; }
        public string? ProviderAdId { get; set; }
        public bool OpaqueMarker { get; set; }
    }
}
