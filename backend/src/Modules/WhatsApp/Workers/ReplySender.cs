using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using Modules.Conversations.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Modules.WhatsApp.Services;
using Modules.CRM.Domain;

namespace Modules.WhatsApp.Workers
{
    public class ReplySender : IIntegrationEventHandler<AIReplyGeneratedEvent>
    {
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;
        private readonly IHumanMessagingEngine _messagingEngine;
        private readonly IServiceProvider _serviceProvider;

        public ReplySender(IConfiguration configuration, IHumanMessagingEngine messagingEngine, IServiceProvider serviceProvider)
            : this(new HttpClient(), configuration, messagingEngine, serviceProvider)
        {
        }

        internal ReplySender(
            HttpClient httpClient,
            IConfiguration configuration,
            IHumanMessagingEngine messagingEngine,
            IServiceProvider serviceProvider)
        {
            _httpClient = httpClient;
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            _messagingEngine = messagingEngine;
            _serviceProvider = serviceProvider;
        }

        public async Task HandleAsync(AIReplyGeneratedEvent @event)
        {
            // Skip non-WhatsApp channels — handled by FacebookReplySender
            var channel = @event.Channel ?? "WhatsApp";
            if (channel != "WhatsApp")
            {
                Console.WriteLine($"[ReplySender] Skipping non-WhatsApp channel: {channel}");
                return;
            }

            @event.WhatsAppAccountId ??= @event.ProjectId;
            if (!@event.RequiredWhatsAppConnectedAt.HasValue)
            {
                Console.WriteLine($"[ReplySender] Dropping unfenced WhatsApp event {@event.Id}; a connection epoch is required.");
                return;
            }

            Console.WriteLine($"[ReplySender] Received AIReplyGeneratedEvent for Project: {@event.ProjectId}, Sender: {@event.Sender}");
            var queuedTooLong = DateTime.UtcNow - @event.OccurredOn > TimeSpan.FromSeconds(10);
            var replyContent = OutgoingMessageText.Normalize(@event.Content);
            var chunks = System.Linq.Enumerable.ToList(_messagingEngine.SplitIntoChunks(replyContent));
            var deliveryKey = @event.WhatsAppDeliveryIdempotencyKey ?? $"reply_{@event.Id:N}";
            if (chunks.Count == 0)
            {
                return;
            }

            try
            {
                // Fetch last incoming message to calculate Thinking/Reading delay
                using (var scope = _serviceProvider.CreateScope())
                {
                    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    tenantContext.SetProjectId(@event.ProjectId);

                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var customer = await dbContext.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.PhoneNumber == @event.Sender);

                    if (customer?.IsBlacklisted == true)
                    {
                        Console.WriteLine($"[ReplySender] Customer {customer.Id} is blacklisted. Dropping queued AI reply.");
                        return;
                    }

                    if (customer != null && await HasPaidBookingAsync(dbContext, customer.Id))
                    {
                        Console.WriteLine($"[ReplySender] Customer {customer.Id} has a paid booking. Dropping queued AI reply.");
                        return;
                    }

                    if (customer != null)
                    {
                        var conversation = await FindConversationAsync(dbContext, @event, customer.Id);

                        if (conversation != null)
                        {
                            var lastIncoming = await dbContext.Messages
                                .IgnoreQueryFilters()
                                .Where(m => m.ConversationId == conversation.Id && m.Direction == "Incoming")
                                .OrderByDescending(m => m.Timestamp)
                                .FirstOrDefaultAsync();

                            int thinkingDelay = 0;
                            if (lastIncoming != null)
                            {
                                thinkingDelay = queuedTooLong ? 0 : _messagingEngine.CalculateThinkingDelay(lastIncoming.Content, @event.ProjectId);
                            }

                            int totalTypingDelay = 0;
                            for (int idx = 0; !queuedTooLong && idx < chunks.Count; idx++)
                            {
                                totalTypingDelay += _messagingEngine.CalculateTypingDelay(chunks[idx], @event.ProjectId);
                                if (idx > 0)
                                {
                                    totalTypingDelay += 3000; // Average stagger delay
                                }
                            }

                            int totalRemainingMs = thinkingDelay + totalTypingDelay;
                            int estSec = (int)Math.Ceiling(totalRemainingMs / 1000.0);

                             if (estSec > 0)
                             {
                                 var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                                 try
                                 {
                                     await redis.StringSetAsync($"ai_typing:{conversation.Id}", "typing", TimeSpan.FromSeconds(estSec));
                                 }
                                 catch (Exception redisEx)
                                 {
                                     Console.WriteLine($"[ReplySender] Redis set initial failed: {redisEx.Message}");
                                 }
                             }

                             var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();
                             await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                             {
                                 conversationId = conversation.Id,
                                 isTyping = true,
                                 estimatedSeconds = estSec,
                                 stage = "typing"
                             });

                            if (thinkingDelay > 0)
                            {
                                Console.WriteLine($"[ReplySender] Simulating smart thinking delay of {thinkingDelay}ms...");
                                await Task.Delay(thinkingDelay);
                            }
                        }
                    }
                }

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];

                    // Smart typing delay occurs BEFORE sending the chunk!
                    int delayMs = queuedTooLong ? 0 : _messagingEngine.CalculateTypingDelay(chunk, @event.ProjectId);
                    if (delayMs > 0) Console.WriteLine($"[ReplySender] Simulating human typing delay of {delayMs}ms...");

                    // Broadcast remaining typing delay before delaying
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                        tenantContext.SetProjectId(@event.ProjectId);
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                        var customer = await dbContext.Customers
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.PhoneNumber == @event.Sender);

                        if (customer != null)
                        {
                            var conversation = await FindConversationAsync(dbContext, @event, customer.Id);

                            if (conversation != null)
                            {
                                int remainingTypingMs = 0;
                                for (int j = i; !queuedTooLong && j < chunks.Count; j++)
                                {
                                    remainingTypingMs += _messagingEngine.CalculateTypingDelay(chunks[j], @event.ProjectId);
                                    if (j > i)
                                    {
                                        remainingTypingMs += 3000; // Average stagger delay
                                    }
                                }
                                 int estSec = (int)Math.Ceiling(remainingTypingMs / 1000.0);

                                 if (estSec > 0)
                                 {
                                     var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                                     try
                                     {
                                         await redis.StringSetAsync($"ai_typing:{conversation.Id}", "typing", TimeSpan.FromSeconds(estSec));
                                     }
                                     catch (Exception redisEx)
                                     {
                                         Console.WriteLine($"[ReplySender] Redis set loop failed: {redisEx.Message}");
                                     }
                                 }

                                 await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                                 {
                                     conversationId = conversation.Id,
                                     isTyping = true,
                                     estimatedSeconds = estSec,
                                     stage = "typing"
                                 });
                            }
                        }
                    }

                    await Task.Delay(delayMs);

                    string recipient;
                    using (var targetScope = _serviceProvider.CreateScope())
                    {
                        var tenantContext = targetScope.ServiceProvider.GetRequiredService<ITenantContext>();
                        tenantContext.SetProjectId(@event.ProjectId);
                        var targetDbContext = targetScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var currentTarget = await ResolveCurrentDeliveryTargetAsync(
                            targetScope.ServiceProvider,
                            targetDbContext,
                            @event);
                        if (currentTarget is null)
                        {
                            Console.WriteLine($"[ReplySender] Originating WhatsApp conversation for {@event.Id} no longer has a live account-scoped target. Delivery stopped.");
                            return;
                        }
                        if (currentTarget.Value.Customer.IsBlacklisted
                            || await HasPaidBookingAsync(targetDbContext, currentTarget.Value.Customer.Id))
                        {
                            Console.WriteLine($"[ReplySender] Delivery target {currentTarget.Value.Customer.Id} is no longer eligible. Delivery stopped.");
                            return;
                        }

                        @event.ConversationId = currentTarget.Value.Conversation.Id;
                        recipient = currentTarget.Value.Customer.PhoneNumber;
                        if (string.IsNullOrWhiteSpace(recipient))
                        {
                            Console.WriteLine($"[ReplySender] Delivery target {currentTarget.Value.Customer.Id} has no WhatsApp recipient. Delivery stopped.");
                            return;
                        }
                    }

                    var payload = new
                    {
                        projectId = @event.ProjectId,
                        whatsappAccountId = @event.WhatsAppAccountId,
                        to = recipient,
                        message = chunk,
                        idempotencyKey = $"{deliveryKey}:{i}",
                        expectedConnectedAt = @event.RequiredWhatsAppConnectedAt
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);

                    try
                    {
                        var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(_httpClient, $"{_gatewayUrl}/api/whatsapp/send", jsonPayload);
                        var responseBody = await response.Content.ReadAsStringAsync();
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[ReplySender] Successfully sent AI reply chunk to {@event.Sender} via Gateway.");
                            var externalMessageId = ProviderMessageId(responseBody);
                            if (string.IsNullOrWhiteSpace(externalMessageId))
                            {
                                Console.WriteLine("[ReplySender] Gateway reported success without a provider message id; delivery is ambiguous.");
                                await MarkDeliveryUnknownAsync(@event, deliveryKey);
                                break;
                            }

                            // Save message to database and broadcast via SignalR
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                                tenantContext.SetProjectId(@event.ProjectId);

                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                                var currentTarget = await ResolveCurrentDeliveryTargetAsync(
                                    scope.ServiceProvider,
                                    dbContext,
                                    @event);
                                if (currentTarget is not null)
                                {
                                    var conversation = currentTarget.Value.Conversation;
                                    @event.ConversationId = conversation.Id;
                                    if (conversation != null)
                                    {
                                        if (string.Equals(
                                                conversation.WhatsAppDeliveryUnknownKey,
                                                deliveryKey,
                                                StringComparison.Ordinal))
                                        {
                                            conversation.WhatsAppDeliveryUnknownAt = null;
                                            conversation.WhatsAppDeliveryUnknownKey = null;
                                        }
                                        var alreadyRecorded = await dbContext.Messages
                                            .IgnoreQueryFilters()
                                            .AnyAsync(message => message.ConversationId == conversation.Id
                                                && message.ExternalMessageId == externalMessageId);
                                        if (alreadyRecorded)
                                        {
                                            if (dbContext.ChangeTracker.HasChanges())
                                            {
                                                await dbContext.SaveChangesAsync();
                                            }
                                            Console.WriteLine($"[ReplySender] Skipping already recorded AI reply chunk {externalMessageId}.");
                                            continue;
                                        }

                                        var message = new Message
                                        {
                                            Id = DeterministicMessageId(
                                                @event.ProjectId,
                                                @event.WhatsAppAccountId ?? @event.ProjectId,
                                                externalMessageId),
                                            ConversationId = conversation.Id,
                                            ExternalMessageId = externalMessageId,
                                            Direction = "Outgoing",
                                            Content = chunk,
                                            MessageType = "Text",
                                            Timestamp = DateTime.UtcNow
                                        };

                                        dbContext.Messages.Add(message);
                                        
                                        conversation.LastMessageTimestamp = DateTime.UtcNow;
                                        dbContext.Entry(conversation).State = EntityState.Modified;

                                        try
                                        {
                                            await dbContext.SaveChangesAsync();
                                        }
                                        catch (DbUpdateException)
                                        {
                                            dbContext.Entry(message).State = EntityState.Detached;
                                            var duplicatePersisted = await dbContext.Messages
                                                .IgnoreQueryFilters()
                                                .AnyAsync(existing => existing.Id == message.Id);
                                            if (!duplicatePersisted) throw;
                                            Console.WriteLine($"[ReplySender] Another consumer already recorded AI reply chunk {externalMessageId}.");
                                            continue;
                                        }

                                        // Broadcast message via SignalR
                                        var signalrPayload = new
                                        {
                                            id = message.Id,
                                            conversationId = message.ConversationId,
                                            senderType = "AI",
                                            content = message.Content,
                                            createdAt = message.Timestamp.ToString("o"),
                                            status = "Sent",
                                            mediaUrl = (string)null,
                                            mediaType = (string)null
                                        };

                                        try
                                        {
                                            await hubContext.Clients.Group($"project_{@event.ProjectId}")
                                                .SendAsync("ReceiveMessage", signalrPayload);
                                        }
                                        catch (Exception notificationError)
                                        {
                                            Console.WriteLine(
                                                $"[ReplySender] Chunk {externalMessageId} was persisted, but SignalR notification failed: {notificationError.Message}");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[ReplySender] Gateway returned error code {response.StatusCode}: {responseBody}");
                            if ((int)response.StatusCode == 412
                                || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                            {
                                await ScheduleRemainingChunksAsync(
                                    @event,
                                    chunks,
                                    i,
                                    deliveryKey);
                                Console.WriteLine("[ReplySender] Deferred the unsent reply remainder to its next daily delivery slot.");
                            }
                            else if ((int)response.StatusCode == 409
                                || ((int)response.StatusCode >= 500
                                    && (int)response.StatusCode != 503))
                            {
                                await MarkDeliveryUnknownAsync(@event, deliveryKey);
                            }

                            // A reply is one ordered operation. Continuing after any failed
                            // chunk could deliver later chunks out of order or mix retries.
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ReplySender] Exception while calling WhatsApp Gateway: {ex.Message}");
                        await MarkDeliveryUnknownAsync(@event, deliveryKey);
                        break;
                    }

                    // Stagger delay between consecutive message chunks to feel human-like
                    if (i < chunks.Count - 1)
                    {
                        bool isTest = false;
                        try
                        {
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var project = dbContext.Projects.Find(@event.ProjectId);
                                if (project != null && HumanMessagingEngine.IsTestProject(project.Name))
                                {
                                    isTest = true;
                                }
                            }
                        }
                        catch
                        {
                            // Fallback
                        }

                        int staggerDelayMs = queuedTooLong ? 0 : isTest ? 100 : new Random().Next(2, 5) * 1000;
                        if (staggerDelayMs > 0)
                        {
                            Console.WriteLine($"[ReplySender] Waiting {staggerDelayMs}ms stagger delay between message chunks...");
                            await Task.Delay(staggerDelayMs);
                        }
                    }
                }
            }
            finally
            {
                // Always clear typing indicator after sending is completed/stopped
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    tenantContext.SetProjectId(@event.ProjectId);

                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                    var customer = await dbContext.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.PhoneNumber == @event.Sender);

                     if (customer != null)
                     {
                         var conversation = await FindConversationAsync(dbContext, @event, customer.Id);

                         if (conversation != null)
                         {
                             var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                             try
                             {
                                 await redis.KeyDeleteAsync($"ai_typing:{conversation.Id}");
                             }
                             catch (Exception redisEx)
                             {
                                 Console.WriteLine($"[ReplySender] Redis delete finally failed: {redisEx.Message}");
                             }

                             await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                             {
                                 conversationId = conversation.Id,
                                 isTyping = false,
                                 estimatedSeconds = 0,
                                 stage = ""
                             });
                         }
                     }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ReplySender] Failed to clear typing status: {ex.Message}");
                }
            }
        }

        private static Task<bool> HasPaidBookingAsync(AppDbContext dbContext, Guid customerId) =>
            dbContext.GroupAppointmentBookings
                .IgnoreQueryFilters()
                .AnyAsync(booking => booking.CustomerId == customerId && booking.IsPaid);

        private async Task MarkDeliveryUnknownAsync(
            AIReplyGeneratedEvent @event,
            string deliveryKey)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenantContext.SetProjectId(@event.ProjectId);
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var currentTarget = await ResolveCurrentDeliveryTargetAsync(
                    scope.ServiceProvider,
                    dbContext,
                    @event);
                var conversation = currentTarget?.Conversation;
                if (conversation is null) return;
                conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow;
                conversation.WhatsAppDeliveryUnknownKey = deliveryKey;
                await dbContext.SaveChangesAsync();
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[ReplySender] Failed to persist DeliveryUnknown fence for {@event.Id}: {exception.Message}");
                throw;
            }
        }

        private async Task ScheduleRemainingChunksAsync(
            AIReplyGeneratedEvent @event,
            IReadOnlyList<string> chunks,
            int firstUnsentChunk,
            string deliveryKey)
        {
            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var currentTarget = await ResolveCurrentDeliveryTargetAsync(
                scope.ServiceProvider,
                dbContext,
                @event);
            if (currentTarget is null)
            {
                throw new InvalidOperationException(
                    $"Cannot defer WhatsApp reply {@event.Id}: the target customer no longer exists.");
            }
            var customer = currentTarget.Value.Customer;
            var conversation = currentTarget.Value.Conversation;
            @event.ConversationId = conversation.Id;

            var projectSettings = await dbContext.ProjectSettings.IgnoreQueryFilters()
                .FirstOrDefaultAsync(settings => settings.ProjectId == @event.ProjectId);
            var timezone = ResolveTimeZone(projectSettings?.Timezone);
            var nowUtc = DateTime.UtcNow;
            var firstDueUtc = WhatsAppDailyDeliverySchedule.NextOccurrenceAfter(
                nowUtc,
                nowUtc,
                timezone);
            var added = new List<FollowUp>();
            Guid? predecessorId = null;

            for (var index = firstUnsentChunk; index < chunks.Count; index++)
            {
                var followUpId = DeterministicDeferredChunkId(
                    @event.ProjectId,
                    @event.WhatsAppAccountId ?? @event.ProjectId,
                    conversation.Id,
                    deliveryKey,
                    index);
                if (await dbContext.FollowUps.IgnoreQueryFilters()
                    .AnyAsync(candidate => candidate.Id == followUpId))
                {
                    continue;
                }

                var followUp = new FollowUp
                {
                    Id = followUpId,
                    ProjectId = @event.ProjectId,
                    CustomerId = customer.Id,
                    ConversationId = conversation.Id,
                    DependsOnFollowUpId = predecessorId,
                    WhatsAppAccountId = @event.WhatsAppAccountId ?? @event.ProjectId,
                    Channel = "WhatsApp",
                    DueDate = firstDueUtc.AddSeconds(index - firstUnsentChunk),
                    Status = "Pending",
                    Notes = chunks[index],
                    Type = "DeferredReplyChunk",
                    Tone = "Exact"
                };
                dbContext.FollowUps.Add(followUp);
                added.Add(followUp);
                predecessorId = followUp.Id;
            }

            if (added.Count == 0) return;
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                foreach (var followUp in added)
                {
                    dbContext.Entry(followUp).State = EntityState.Detached;
                }
                var ids = added.Select(followUp => followUp.Id).ToArray();
                var persistedCount = await dbContext.FollowUps.IgnoreQueryFilters()
                    .CountAsync(candidate => ids.Contains(candidate.Id));
                if (persistedCount != ids.Length) throw;
            }
        }

        private static TimeZoneInfo ResolveTimeZone(string? timezoneId)
        {
            if (string.IsNullOrWhiteSpace(timezoneId)) return TimeZoneInfo.Utc;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private static Guid DeterministicDeferredChunkId(
            Guid projectId,
            Guid whatsAppAccountId,
            Guid conversationId,
            string deliveryKey,
            int chunkIndex)
        {
            var value = $"whatsapp-deferred-chunk:{projectId:N}:{whatsAppAccountId:N}:{conversationId:N}:{deliveryKey}:{chunkIndex}";
            var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return new Guid(bytes.AsSpan(0, 16));
        }

        private static Task<Conversation?> FindConversationAsync(
            AppDbContext dbContext,
            AIReplyGeneratedEvent @event,
            Guid customerId)
        {
            var conversations = dbContext.Conversations
                .IgnoreQueryFilters()
                .Where(conversation => conversation.ProjectId == @event.ProjectId
                    && conversation.CustomerId == customerId
                    && conversation.Channel == "WhatsApp"
                    && conversation.Status != "Closed");
            return @event.ConversationId.HasValue
                ? conversations.FirstOrDefaultAsync(conversation =>
                    conversation.Id == @event.ConversationId.Value
                    && conversation.WhatsAppAccountId == @event.WhatsAppAccountId)
                : conversations.FirstOrDefaultAsync(conversation =>
                    conversation.WhatsAppAccountId == @event.WhatsAppAccountId);
        }

        private static async Task<(Customer Customer, Conversation Conversation)?> ResolveCurrentDeliveryTargetAsync(
            IServiceProvider scopedServices,
            AppDbContext dbContext,
            AIReplyGeneratedEvent @event)
        {
            var effectiveAccountId = @event.WhatsAppAccountId ?? @event.ProjectId;
            Customer? customer = null;

            if (@event.ConversationId.HasValue)
            {
                var referencedConversation = await dbContext.Conversations.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(candidate => candidate.ProjectId == @event.ProjectId
                        && candidate.Id == @event.ConversationId.Value
                        && candidate.Channel == "WhatsApp"
                        && candidate.WhatsAppDestinationId == null
                        && (candidate.WhatsAppAccountId ?? candidate.ProjectId) == effectiveAccountId);
                if (referencedConversation is not null)
                {
                    customer = await dbContext.Customers.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(candidate => candidate.ProjectId == @event.ProjectId
                            && candidate.Id == referencedConversation.CustomerId);
                    if (customer is not null && referencedConversation.Status != "Closed")
                        return (customer, referencedConversation);
                }
            }

            var sender = (@event.Sender ?? string.Empty).Trim();
            var accountIdentity = await dbContext.WhatsAppCustomerIdentities.IgnoreQueryFilters()
                .FirstOrDefaultAsync(identity => identity.ProjectId == @event.ProjectId
                    && identity.WhatsAppAccountId == effectiveAccountId
                    && identity.ExternalId == sender);
            if (accountIdentity is not null)
            {
                customer = await dbContext.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(candidate => candidate.ProjectId == @event.ProjectId
                        && candidate.Id == accountIdentity.CustomerId);
            }

            if (customer is null && !sender.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedSender = sender.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase)
                    ? sender[..sender.IndexOf('@')]
                    : sender;
                if (!string.IsNullOrWhiteSpace(normalizedSender))
                {
                    customer = await scopedServices
                        .GetRequiredService<WhatsAppCustomerMergeService>()
                        .ResolveByPhoneAsync(@event.ProjectId, normalizedSender);
                }
            }

            customer ??= await dbContext.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.ProjectId == @event.ProjectId
                    && candidate.PhoneNumber == sender);
            if (customer is null) return null;

            var conversation = await dbContext.Conversations.IgnoreQueryFilters()
                .Where(candidate => candidate.ProjectId == @event.ProjectId
                    && candidate.CustomerId == customer.Id
                    && candidate.Channel == "WhatsApp"
                    && candidate.WhatsAppDestinationId == null
                    && candidate.Status != "Closed"
                    && (candidate.WhatsAppAccountId ?? candidate.ProjectId) == effectiveAccountId)
                .OrderByDescending(candidate => candidate.LastMessageTimestamp)
                .FirstOrDefaultAsync();
            return conversation is null ? null : (customer, conversation);
        }

        private static string? ProviderMessageId(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return document.RootElement.TryGetProperty("messageId", out var messageId)
                    ? messageId.GetString()
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
            string providerMessageId)
        {
            var value = $"whatsapp-outgoing:{projectId:N}:{whatsAppAccountId:N}:{providerMessageId}";
            var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return new Guid(bytes.AsSpan(0, 16));
        }
    }
}
