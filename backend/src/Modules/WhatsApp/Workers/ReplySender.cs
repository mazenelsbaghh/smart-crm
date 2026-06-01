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
            Console.WriteLine($"[ReplySender] Received AIReplyGeneratedEvent for Project: {@event.ProjectId}, Sender: {@event.Sender}");

            try
            {
                var chunks = _messagingEngine.SplitIntoChunks(@event.Content);

                foreach (var chunk in chunks)
                {
                    var payload = new
                    {
                        projectId = @event.ProjectId,
                        to = @event.Sender,
                        message = chunk
                    };

                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                    try
                    {
                        var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/send", jsonContent);
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

                    // Human typing delay
                    int delayMs = _messagingEngine.CalculateTypingDelay(chunk, @event.ProjectId);
                    Console.WriteLine($"[ReplySender] Simulating human typing delay of {delayMs}ms...");
                    await Task.Delay(delayMs);
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
                            await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                            {
                                conversationId = conversation.Id,
                                isTyping = false
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
