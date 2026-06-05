using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Modules.AI.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;

namespace Modules.AI.Workers
{
    public class AIReplyWorker : IIntegrationEventHandler<MessageAggregatedEvent>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAIMarketingBrain _aiMarketingBrain;
        private readonly IEventBus _eventBus;

        public AIReplyWorker(
            IServiceProvider serviceProvider,
            IAIMarketingBrain aiMarketingBrain,
            IEventBus eventBus)
        {
            _serviceProvider = serviceProvider;
            _aiMarketingBrain = aiMarketingBrain;
            _eventBus = eventBus;
        }

        public async Task HandleAsync(MessageAggregatedEvent @event)
        {
            Console.WriteLine($"[AIReplyWorker] Received aggregated message for Project: {@event.ProjectId}, Sender: {@event.Sender}");

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Find customer to get customerId
            var customer = await dbContext.Customers
                .FirstOrDefaultAsync(c => c.PhoneNumber == @event.Sender);

            // Query ProjectSettings
            var settings = await dbContext.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId);

            if (settings == null)
            {
                Console.WriteLine($"[AIReplyWorker] ProjectSettings not found for project {@event.ProjectId}. Skipping AI reply.");
                return;
            }

            if (!settings.AiAutoReplyEnabled)
            {
                Console.WriteLine($"[AIReplyWorker] AI Auto-Reply is disabled for project {@event.ProjectId}. Skipping AI reply.");
                if (customer != null)
                {
                    await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                }
                return;
            }

            // Decide which API key to use. Per-project key, or fall back to system default.
            string apiKeyOverride = !string.IsNullOrEmpty(settings.GeminiApiKey) ? settings.GeminiApiKey : null;

            // Retrieve matching context from the Company Brain (Knowledge Base)
            string brainContext = null;
            try
            {
                var companyBrain = scope.ServiceProvider.GetRequiredService<Modules.Brain.Services.IAICompanyBrain>();
                var chunks = await companyBrain.SearchBrainAsync(@event.ProjectId, @event.Content, limit: 3);
                if (chunks != null && chunks.Any())
                {
                    brainContext = string.Join("\n\n", chunks.Select(c => $"- {c.ChunkText}"));
                    Console.WriteLine($"[AIReplyWorker] Injected {chunks.Count} knowledge chunks into AI prompt context.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] Failed to query company brain: {ex.Message}");
            }

            // Inject Group Appointments context if enabled
            if (settings.IsGroupAppointmentsEnabled)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var activeGroups = await dbContext.GroupAppointments
                        .Include(g => g.Bookings)
                        .Where(g => g.ProjectId == @event.ProjectId && g.IsActive && g.DateTime > now)
                        .OrderBy(g => g.DateTime)
                        .ToListAsync();

                    var groupsContextList = new System.Collections.Generic.List<string>();
                    TimeZoneInfo projectZone;
                    try
                    {
                        projectZone = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
                    }
                    catch
                    {
                        projectZone = TimeZoneInfo.Utc;
                    }

                    // Filter out full groups - don't show them to the AI at all
                    var availableGroups = activeGroups.Where(g => g.Bookings.Count < g.Capacity).ToList();
                    Console.WriteLine($"[AIReplyWorker] Active groups: {activeGroups.Count}, Available (not full): {availableGroups.Count}");

                    foreach (var g in availableGroups)
                    {
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(g.DateTime, projectZone);
                        groupsContextList.Add($"- معرف المجموعة (ID): {g.Id}\n  اسم المجموعة: {g.Name}\n  الموعد: {localTime:dd/MM/yyyy h:mm tt}");
                    }


                    // Check if this customer is already booked in any active group
                    var customerAlreadyBookedIn = new System.Collections.Generic.HashSet<Guid>();
                    if (customer != null)
                    {
                        foreach (var g in activeGroups)
                        {
                            if (g.Bookings.Any(b => b.CustomerId == customer.Id))
                                customerAlreadyBookedIn.Add(g.Id);
                        }
                    }
                    var alreadyBookedNote = customerAlreadyBookedIn.Count > 0
                        ? $"\nملاحظة: هذا العميل مسجل بالفعل في {customerAlreadyBookedIn.Count} مجموعة/مجموعات. إذا طلب الحجز في مجموعة مسجل فيها بالفعل، أخبره أنه مسجل مسبقاً ولا تسجله مرة أخرى (اترك suggestedGroupBookingId = null)."
                        : "";
                    
                    var groupsContextText = "معلومات مواعيد المجموعات المتاحة للحجز (Group Appointments):\n" +
                                            "إذا سأل العميل عن المجموعات أو المواعيد المتاحة أو يرغب في الحجز، اعرض عليه أسماء المجموعات المتاحة ومواعيدها فقط. لا تذكر أبداً عدد الأماكن المتبقية أو السعة أو أي أرقام. إذا أراد الحجز، ضع suggestedGroupBookingId = معرف المجموعة (ID) وأكد له الحجز في ردك. النظام سيسجله تلقائياً. لا ترسل أي رابط حجز للعميل. إذا لم تكن هناك مجموعات متاحة، أخبره أن المجموعات مكتملة حالياً.\n" +
                                            alreadyBookedNote + "\n\n" +
                                            "قائمة المجموعات المتاحة حالياً:\n" +
                                            (groupsContextList.Any() ? string.Join("\n", groupsContextList) : "- لا توجد مجموعات متاحة حالياً للحجز.");

                    if (string.IsNullOrEmpty(brainContext))
                    {
                        brainContext = groupsContextText;
                    }
                    else
                    {
                        brainContext = groupsContextText + "\n\n" + brainContext;
                    }
                    Console.WriteLine($"[AIReplyWorker] Injected Group Appointments context (Found {activeGroups.Count} active, {availableGroups.Count} with slots).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to query active group appointments for AI context: {ex.Message}");
                }
            }

            if (customer != null && customer.IsBlacklisted)
            {
                Console.WriteLine($"[AIReplyWorker] Customer {@event.Sender} is blacklisted. Skipping AI reply.");
                await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                return;
            }

            Guid customerId = customer?.Id ?? Guid.Empty;

            // Fetch chat history for context
            string chatHistory = null;
            Conversation conversation = null;
            if (customerId != Guid.Empty)
            {
                try
                {
                    conversation = await dbContext.Conversations
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Status != "Closed");

                    if (conversation != null)
                    {
                        var historyMessages = await dbContext.Messages
                            .Where(m => m.ConversationId == conversation.Id)
                            .OrderByDescending(m => m.Timestamp)
                            .Take(15) // Limit history to last 15 messages
                            .ToListAsync();

                        historyMessages.Reverse(); // Chronological order

                        chatHistory = string.Join("\n", historyMessages.Select(m => 
                            $"{(m.Direction == "Incoming" ? "Customer" : "Agent/AI")}: {m.Content}"));
                        
                        Console.WriteLine($"[AIReplyWorker] Injected {historyMessages.Count} history messages into AI prompt context.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to query chat history: {ex.Message}");
                }
            }

            // Retrieve CustomerMemory
            string customerMemory = null;
            if (customerId != Guid.Empty)
            {
                try
                {
                    var memory = await dbContext.CustomerMemories
                        .FirstOrDefaultAsync(m => m.CustomerId == customerId);
                    if (memory != null)
                    {
                        var summaryText = memory.LongTermSummary;
                        var factsText = string.IsNullOrEmpty(memory.FactsJson) || memory.FactsJson == "[]"
                            ? ""
                            : "\nFacts: " + string.Join(", ", System.Text.Json.JsonSerializer.Deserialize<string[]>(memory.FactsJson));
                        var objectionsText = string.IsNullOrEmpty(memory.ObjectionsJson) || memory.ObjectionsJson == "[]"
                            ? ""
                            : "\nObjections: " + string.Join(", ", System.Text.Json.JsonSerializer.Deserialize<string[]>(memory.ObjectionsJson));

                        customerMemory = $"Summary: {summaryText}{factsText}{objectionsText}";
                        Console.WriteLine($"[AIReplyWorker] Injected Customer Memory: {customerMemory}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to query customer memory: {ex.Message}");
                }
            }

            // Fetch existing customer labels to restrict options
            string[] existingLabels = Array.Empty<string>();
            try
            {
                existingLabels = await dbContext.Customers
                    .Where(c => c.ProjectId == @event.ProjectId && c.Label != null && c.Label != "")
                    .Select(c => c.Label)
                    .Distinct()
                    .ToArrayAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] Failed to query existing labels: {ex.Message}");
            }

            // Construct customer profile description to probe for missing data
            string customerProfile = $"Name: {(string.IsNullOrEmpty(customer?.Name) ? "Missing" : customer.Name)}\n" +
                                     $"City: {(string.IsNullOrEmpty(customer?.City) ? "Missing" : customer.City)}";

            // Check for media attachments in the active conversation
            byte[] fileBytes = null;
            string mimeType = null;
            Message latestMediaMsg = null;

            if (conversation != null)
            {
                latestMediaMsg = await dbContext.Messages
                    .Where(m => m.ConversationId == conversation.Id && m.Direction == "Incoming" && m.AssetId != null)
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefaultAsync();

                if (latestMediaMsg != null)
                {
                    var timeDiff = DateTime.UtcNow - latestMediaMsg.Timestamp;
                    if (timeDiff.TotalMinutes <= 2.0)
                    {
                        var asset = await dbContext.Assets
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(a => a.Id == latestMediaMsg.AssetId);

                        if (asset != null)
                        {
                            try
                            {
                                var storageService = scope.ServiceProvider.GetRequiredService<Modules.Media.Services.IMinIoStorageService>();
                                using var stream = await storageService.DownloadFileAsync(asset.StoragePath);
                                using var ms = new System.IO.MemoryStream();
                                await stream.CopyToAsync(ms);
                                fileBytes = ms.ToArray();
                                mimeType = asset.ContentType;
                                Console.WriteLine($"[AIReplyWorker] Downloaded multimodal media: {asset.FileName} ({fileBytes.Length} bytes) of type {mimeType}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[AIReplyWorker] Failed to download media asset from MinIO: {ex.Message}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[AIReplyWorker] Generating AI response using AIMarketingBrain...");
            var analysisResult = await _aiMarketingBrain.AnalyzeAndGenerateReplyAsync(
                @event.Content, 
                apiKeyOverride, 
                brainContext, 
                chatHistory, 
                customerMemory,
                existingLabels,
                customerProfile,
                fileBytes,
                mimeType,
                settings.AiTonePreference,
                settings.AiTargetAudience);

            if (latestMediaMsg != null && !string.IsNullOrEmpty(analysisResult.Transcription))
            {
                latestMediaMsg.Transcription = analysisResult.Transcription;
                dbContext.Entry(latestMediaMsg).State = EntityState.Modified;
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Saved voice note transcription: {latestMediaMsg.Transcription}");
            }

            Console.WriteLine($"[AIReplyWorker] AI Response: {analysisResult.ReplyContent}");

            // 1. Publish CRM Update suggestion
            var crmSuggestion = new CRMUpdateSuggestedEvent
            {
                ProjectId = @event.ProjectId,
                CustomerId = customerId,
                Sender = @event.Sender,
                City = analysisResult.Entities?.City,
                Budget = analysisResult.Entities?.Budget,
                Interests = analysisResult.Entities?.Interests ?? Array.Empty<string>(),
                Timeline = analysisResult.Entities?.Timeline,
                Intent = analysisResult.Intent,
                Sentiment = analysisResult.Sentiment,
                Confidence = analysisResult.Confidence,
                Label = analysisResult.Label,
                PipelineStage = analysisResult.PipelineStage,
                FollowUpNeeded = analysisResult.SuggestedFollowUp?.Needed ?? false,
                FollowUpType = analysisResult.SuggestedFollowUp?.Type,
                FollowUpAppointmentTime = analysisResult.SuggestedFollowUp?.AppointmentTime,
                FollowUpDueDate = analysisResult.SuggestedFollowUp?.DueDate,
                FollowUpNotes = analysisResult.SuggestedFollowUp?.Notes
            };
            await _eventBus.PublishAsync(crmSuggestion);
            Console.WriteLine($"[AIReplyWorker] Published CRMUpdateSuggestedEvent for {@event.Sender}");

            // 2. Publish AI Reply
            var replyGeneratedEvent = new AIReplyGeneratedEvent
            {
                ProjectId = @event.ProjectId,
                Sender = @event.Sender,
                Content = analysisResult.ReplyContent,
                Buttons = analysisResult.SuggestedButtons ?? Array.Empty<string>()
            };

            await _eventBus.PublishAsync(replyGeneratedEvent);
            Console.WriteLine($"[AIReplyWorker] Published AIReplyGeneratedEvent for {@event.Sender}");

            // 2.5. Process AI Auto-Booking if suggestedGroupBookingId is set
            if (!string.IsNullOrEmpty(analysisResult.SuggestedGroupBookingId))
            {
                try
                {
                    if (Guid.TryParse(analysisResult.SuggestedGroupBookingId, out var groupId))
                    {
                        var group = await dbContext.GroupAppointments
                            .Include(g => g.Bookings)
                            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == @event.ProjectId && g.IsActive);

                        if (group == null)
                        {
                            Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Group {groupId} not found or inactive.");
                        }
                        else if (group.Bookings.Count >= group.Capacity)
                        {
                            Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Group '{group.Name}' is full ({group.Bookings.Count}/{group.Capacity}).");
                        }
                        else
                        {
                            // Resolve or create customer for the booking
                            var bookingCustomerId = customer?.Id ?? Guid.Empty;
                            var bookingCustomerName = customer?.Name ?? @event.Sender;
                            var bookingCustomerPhone = @event.Sender;

                            // Check if already booked
                            var alreadyBooked = group.Bookings.Any(b => b.CustomerPhone == bookingCustomerPhone || b.CustomerId == bookingCustomerId);
                            if (alreadyBooked)
                            {
                                Console.WriteLine($"[AIReplyWorker] Auto-booking skipped: Customer {bookingCustomerPhone} already booked in group '{group.Name}'.");
                            }
                            else
                            {
                                // Create the booking
                                var booking = new GroupAppointmentBooking
                                {
                                    Id = Guid.NewGuid(),
                                    ProjectId = @event.ProjectId,
                                    GroupAppointmentId = groupId,
                                    CustomerId = bookingCustomerId,
                                    CustomerName = bookingCustomerName,
                                    CustomerPhone = bookingCustomerPhone
                                };

                                dbContext.GroupAppointmentBookings.Add(booking);
                                await dbContext.SaveChangesAsync();

                                Console.WriteLine($"[AIReplyWorker] ✅ Auto-booked customer {bookingCustomerPhone} ('{bookingCustomerName}') into group '{group.Name}' ({group.Bookings.Count + 1}/{group.Capacity}).");

                                // Broadcast update via SignalR to refresh dashboard
                                try
                                {
                                    var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                                    await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("GroupBookingUpdated", new
                                    {
                                        groupId = groupId,
                                        groupName = group.Name,
                                        customerPhone = bookingCustomerPhone,
                                        customerName = bookingCustomerName,
                                        newBookedCount = group.Bookings.Count + 1,
                                        capacity = group.Capacity,
                                        isFull = (group.Bookings.Count + 1) >= group.Capacity
                                    });
                                }
                                catch (Exception signalREx)
                                {
                                    Console.WriteLine($"[AIReplyWorker] SignalR broadcast for group booking failed: {signalREx.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Invalid GUID '{analysisResult.SuggestedGroupBookingId}'.");
                    }
                }
                catch (Exception bookingEx)
                {
                    Console.WriteLine($"[AIReplyWorker] Auto-booking error: {bookingEx.Message}");
                }
            }

            // 3. Process AI Auto-Reaction if suggested
            if (!string.IsNullOrEmpty(analysisResult.SuggestedReaction))
            {
                try
                {
                    if (conversation != null)
                    {
                        var targetMessage = await dbContext.Messages
                            .Where(m => m.ConversationId == conversation.Id && m.Direction == "Incoming")
                            .OrderByDescending(m => m.Timestamp)
                            .FirstOrDefaultAsync();

                        if (targetMessage != null)
                        {
                            var reactionMessage = new Message
                            {
                                ConversationId = conversation.Id,
                                ExternalMessageId = $"msg_ai_react_{Guid.NewGuid().ToString("N")}",
                                Direction = "Outgoing",
                                Content = $"[تفاعل] {analysisResult.SuggestedReaction}",
                                MessageType = "Reaction",
                                Timestamp = DateTime.UtcNow
                            };

                            dbContext.Messages.Add(reactionMessage);
                            await dbContext.SaveChangesAsync();

                            var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";

                            var gatewayPayload = new
                            {
                                projectId = conversation.ProjectId,
                                to = @event.Sender,
                                reactionText = analysisResult.SuggestedReaction,
                                targetMessageId = targetMessage.ExternalMessageId,
                                targetFromMe = false
                            };

                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(gatewayPayload, new System.Text.Json.JsonSerializerOptions 
                            { 
                                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
                            });

                            var httpClient = new System.Net.Http.HttpClient();
                            var gatewayResponse = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(httpClient, $"{gatewayUrl}/api/whatsapp/react", jsonPayload);
                            if (gatewayResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"[AIReplyWorker] Sent reaction {analysisResult.SuggestedReaction} to message {targetMessage.ExternalMessageId}");
                            }
                            else
                            {
                                var body = await gatewayResponse.Content.ReadAsStringAsync();
                                Console.WriteLine($"[AIReplyWorker] Gateway reaction returned {gatewayResponse.StatusCode}: {body}");
                            }

                            // Broadcast via SignalR to project group
                            var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                            var signalrPayload = new
                            {
                                id = reactionMessage.Id,
                                conversationId = reactionMessage.ConversationId,
                                senderType = "AI",
                                content = reactionMessage.Content,
                                createdAt = reactionMessage.Timestamp.ToString("o"),
                                status = "Sent",
                                mediaUrl = (string)null,
                                mediaType = (string)null,
                                messageType = "Reaction"
                            };

                            await hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to process auto-reaction: {ex.Message}");
                }
            }
        }

        private async Task CompletePendingFollowUpsAsync(AppDbContext dbContext, Guid customerId)
        {
            try
            {
                var pending = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.CustomerId == customerId && f.Status == "Pending")
                    .ToListAsync();

                foreach (var fu in pending)
                {
                    dbContext.FollowUps.Remove(fu);
                }
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Deleted {pending.Count} pending follow-ups for skipped customer {customerId}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] Error completing/deleting follow-ups: {ex.Message}");
            }
        }
    }
}
