using Microsoft.Extensions.Configuration;
using Shared.Events;
using Shared.Queue;
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

        public ReplySender(IConfiguration configuration, IHumanMessagingEngine messagingEngine)
        {
            _httpClient = new HttpClient();
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            _messagingEngine = messagingEngine;
        }

        public async Task HandleAsync(AIReplyGeneratedEvent @event)
        {
            Console.WriteLine($"[ReplySender] Received AIReplyGeneratedEvent for Project: {@event.ProjectId}, Sender: {@event.Sender}");

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
                int delayMs = _messagingEngine.CalculateTypingDelay(chunk);
                Console.WriteLine($"[ReplySender] Simulating human typing delay of {delayMs}ms...");
                await Task.Delay(delayMs);
            }
        }
    }
}
