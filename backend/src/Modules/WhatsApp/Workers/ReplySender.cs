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

namespace Modules.WhatsApp.Workers
{
    public class ReplySender : IIntegrationEventHandler<AIReplyGeneratedEvent>
    {
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;
        private readonly IHumanMessagingEngine _messagingEngine;
        private readonly IServiceProvider _serviceProvider;

        public ReplySender(IConfiguration configuration, IHumanMessagingEngine messagingEngine, IServiceProvider serviceProvider)
        {
            _httpClient = new HttpClient();
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

            Console.WriteLine($"[ReplySender] Received AIReplyGeneratedEvent for Project: {@event.ProjectId}, Sender: {@event.Sender}");

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

                    if (customer != null)
                    {
                        var conversation = await dbContext.Conversations
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

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
                                thinkingDelay = _messagingEngine.CalculateThinkingDelay(lastIncoming.Content, @event.ProjectId);
                            }

                            // Calculate total remaining typing delay for all chunks
                            var allChunks = System.Linq.Enumerable.ToList(_messagingEngine.SplitIntoChunks(@event.Content));
                            int totalTypingDelay = 0;
                            for (int idx = 0; idx < allChunks.Count; idx++)
                            {
                                totalTypingDelay += _messagingEngine.CalculateTypingDelay(allChunks[idx], @event.ProjectId);
                                if (idx > 0)
                                {
                                    totalTypingDelay += 3000; // Average stagger delay
                                }
                            }

                            int totalRemainingMs = thinkingDelay + totalTypingDelay;
                            int estSec = (int)Math.Ceiling(totalRemainingMs / 1000.0);

                             var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                             try
                             {
                                 await redis.StringSetAsync($"ai_typing:{conversation.Id}", "typing", TimeSpan.FromSeconds(estSec));
                             }
                             catch (Exception redisEx)
                             {
                                 Console.WriteLine($"[ReplySender] Redis set initial failed: {redisEx.Message}");
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

                var chunks = System.Linq.Enumerable.ToList(_messagingEngine.SplitIntoChunks(@event.Content));

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];

                    // Smart typing delay occurs BEFORE sending the chunk!
                    int delayMs = _messagingEngine.CalculateTypingDelay(chunk, @event.ProjectId);
                    Console.WriteLine($"[ReplySender] Simulating human typing delay of {delayMs}ms...");

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
                            var conversation = await dbContext.Conversations
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

                            if (conversation != null)
                            {
                                int remainingTypingMs = 0;
                                for (int j = i; j < chunks.Count; j++)
                                {
                                    remainingTypingMs += _messagingEngine.CalculateTypingDelay(chunks[j], @event.ProjectId);
                                    if (j > i)
                                    {
                                        remainingTypingMs += 3000; // Average stagger delay
                                    }
                                }
                                 int estSec = (int)Math.Ceiling(remainingTypingMs / 1000.0);

                                 var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                                 try
                                 {
                                     await redis.StringSetAsync($"ai_typing:{conversation.Id}", "typing", TimeSpan.FromSeconds(estSec));
                                 }
                                 catch (Exception redisEx)
                                 {
                                     Console.WriteLine($"[ReplySender] Redis set loop failed: {redisEx.Message}");
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

                    var payload = new
                    {
                        projectId = @event.ProjectId,
                        to = @event.Sender,
                        message = chunk
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);

                    try
                    {
                        var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(_httpClient, $"{_gatewayUrl}/api/whatsapp/send", jsonPayload);
                        var responseBody = await response.Content.ReadAsStringAsync();
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"[ReplySender] Successfully sent AI reply chunk to {@event.Sender} via Gateway.");

                            // Save message to database and broadcast via SignalR
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                                tenantContext.SetProjectId(@event.ProjectId);

                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

                                // Find customer
                                var customer = await dbContext.Customers
                                    .IgnoreQueryFilters()
                                    .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.PhoneNumber == @event.Sender);

                                if (customer != null)
                                {
                                    // Find open/pending conversation
                                    var conversation = await dbContext.Conversations
                                        .IgnoreQueryFilters()
                                        .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

                                    if (conversation != null)
                                    {
                                        var message = new Message
                                        {
                                            ConversationId = conversation.Id,
                                            ExternalMessageId = $"msg_ai_{Guid.NewGuid().ToString("N")}",
                                            Direction = "Outgoing",
                                            Content = chunk,
                                            MessageType = "Text",
                                            Timestamp = DateTime.UtcNow
                                        };

                                        dbContext.Messages.Add(message);
                                        
                                        conversation.LastMessageTimestamp = DateTime.UtcNow;
                                        dbContext.Entry(conversation).State = EntityState.Modified;

                                        await dbContext.SaveChangesAsync();

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

                                        await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[ReplySender] Gateway returned error code {response.StatusCode}: {responseBody}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ReplySender] Exception while calling WhatsApp Gateway: {ex.Message}");
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

                        int staggerDelayMs = isTest ? 100 : new Random().Next(2, 5) * 1000;
                        Console.WriteLine($"[ReplySender] Waiting {staggerDelayMs}ms stagger delay between message chunks...");
                        await Task.Delay(staggerDelayMs);
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
                         var conversation = await dbContext.Conversations
                             .IgnoreQueryFilters()
                             .FirstOrDefaultAsync(c => c.ProjectId == @event.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

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
    }
}
