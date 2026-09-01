using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using Modules.Conversations.Domain;
using Modules.Facebook.Domain;
using Modules.CRM.Domain;
using Modules.WhatsApp.Services;
using Shared.Security;

namespace Modules.CRM.Services
{
    public class FollowUpScheduler : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public FollowUpScheduler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Register Hangfire recurring jobs on startup
            RecurringJob.AddOrUpdate<FollowUpScheduler>(
                "check-overdue-followups",
                s => s.CheckOverdueFollowUpsJobAsync(),
                Cron.Minutely); // Check every minute for overdue follow-ups
            
            RecurringJob.AddOrUpdate<FollowUpScheduler>(
                "recalculate-lead-scores",
                s => s.RecalculateLeadScoresJobAsync(),
                Cron.Minutely);

            var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone("Africa/Cairo");
            RecurringJob.AddOrUpdate<FollowUpScheduler>(
                "whatsapp-group-automation-lifecycle",
                s => s.RunWhatsAppGroupAutomationLifecycleJobAsync(null),
                "0 23 * * *", // 11:00 PM every day Cairo time
                new RecurringJobOptions { TimeZone = cairoZone });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public async Task CheckOverdueFollowUpsJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();
            var projectSecretVault = scope.ServiceProvider.GetRequiredService<IProjectSecretVault>();
            var gatewaySessionClient = scope.ServiceProvider.GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();
            var whatsAppAccounts = scope.ServiceProvider.GetRequiredService<WhatsAppAccountService>();
            var whatsAppConversations = scope.ServiceProvider.GetRequiredService<WhatsAppConversationService>();

            var now = DateTime.UtcNow;
            var leaseExpiredBefore = now.AddMinutes(-10);
            if (dbContext.Database.IsRelational())
            {
                await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.Status == "Processing"
                        && f.UpdatedAt < leaseExpiredBefore
                        && (f.Channel != "WhatsApp" || !f.ConversationId.HasValue))
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(f => f.Status, "DeliveryUnknown")
                        .SetProperty(f => f.UpdatedAt, now));
            }
            else
            {
                var unknownDeliveries = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.Status == "Processing"
                        && f.UpdatedAt < leaseExpiredBefore
                        && (f.Channel != "WhatsApp" || !f.ConversationId.HasValue))
                    .ToListAsync();
                foreach (var followUp in unknownDeliveries) followUp.Status = "DeliveryUnknown";
                await dbContext.SaveChangesAsync();
            }
            var overdueIds = await dbContext.FollowUps
                .IgnoreQueryFilters()
                .Where(f => (f.Status == "Pending" && f.DueDate < now)
                    || (f.Status == "Processing"
                        && f.UpdatedAt < leaseExpiredBefore
                        && f.Channel == "WhatsApp"
                        && f.ConversationId.HasValue))
                .OrderBy(f => f.DueDate)
                .Select(f => f.Id)
                .ToListAsync();
            var overdueFollowUps = new List<FollowUp>();
            if (dbContext.Database.IsRelational())
            {
                foreach (var followUpId in overdueIds)
                {
                    var claimed = await dbContext.FollowUps
                        .IgnoreQueryFilters()
                        .Where(f => f.Id == followUpId && f.Status == "Pending" && f.DueDate < now)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(f => f.Status, "Processing")
                            .SetProperty(f => f.UpdatedAt, now));
                    if (claimed == 0)
                    {
                        claimed = await dbContext.FollowUps
                            .IgnoreQueryFilters()
                            .Where(f => f.Id == followUpId
                                && f.Status == "Processing"
                                && f.UpdatedAt < leaseExpiredBefore
                                && f.Channel == "WhatsApp"
                                && f.ConversationId.HasValue)
                            .ExecuteUpdateAsync(update => update.SetProperty(f => f.UpdatedAt, now));
                    }
                    if (claimed == 0) continue;
                    overdueFollowUps.Add(await dbContext.FollowUps
                        .IgnoreQueryFilters()
                        .SingleAsync(f => f.Id == followUpId));
                }
            }
            else
            {
                overdueFollowUps = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => overdueIds.Contains(f.Id)
                        && ((f.Status == "Pending" && f.DueDate < now)
                            || (f.Status == "Processing"
                                && f.UpdatedAt < leaseExpiredBefore
                                && f.Channel == "WhatsApp"
                                && f.ConversationId.HasValue)))
                    .OrderBy(f => f.DueDate)
                    .ToListAsync();
                foreach (var followUp in overdueFollowUps)
                {
                    followUp.Status = "Processing";
                    followUp.UpdatedAt = now;
                }
                await dbContext.SaveChangesAsync();
            }

            if (!overdueFollowUps.Any())
            {
                Console.WriteLine($"[Hangfire Job] No overdue follow-ups found. {await dbContext.FollowUps.IgnoreQueryFilters().CountAsync(f => f.Status == "Pending")} pending follow-ups scheduled for future.");
                return;
            }

            Console.WriteLine($"[Hangfire Job] Found {overdueFollowUps.Count} pending follow-ups to execute.");

            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            var httpClientFactory = scope.ServiceProvider.GetService<IHttpClientFactory>();
            using var httpClient = httpClientFactory?.CreateClient(nameof(FollowUpScheduler)) ?? new HttpClient();

            foreach (var followUp in overdueFollowUps)
            {
                string? activeDispatchChannel = null;
                TimeZoneInfo? activeProjectTimezone = null;
                Conversation? activeTargetConversation = null;
                var deliveryAttempted = false;
                try
                {
                    var customer = await dbContext.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == followUp.CustomerId);

                    if (customer == null)
                    {
                        Console.WriteLine($"[Hangfire Job] Customer not found for follow-up {followUp.Id}. Marking as Missed.");
                        followUp.Status = "Missed";
                        continue;
                    }

                    if (followUp.DependsOnFollowUpId.HasValue)
                    {
                        var predecessor = await dbContext.FollowUps
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(candidate =>
                                candidate.ProjectId == followUp.ProjectId
                                && candidate.Id == followUp.DependsOnFollowUpId.Value);
                        if (predecessor is null)
                        {
                            followUp.Status = "Cancelled";
                            continue;
                        }
                        if (predecessor.Status != "Completed")
                        {
                            if (predecessor.Status == "DeliveryUnknown")
                            {
                                followUp.Status = "DeliveryUnknown";
                            }
                            else if (predecessor.Status is "Cancelled" or "Missed" or "Bypassed")
                            {
                                followUp.Status = "Cancelled";
                            }
                            else
                            {
                                followUp.DueDate = followUp.DueDate > predecessor.DueDate
                                    ? followUp.DueDate
                                    : predecessor.DueDate.AddSeconds(1);
                                followUp.Status = "Pending";
                            }
                            followUp.UpdatedAt = DateTime.UtcNow;
                            await dbContext.SaveChangesAsync();
                            continue;
                        }
                    }

                    Conversation? targetConversation = null;
                    if (followUp.ConversationId.HasValue)
                    {
                        if (followUp.Channel is not ("WhatsApp" or "Messenger"))
                        {
                            Console.WriteLine($"[Hangfire Job] Follow-up {followUp.Id} has incomplete or unsupported target metadata. Marking as Missed.");
                            followUp.Status = "Missed";
                            continue;
                        }
                        targetConversation = await dbContext.Conversations
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(conversation =>
                                conversation.Id == followUp.ConversationId.Value
                                && conversation.ProjectId == followUp.ProjectId
                                && conversation.CustomerId == customer.Id
                                && conversation.Channel == followUp.Channel);
                        if (targetConversation is null)
                        {
                            Console.WriteLine($"[Hangfire Job] Target conversation not found for follow-up {followUp.Id}. Marking as Missed.");
                            followUp.Status = "Missed";
                            continue;
                        }
                        if (targetConversation.Channel == "WhatsApp"
                            && targetConversation.WhatsAppDestinationId.HasValue)
                        {
                            Console.WriteLine($"[Hangfire Job] Follow-up {followUp.Id} targets a Cloud API conversation, which the Baileys sender cannot dispatch. Cancelling.");
                            followUp.Status = "Cancelled";
                            continue;
                        }
                        if (targetConversation.Status == "Closed")
                        {
                            Console.WriteLine($"[Hangfire Job] Target conversation is closed for follow-up {followUp.Id}. Cancelling.");
                            followUp.Status = "Cancelled";
                            continue;
                        }
                        activeTargetConversation = targetConversation;
                    }
                    else if (!string.IsNullOrWhiteSpace(followUp.Channel)
                        && followUp.Channel is not ("WhatsApp" or "Messenger"))
                    {
                        Console.WriteLine($"[Hangfire Job] Follow-up {followUp.Id} has an unsupported channel. Marking as Missed.");
                        followUp.Status = "Missed";
                        continue;
                    }

                    var dispatchChannel = followUp.Channel
                        ?? (string.IsNullOrEmpty(customer.PhoneNumber) && !string.IsNullOrEmpty(customer.FacebookPSID)
                            ? "Messenger"
                            : "WhatsApp");
                    activeDispatchChannel = dispatchChannel;
                    var whatsAppAccountId = followUp.WhatsAppAccountId
                        ?? targetConversation?.WhatsAppAccountId
                        ?? (await whatsAppAccounts.GetDefaultAsync(followUp.ProjectId)).Id;
                    if (dispatchChannel == "WhatsApp")
                    {
                        followUp.WhatsAppAccountId = whatsAppAccountId;
                    }
                    if ((dispatchChannel == "WhatsApp" && string.IsNullOrEmpty(customer.PhoneNumber))
                        || (dispatchChannel == "Messenger" && string.IsNullOrEmpty(customer.FacebookPSID)))
                    {
                        Console.WriteLine($"[Hangfire Job] Customer {customer.Id} has no contact for {dispatchChannel}. Marking follow-up {followUp.Id} as Missed.");
                        followUp.Status = "Missed";
                        continue;
                    }

                    bool isMessenger = dispatchChannel == "Messenger";
                    var projectSettings = await dbContext.ProjectSettings
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
                    var projectTimezone = TimezoneHelper.GetTimeZone(projectSettings?.Timezone);
                    activeProjectTimezone = projectTimezone;

                    if (!isMessenger)
                    {
                        var session = await gatewaySessionClient.GetAsync(followUp.ProjectId, whatsAppAccountId);
                        if (!CanDispatchInCurrentConnection(followUp, session))
                        {
                            var deferred = TryDeferToNextDailySlot(followUp, DateTime.UtcNow, projectTimezone);
                            await dbContext.SaveChangesAsync();
                            Console.WriteLine(deferred
                                ? $"[Hangfire Job] Deferred WhatsApp follow-up {followUp.Id} to {followUp.DueDate:O} because it was due before the current connection was available."
                                : $"[Hangfire Job] Expired appointment reminder {followUp.Id} instead of sending it after the appointment.");
                            continue;
                        }
                    }

                    string? talkTipsTrialInstructions = null;
                    if (!isMessenger && projectSettings?.IsTalkTipsTrialGateEnabled == true)
                    {
                        var trialStatusClient = scope.ServiceProvider.GetRequiredService<Modules.TalkTips.Services.TalkTipsTrialStatusClient>();
                        if (!await trialStatusClient.HasTriedAsync(customer.PhoneNumber))
                        {
                            talkTipsTrialInstructions = Modules.TalkTips.Services.TalkTipsTrialCtaInstructions.ForCustomerWhoHasNotTried();
                        }
                    }

                    if (customer.IsBlacklisted)
                    {
                        Console.WriteLine($"[Hangfire Job] Customer {customer.PhoneNumber ?? customer.FacebookPSID} is blacklisted. Cancelling follow-up {followUp.Id}.");
                        followUp.Status = "Cancelled";
                        dbContext.Entry(followUp).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    // Check if customer has any paid group booking
                    var hasPaid = await dbContext.GroupAppointmentBookings
                        .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid && b.ProjectId == followUp.ProjectId);

                    if (hasPaid)
                    {
                        Console.WriteLine($"[Hangfire Job] Customer {customer.PhoneNumber ?? customer.FacebookPSID} has already paid. Cancelling follow-up {followUp.Id}.");
                        followUp.Status = "Cancelled";
                        dbContext.Entry(followUp).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    // Check if WhatsApp auto-reminder rule is disabled in automation rules
                    bool whatsappReminderEnabled = true;
                    if (!string.IsNullOrEmpty(customer.AutomationRules))
                    {
                        try
                        {
                            using var rulesDoc = JsonDocument.Parse(customer.AutomationRules);
                            if (rulesDoc.RootElement.TryGetProperty("whatsappReminder24h", out var prop))
                            {
                                whatsappReminderEnabled = prop.GetBoolean();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Hangfire Job] Error parsing automation rules for customer {customer.Id}: {ex.Message}");
                        }
                    }

                    if (!isMessenger && !whatsappReminderEnabled)
                    {
                        Console.WriteLine($"[Hangfire Job] WhatsApp reminder is disabled in automation rules for customer {customer.PhoneNumber}. Bypassing follow-up {followUp.Id}.");
                        followUp.Status = "Bypassed";
                        dbContext.Entry(followUp).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    string messageContent = string.Empty;
                    if (string.Equals(followUp.Tone, "Exact", StringComparison.Ordinal))
                    {
                        messageContent = followUp.Notes;
                        talkTipsTrialInstructions = null;
                    }
                    else if (!string.IsNullOrEmpty(followUp.Notes))
                    {
                        var notesTrimmed = followUp.Notes.Trim();
                        bool looksLikeDirectMessage = notesTrimmed.StartsWith("مرحباً", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("أهلاً", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("يا فندم", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("صباح الخير", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("مساء الخير", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("السلام عليكم", StringComparison.OrdinalIgnoreCase);

                        if (looksLikeDirectMessage && string.IsNullOrWhiteSpace(talkTipsTrialInstructions))
                        {
                            messageContent = followUp.Notes;
                        }
                        else
                        {
                            try
                            {
                                var aiMarketingBrain = scope.ServiceProvider.GetService(typeof(Modules.AI.Services.IAIMarketingBrain)) as Modules.AI.Services.IAIMarketingBrain;
                                if (aiMarketingBrain != null)
                                {
                                    string? apiKey = projectSecretVault.Unprotect(
                                        followUp.ProjectId,
                                        projectSettings?.GeminiApiKey);
                                    string model = projectSettings?.ResolveGeminiModel(DateTime.UtcNow);

                                    var hasAttended = await dbContext.GroupAppointmentBookings
                                        .AnyAsync(b => b.CustomerId == customer.Id && b.IsAttended);

                                    var followUpNotesForAi = string.IsNullOrWhiteSpace(talkTipsTrialInstructions)
                                        ? followUp.Notes
                                        : $"{followUp.Notes}\n\n{talkTipsTrialInstructions}";
                                    messageContent = await aiMarketingBrain.RewriteFollowUpNotesAsync(
                                        customer.Name,
                                        followUpNotesForAi,
                                        hasAttended,
                                        followUp.Tone,
                                        apiKey,
                                        model);
                                }
                                else
                                {
                                    messageContent = followUp.Notes;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Hangfire Job] Failed to rewrite follow-up notes via Gemini for follow-up {followUp.Id}: {ex.Message}");
                                messageContent = followUp.Notes;
                            }
                        }
                    }
                    else
                    {
                        messageContent = followUp.Type == "AppointmentReminder"
                            ? "مرحباً، نود تذكيرك بموعد الكورس غداً. ننتظر حضورك!"
                            : "مرحباً، أردنا فقط المتابعة معك لمعرفة ما إذا كان لديك أي استفسار آخر.";
                    }

                    if (!string.IsNullOrWhiteSpace(talkTipsTrialInstructions))
                    {
                        messageContent = Modules.TalkTips.Services.TalkTipsTrialCtaInstructions.EnsureCta(messageContent);
                    }

                    messageContent = Modules.WhatsApp.Services.OutgoingMessageText.Normalize(messageContent);

                    if (isMessenger)
                    {
                        var connectedPage = await dbContext.ConnectedPages
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(cp => cp.ProjectId == followUp.ProjectId && cp.IsActive);

                        if (connectedPage == null)
                        {
                            Console.WriteLine($"[Hangfire Job] Active ConnectedPage not found for project {followUp.ProjectId} and customer {customer.Id}. Marking follow-up {followUp.Id} as Missed.");
                            followUp.Status = "Missed";
                            continue;
                        }

                        var facebookGraphService = scope.ServiceProvider.GetRequiredService<Modules.Facebook.Services.IFacebookGraphService>();
                        bool fbSent = false;
                        try
                        {
                            await facebookGraphService.SendMessageAsync(
                                connectedPage.FacebookPageId,
                                connectedPage.PageAccessToken,
                                customer.FacebookPSID,
                                messageContent
                            );
                            fbSent = true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Hangfire Job] Failed to send Messenger follow-up to PSID {customer.FacebookPSID}: {ex.Message}");
                        }

                        if (fbSent)
                        {
                            Console.WriteLine($"[Hangfire Job] Successfully sent follow-up message to Messenger PSID {customer.FacebookPSID}");

                            var conversation = targetConversation ?? await dbContext.Conversations
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(c => c.ProjectId == followUp.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger" && c.Status != "Closed");

                            if (conversation == null)
                            {
                                conversation = new Conversation
                                {
                                    ProjectId = followUp.ProjectId,
                                    CustomerId = customer.Id,
                                    Status = "Open",
                                    Channel = "Messenger",
                                    LastMessageTimestamp = DateTime.UtcNow
                                };
                                dbContext.Conversations.Add(conversation);
                                await dbContext.SaveChangesAsync();
                            }
                            else
                            {
                                conversation.LastMessageTimestamp = DateTime.UtcNow;
                                dbContext.Entry(conversation).State = EntityState.Modified;
                            }

                            var message = new Message
                            {
                                ConversationId = conversation.Id,
                                ExternalMessageId = $"msg_fb_fu_{Guid.NewGuid():N}",
                                Direction = "Outgoing",
                                Content = messageContent,
                                MessageType = "Text",
                                Timestamp = DateTime.UtcNow
                            };
                            dbContext.Messages.Add(message);

                            followUp.Status = "Completed";
                            await dbContext.SaveChangesAsync();

                            var signalrPayload = new
                            {
                                id = message.Id,
                                conversationId = message.ConversationId,
                                senderType = "Agent",
                                content = message.Content,
                                createdAt = message.Timestamp.ToString("o"),
                                status = "Sent",
                                mediaUrl = (string)null,
                                mediaType = (string)null,
                                channel = "Messenger"
                            };

                            await hubContext.Clients.Group($"project_{followUp.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                        }
                        else
                        {
                            Console.WriteLine($"[Hangfire Job] Facebook API error/failure for Messenger follow-up {followUp.Id}. Marking as Missed.");
                            followUp.Status = "Missed";
                        }
                    }
                    else
                    {
                        var liveSession = await gatewaySessionClient.GetAsync(followUp.ProjectId, whatsAppAccountId);
                        if (!CanDispatchInCurrentConnection(followUp, liveSession))
                        {
                            var deferred = TryDeferToNextDailySlot(followUp, DateTime.UtcNow, projectTimezone);
                            await dbContext.SaveChangesAsync();
                            Console.WriteLine(deferred
                                ? $"[Hangfire Job] Deferred WhatsApp follow-up {followUp.Id} to {followUp.DueDate:O} because the connection changed before delivery."
                                : $"[Hangfire Job] Expired appointment reminder {followUp.Id} instead of sending it after the appointment.");
                            continue;
                        }

                        var payload = new
                        {
                            projectId = followUp.ProjectId,
                            whatsappAccountId = whatsAppAccountId,
                            to = customer.PhoneNumber,
                            message = messageContent,
                            idempotencyKey = followUp.Id.ToString("N"),
                            expectedConnectedAt = liveSession.ConnectedAt
                        };

                        var jsonPayload = JsonSerializer.Serialize(payload);
                        deliveryAttempted = true;
                        var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(httpClient, $"{gatewayUrl}/api/whatsapp/send", jsonPayload);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[Hangfire Job] Successfully sent follow-up message to {customer.PhoneNumber}");
                            var providerMessageId = ProviderMessageId(responseBody);
                            if (string.IsNullOrWhiteSpace(providerMessageId))
                            {
                                followUp.Status = "DeliveryUnknown";
                                MarkConversationDeliveryUnknown(
                                    targetConversation,
                                    followUp.Id.ToString("N"));
                                await dbContext.SaveChangesAsync();
                                continue;
                            }

                            var sentAt = DateTime.UtcNow;
                            var conversation = targetConversation
                                ?? await whatsAppConversations.ResolveOrCreateAsync(
                                    followUp.ProjectId,
                                    customer.Id,
                                    whatsAppAccountId,
                                    sentAt);
                            if (sentAt > conversation.LastMessageTimestamp)
                                conversation.LastMessageTimestamp = sentAt;
                            if (string.Equals(
                                    conversation.WhatsAppDeliveryUnknownKey,
                                    followUp.Id.ToString("N"),
                                    StringComparison.Ordinal))
                            {
                                conversation.WhatsAppDeliveryUnknownAt = null;
                                conversation.WhatsAppDeliveryUnknownKey = null;
                            }

                            var messageId = WhatsAppMessageIdentity.Outgoing(
                                followUp.ProjectId,
                                whatsAppAccountId,
                                providerMessageId);
                            var message = await dbContext.Messages.IgnoreQueryFilters()
                                .FirstOrDefaultAsync(existing => existing.Id == messageId);
                            var createdMessage = message is null;
                            if (message is null)
                            {
                                message = new Message
                                {
                                    Id = messageId,
                                    ConversationId = conversation.Id,
                                    ExternalMessageId = providerMessageId,
                                    Direction = "Outgoing",
                                    Content = messageContent,
                                    MessageType = "Text",
                                    Timestamp = sentAt
                                };
                                dbContext.Messages.Add(message);
                            }

                            followUp.Status = "Completed";
                            await dbContext.SaveChangesAsync();

                            var signalrPayload = new
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

                            try
                            {
                                if (createdMessage)
                                    await hubContext.Clients.Group($"project_{followUp.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                            }
                            catch (Exception notificationError)
                            {
                                Console.WriteLine($"[Hangfire Job] Follow-up {followUp.Id} was sent, but SignalR notification failed: {notificationError.Message}");
                            }
                        }
                        else
                        {
                            if ((int)response.StatusCode == 412)
                            {
                                var deferred = TryDeferToNextDailySlot(followUp, DateTime.UtcNow, projectTimezone);
                                await dbContext.SaveChangesAsync();
                                Console.WriteLine(deferred
                                    ? $"[Hangfire Job] Deferred WhatsApp follow-up {followUp.Id} to {followUp.DueDate:O} because the connection changed at the delivery boundary."
                                    : $"[Hangfire Job] Expired appointment reminder {followUp.Id} instead of sending it after the appointment.");
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                            {
                                var deferred = TryDeferToNextDailySlot(followUp, DateTime.UtcNow, projectTimezone);
                                await dbContext.SaveChangesAsync();
                                Console.WriteLine(deferred
                                    ? $"[Hangfire Job] Deferred WhatsApp follow-up {followUp.Id} to {followUp.DueDate:O} because the gateway could not safely accept the delivery."
                                    : $"[Hangfire Job] Expired appointment reminder {followUp.Id} instead of sending it after the appointment.");
                            }
                            else
                            {
                                var deliveryUnknown = (int)response.StatusCode == 409
                                    || (int)response.StatusCode >= 500;
                                var nextStatus = deliveryUnknown ? "DeliveryUnknown" : "Missed";
                                Console.WriteLine($"[Hangfire Job] Gateway error {response.StatusCode} for follow-up {followUp.Id}: {responseBody}. Marking as {nextStatus}.");
                                followUp.Status = nextStatus;
                                if (deliveryUnknown)
                                    MarkConversationDeliveryUnknown(
                                        targetConversation,
                                        followUp.Id.ToString("N"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Hangfire Job] Exception while executing follow-up {followUp.Id}: {ex.Message}.");
                    if (followUp.Status != "Processing") continue;

                    if (activeDispatchChannel == "WhatsApp" && !deliveryAttempted)
                    {
                        TryDeferToNextDailySlot(
                            followUp,
                            DateTime.UtcNow,
                            activeProjectTimezone ?? TimeZoneInfo.Utc);
                    }
                    else if (activeDispatchChannel == "WhatsApp")
                    {
                        followUp.Status = "DeliveryUnknown";
                        MarkConversationDeliveryUnknown(
                            activeTargetConversation,
                            followUp.Id.ToString("N"));
                    }
                    else
                    {
                        followUp.Status = "Missed";
                    }
                }
            }

            await dbContext.SaveChangesAsync();
        }

        public async Task RecalculateLeadScoresJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var missedFollowUpsByCustomer = await dbContext.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.Status == "Missed")
                .GroupBy(f => f.CustomerId)
                .Select(g => new { CustomerId = g.Key, MissedCount = g.Count() })
                .ToListAsync();

            if (!missedFollowUpsByCustomer.Any())
            {
                Console.WriteLine("[Hangfire Job] No missed follow-ups found for lead score recalculation.");
                return;
            }

            var missedCounts = missedFollowUpsByCustomer.ToDictionary(f => f.CustomerId, f => f.MissedCount);
            var customerIds = missedCounts.Keys.ToList();
            var customers = await dbContext.Customers
                .IgnoreQueryFilters()
                .Where(c => customerIds.Contains(c.Id))
                .ToListAsync();

            foreach (var customer in customers)
            {
                var missedCount = missedCounts[customer.Id];
                var recalculatedScore = Math.Max(0, customer.LeadScore - (missedCount * 2));
                if (customer.LeadScore != recalculatedScore)
                {
                    customer.LeadScore = recalculatedScore;
                }
            }

            await dbContext.SaveChangesAsync();
            Console.WriteLine($"[Hangfire Job] Recalculated lead scores for {customers.Count} customers.");
        }

        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public async Task RunWhatsAppGroupAutomationLifecycleJobAsync(Guid? projectId = null)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var whatsAppAccounts = scope.ServiceProvider.GetRequiredService<WhatsAppAccountService>();
            var gatewaySessionClient = scope.ServiceProvider
                .GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();

            var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone("Africa/Cairo");
            var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoZone);
            var startOfWindowCairo = cairoNow.Date;
            var endOfWindowCairo = cairoNow.Date.AddDays(2);

            var startOfWindowUtc = TimeZoneInfo.ConvertTimeToUtc(startOfWindowCairo, cairoZone);
            var endOfWindowUtc = TimeZoneInfo.ConvertTimeToUtc(endOfWindowCairo, cairoZone);

            Console.WriteLine($"[Hangfire Group Lifecycle] Checking for active waves/appointments from today through tomorrow in Cairo timezone (UTC range: {startOfWindowUtc:O} to {endOfWindowUtc:O})");

            var appointments = await dbContext.GroupAppointments
                .IgnoreQueryFilters()
                .Where(a => (!projectId.HasValue || a.ProjectId == projectId.Value)
                    && a.IsActive
                    && a.DateTime >= startOfWindowUtc
                    && a.DateTime < endOfWindowUtc)
                .ToListAsync();

            if (!appointments.Any())
            {
                Console.WriteLine("[Hangfire Group Lifecycle] No active waves scheduled from today through tomorrow.");
                return;
            }

            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            using var httpClient = new HttpClient();

            foreach (var appointment in appointments)
            {
                try
                {
                    // Query project settings to check if Group Automation is enabled
                    var settings = await dbContext.ProjectSettings
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(s => s.ProjectId == appointment.ProjectId);

                    if (settings == null || !settings.IsWhatsAppGroupAutomationEnabled)
                    {
                        Console.WriteLine($"[Hangfire Group Lifecycle] WhatsApp Group Automation is disabled for project {appointment.ProjectId} or settings missing. Skipping appointment {appointment.Id}.");
                        continue;
                    }

                    var groupSubject = BuildWhatsAppGroupSubject(appointment.Name, appointment.Mode, appointment.DateTime, cairoZone);
                    var whatsAppAccountId = appointment.WhatsAppAccountId;
                    if (!whatsAppAccountId.HasValue)
                    {
                        whatsAppAccountId = (await whatsAppAccounts.GetDefaultAsync(appointment.ProjectId)).Id;
                        appointment.WhatsAppAccountId = whatsAppAccountId;
                        dbContext.Entry(appointment).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                    }
                    var inviteLink = appointment.WhatsAppGroupInviteLink ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(appointment.WhatsAppGroupJid))
                    {
                        Console.WriteLine($"[Hangfire Group Lifecycle] Creating group for appointment {appointment.Id}: '{groupSubject}'");
                        var managerPhone = NormalizeWhatsAppParticipantPhone(settings.GroupAutomationManagerPhone);
                        if (string.IsNullOrEmpty(managerPhone))
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Manager phone is not configured for project {appointment.ProjectId}. Skipping appointment {appointment.Id} without guessing a recipient.");
                            continue;
                        }
                        var session = await gatewaySessionClient.GetAsync(
                            appointment.ProjectId,
                            whatsAppAccountId.Value);
                        if (!session.Connected || !session.ConnectedAt.HasValue)
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] WhatsApp account {whatsAppAccountId} is disconnected. Appointment {appointment.Id} will retry in the next daily run.");
                            continue;
                        }
                        var payload = new
                        {
                            projectId = appointment.ProjectId,
                            whatsappAccountId = whatsAppAccountId,
                            subject = groupSubject,
                            participants = new[] { managerPhone },
                            idempotencyKey = $"group:{appointment.Id:N}",
                            expectedConnectedAt = session.ConnectedAt
                        };
                        var jsonPayload = JsonSerializer.Serialize(payload);
                        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                        using var gatewayResponse = await httpClient.PostAsync($"{gatewayUrl}/api/whatsapp/group/create", content);
                        var responseBody = await gatewayResponse.Content.ReadAsStringAsync();
                        if (!gatewayResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Failed to create group for appointment {appointment.Id}. Gateway response: {responseBody}");
                            continue;
                        }
                        using var responseDoc = JsonDocument.Parse(responseBody);
                        var responseRoot = responseDoc.RootElement;
                        var groupJid = responseRoot.GetProperty("jid").GetString() ?? string.Empty;
                        inviteLink = responseRoot.GetProperty("inviteLink").GetString() ?? string.Empty;
                        if (string.IsNullOrEmpty(groupJid))
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Invalid response from gateway for appointment {appointment.Id}: {responseBody}");
                            continue;
                        }
                        appointment.WhatsAppGroupJid = groupJid;
                        appointment.WhatsAppGroupInviteLink = string.IsNullOrWhiteSpace(inviteLink)
                            ? null
                            : inviteLink;
                        dbContext.Entry(appointment).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        Console.WriteLine($"[Hangfire Group Lifecycle] Successfully created group: {groupJid}, link: {inviteLink}");
                    }
                    else
                    {
                        Console.WriteLine($"[Hangfire Group Lifecycle] Reconciling reminders for existing group {appointment.WhatsAppGroupJid}.");
                    }

                    if (string.IsNullOrWhiteSpace(inviteLink))
                    {
                        var inviteSession = await gatewaySessionClient.GetAsync(
                            appointment.ProjectId,
                            whatsAppAccountId.Value);
                        if (!inviteSession.Connected || !inviteSession.ConnectedAt.HasValue)
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Invite link for group {appointment.WhatsAppGroupJid} will retry in the next daily run.");
                            continue;
                        }
                        var invitePayload = JsonSerializer.Serialize(new
                        {
                            projectId = appointment.ProjectId,
                            whatsappAccountId = whatsAppAccountId,
                            groupJid = appointment.WhatsAppGroupJid,
                            expectedConnectedAt = inviteSession.ConnectedAt
                        });
                        using var inviteContent = new StringContent(invitePayload, Encoding.UTF8, "application/json");
                        using var inviteResponse = await httpClient.PostAsync($"{gatewayUrl}/api/whatsapp/group/invite", inviteContent);
                        var inviteResponseBody = await inviteResponse.Content.ReadAsStringAsync();
                        if (!inviteResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Failed to reconcile invite link for {appointment.Id}: {inviteResponseBody}");
                            continue;
                        }
                        using var inviteDocument = JsonDocument.Parse(inviteResponseBody);
                        inviteLink = inviteDocument.RootElement.GetProperty("inviteLink").GetString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(inviteLink)) continue;
                        appointment.WhatsAppGroupInviteLink = inviteLink;
                        await dbContext.SaveChangesAsync();
                    }

                    // Find booked students
                    var bookings = await dbContext.GroupAppointmentBookings
                        .IgnoreQueryFilters()
                        .Where(b => b.GroupAppointmentId == appointment.Id && b.ProjectId == appointment.ProjectId)
                        .ToListAsync();

                    Console.WriteLine($"[Hangfire Group Lifecycle] Found {bookings.Count} bookings for appointment {appointment.Id}. Scheduling follow-ups...");

                    var aiBehaviorSettingsService = scope.ServiceProvider.GetRequiredService<Modules.AI.Services.IAIBehaviorSettingsService>();
                    var aiBehavior = aiBehaviorSettingsService.Resolve(settings, "WhatsApp");

                    // Fallback reminder templates
                    string reminderTemplateOnline = "أهلاً يا {customerName}، هذا هو رابط الجروب الذي سيرسل عليه رابط الحصة: {groupInviteLink}";
                    string reminderTemplateOffline = "أهلاً يا {customerName}، هذا هو رابط الجروب: {groupInviteLink}. نحن بانتظاركم!";

                    // Fetch template override from fallbacks in AI behavior settings if present
                    if (aiBehavior?.Fallbacks != null)
                    {
                        if (!string.IsNullOrEmpty(aiBehavior.Fallbacks.GroupReminderOnline))
                        {
                            reminderTemplateOnline = aiBehavior.Fallbacks.GroupReminderOnline;
                        }
                        if (!string.IsNullOrEmpty(aiBehavior.Fallbacks.GroupReminderOffline))
                        {
                            reminderTemplateOffline = aiBehavior.Fallbacks.GroupReminderOffline;
                        }
                    }

                    var selectedTemplate = appointment.Mode == "online" ? reminderTemplateOnline : reminderTemplateOffline;

                    foreach (var booking in bookings)
                    {
                        // Check if customer is blacklisted
                        var customer = await dbContext.Customers
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.Id == booking.CustomerId && c.ProjectId == appointment.ProjectId);

                        if (customer == null || customer.IsBlacklisted)
                        {
                            Console.WriteLine($"[Hangfire Group Lifecycle] Customer {booking.CustomerId} is blacklisted or not found. Skipping follow-ups.");
                            continue;
                        }

                        // Schedule Session Reminder FollowUp (immediate)
                        var reminderNotes = selectedTemplate
                            .Replace("{customerName}", booking.CustomerName)
                            .Replace("{groupInviteLink}", inviteLink)
                            .Replace("{waveName}", appointment.Name)
                            .Replace("{groupName}", appointment.Name);

                        var reminderId = DeterministicGroupFollowUpId(appointment.Id, booking.Id, "invite");
                        var reminderExists = await dbContext.FollowUps.IgnoreQueryFilters()
                            .AnyAsync(followUp => followUp.Id == reminderId
                                || (followUp.ProjectId == appointment.ProjectId
                                    && followUp.CustomerId == booking.CustomerId
                                    && followUp.WhatsAppAccountId == whatsAppAccountId
                                    && followUp.Type == "AppointmentReminder"
                                    && followUp.AppointmentTime == appointment.DateTime));
                        if (!reminderExists) dbContext.FollowUps.Add(new CRM.Domain.FollowUp
                        {
                            Id = reminderId,
                            ProjectId = appointment.ProjectId,
                            CustomerId = booking.CustomerId,
                            WhatsAppAccountId = whatsAppAccountId,
                            Channel = "WhatsApp",
                            DueDate = DateTime.UtcNow,
                            Status = "Pending",
                            Type = "AppointmentReminder",
                            AppointmentTime = appointment.DateTime,
                            Notes = reminderNotes,
                            Tone = "Default"
                        });

                        // Schedule Post-Session 2-day FollowUp
                        var postSessionId = DeterministicGroupFollowUpId(appointment.Id, booking.Id, "post");
                        var postSessionExists = await dbContext.FollowUps.IgnoreQueryFilters()
                            .AnyAsync(followUp => followUp.Id == postSessionId
                                || (followUp.ProjectId == appointment.ProjectId
                                    && followUp.CustomerId == booking.CustomerId
                                    && followUp.WhatsAppAccountId == whatsAppAccountId
                                    && followUp.Type == "Nurturing"
                                    && followUp.AppointmentTime == appointment.DateTime));
                        if (!postSessionExists) dbContext.FollowUps.Add(new CRM.Domain.FollowUp
                        {
                            Id = postSessionId,
                            ProjectId = appointment.ProjectId,
                            CustomerId = booking.CustomerId,
                            WhatsAppAccountId = whatsAppAccountId,
                            Channel = "WhatsApp",
                            DueDate = appointment.DateTime.AddDays(2),
                            Status = "Pending",
                            Type = "Nurturing",
                            AppointmentTime = appointment.DateTime,
                            Notes = "طمننا يا فندم، هل حضرت السيشن واشتركت معانا؟",
                            Tone = "Default"
                        });
                    }

                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Hangfire Group Lifecycle] Error processing appointment {appointment.Id}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                }
            }
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

        private static Guid DeterministicGroupFollowUpId(
            Guid appointmentId,
            Guid bookingId,
            string kind)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes($"group-followup:{appointmentId:N}:{bookingId:N}:{kind}"));
            return new Guid(bytes.AsSpan(0, 16));
        }

        private static bool CanDispatchInCurrentConnection(
            FollowUp followUp,
            Modules.Advertising.Services.WhatsAppGatewaySessionStatus session) =>
            session.Connected
            && session.ConnectedAt.HasValue
            && followUp.DueDate >= session.ConnectedAt.Value.UtcDateTime;

        private static bool TryDeferToNextDailySlot(
            FollowUp followUp,
            DateTime nowUtc,
            TimeZoneInfo timezone)
        {
            var nextDueDate = WhatsAppDailyDeliverySchedule.NextOccurrenceAfter(
                followUp.DueDate,
                nowUtc,
                timezone);
            if (followUp.Type == "AppointmentReminder"
                && followUp.AppointmentTime.HasValue
                && nextDueDate >= followUp.AppointmentTime.Value)
            {
                followUp.Status = "Cancelled";
                followUp.UpdatedAt = nowUtc;
                return false;
            }

            followUp.DueDate = nextDueDate;
            followUp.Status = "Pending";
            followUp.UpdatedAt = nowUtc;
            return true;
        }

        private static void MarkConversationDeliveryUnknown(
            Conversation? conversation,
            string deliveryKey)
        {
            if (conversation is null) return;
            conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow;
            conversation.WhatsAppDeliveryUnknownKey = deliveryKey;
        }

        private static string NormalizeWhatsAppParticipantPhone(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                return string.Empty;
            }

            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("00"))
            {
                digits = digits[2..];
            }

            if (digits.Length == 11 && digits.StartsWith("0"))
            {
                digits = $"20{digits[1..]}";
            }

            return digits;
        }

        private static string BuildWhatsAppGroupSubject(string appointmentName, string appointmentMode, DateTime appointmentDateTime, TimeZoneInfo timezone)
        {
            var utcDateTime = appointmentDateTime.Kind == DateTimeKind.Utc
                ? appointmentDateTime
                : DateTime.SpecifyKind(appointmentDateTime, DateTimeKind.Utc);
            var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timezone);
            var formattedDateTime = localDateTime.ToString("d MMMM yyyy h:mm tt", new CultureInfo("ar-EG"));
            var groupKind = appointmentMode == "online"
                ? "أونلاين"
                : appointmentMode == "offline"
                    ? "أوفلاين"
                    : appointmentName;

            return $"مجموعة {groupKind} - {formattedDateTime}";
        }
    }
}
