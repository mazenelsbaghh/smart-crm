using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using Modules.Conversations.Domain;

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

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task CheckOverdueFollowUpsJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();

            var now = DateTime.UtcNow;
            var overdueFollowUps = await dbContext.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.Status == "Pending" && f.DueDate < now)
                .ToListAsync();

            if (!overdueFollowUps.Any())
            {
                Console.WriteLine($"[Hangfire Job] No overdue follow-ups found. {await dbContext.FollowUps.IgnoreQueryFilters().CountAsync(f => f.Status == "Pending")} pending follow-ups scheduled for future.");
                return;
            }

            Console.WriteLine($"[Hangfire Job] Found {overdueFollowUps.Count} pending follow-ups to execute.");

            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            using var httpClient = new HttpClient();

            foreach (var followUp in overdueFollowUps)
            {
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

                    // Check if customer has any paid group booking
                    var hasPaid = await dbContext.GroupAppointmentBookings
                        .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid && b.ProjectId == followUp.ProjectId);

                    if (hasPaid)
                    {
                        Console.WriteLine($"[Hangfire Job] Customer {customer.PhoneNumber} has already paid. Cancelling follow-up {followUp.Id}.");
                        followUp.Status = "Cancelled";
                        dbContext.Entry(followUp).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    string messageContent = string.Empty;
                    if (!string.IsNullOrEmpty(followUp.Notes))
                    {
                        var notesTrimmed = followUp.Notes.Trim();
                        bool looksLikeDirectMessage = notesTrimmed.StartsWith("مرحباً", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("أهلاً", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("يا فندم", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("صباح الخير", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("مساء الخير", StringComparison.OrdinalIgnoreCase) || 
                                                     notesTrimmed.StartsWith("السلام عليكم", StringComparison.OrdinalIgnoreCase);

                        if (looksLikeDirectMessage)
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
                                    var projectSettings = await dbContext.ProjectSettings
                                        .IgnoreQueryFilters()
                                        .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
                                    string apiKey = projectSettings?.GeminiApiKey;
                                    if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("mock_"))
                                    {
                                        apiKey = null; // Use default system key
                                    }
                                    string model = projectSettings?.GeminiModel;

                                    var hasAttended = await dbContext.GroupAppointmentBookings
                                        .AnyAsync(b => b.CustomerId == customer.Id && b.IsAttended);

                                    messageContent = await aiMarketingBrain.RewriteFollowUpNotesAsync(
                                        customer.Name,
                                        followUp.Notes,
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

                    var payload = new
                    {
                        projectId = followUp.ProjectId,
                        to = customer.PhoneNumber,
                        message = messageContent
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);
                    var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(httpClient, $"{gatewayUrl}/api/whatsapp/send", jsonPayload);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Hangfire Job] Successfully sent follow-up message to {customer.PhoneNumber}");

                        var conversation = await dbContext.Conversations
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.ProjectId == followUp.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

                        if (conversation == null)
                        {
                            conversation = new Conversation
                            {
                                ProjectId = followUp.ProjectId,
                                CustomerId = customer.Id,
                                Status = "Open",
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
                            ExternalMessageId = $"msg_fu_{Guid.NewGuid().ToString("N")}",
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
                            mediaType = (string)null
                        };

                        await hubContext.Clients.Group($"project_{followUp.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                    }
                    else
                    {
                        Console.WriteLine($"[Hangfire Job] Gateway error {response.StatusCode} for follow-up {followUp.Id}: {responseBody}. Marking as Missed.");
                        followUp.Status = "Missed";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Hangfire Job] Exception while executing follow-up {followUp.Id}: {ex.Message}. Marking as Missed.");
                    followUp.Status = "Missed";
                }
            }

            await dbContext.SaveChangesAsync();
        }

        public async Task RecalculateLeadScoresJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var customers = await dbContext.Customers
                .IgnoreQueryFilters()
                .ToListAsync();

            foreach (var customer in customers)
            {
                var missedCount = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .CountAsync(f => f.CustomerId == customer.Id && f.Status == "Missed");
                
                if (missedCount > 0)
                {
                    customer.LeadScore = Math.Max(0, customer.LeadScore - (missedCount * 2));
                }
            }

            await dbContext.SaveChangesAsync();
            Console.WriteLine($"[Hangfire Job] Recalculated lead scores for {customers.Count} customers.");
        }
    }
}
