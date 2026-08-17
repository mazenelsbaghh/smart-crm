using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure;
using System;
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
                s => s.RunWhatsAppGroupAutomationLifecycleJobAsync(),
                "0 23 * * *", // 11:00 PM every day Cairo time
                new RecurringJobOptions { TimeZone = cairoZone });

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

                    if (string.IsNullOrEmpty(customer.PhoneNumber) && string.IsNullOrEmpty(customer.FacebookPSID))
                    {
                        Console.WriteLine($"[Hangfire Job] Customer {customer.Id} has no phone number and no Facebook PSID. Marking follow-up {followUp.Id} as Missed.");
                        followUp.Status = "Missed";
                        dbContext.Entry(followUp).State = EntityState.Modified;
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    bool isMessenger = string.IsNullOrEmpty(customer.PhoneNumber) && !string.IsNullOrEmpty(customer.FacebookPSID);

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

                            var conversation = await dbContext.Conversations
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

        public async Task RunWhatsAppGroupAutomationLifecycleJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone("Africa/Cairo");
            var cairoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoZone);
            var startOfWindowCairo = cairoNow.Date;
            var endOfWindowCairo = cairoNow.Date.AddDays(2);

            var startOfWindowUtc = TimeZoneInfo.ConvertTimeToUtc(startOfWindowCairo, cairoZone);
            var endOfWindowUtc = TimeZoneInfo.ConvertTimeToUtc(endOfWindowCairo, cairoZone);

            Console.WriteLine($"[Hangfire Group Lifecycle] Checking for active waves/appointments from today through tomorrow in Cairo timezone (UTC range: {startOfWindowUtc:O} to {endOfWindowUtc:O})");

            var appointments = await dbContext.GroupAppointments
                .IgnoreQueryFilters()
                .Where(a => a.IsActive && a.DateTime >= startOfWindowUtc && a.DateTime < endOfWindowUtc)
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

                    if (!string.IsNullOrEmpty(appointment.WhatsAppGroupJid))
                    {
                        Console.WriteLine($"[Hangfire Group Lifecycle] Group already created for appointment {appointment.Id} (JID: {appointment.WhatsAppGroupJid}). Skipping creation.");
                        continue;
                    }

                    var groupSubject = BuildWhatsAppGroupSubject(appointment.Name, appointment.Mode, appointment.DateTime, cairoZone);
                    Console.WriteLine($"[Hangfire Group Lifecycle] Creating group for appointment {appointment.Id}: '{groupSubject}'");

                    var managerPhone = NormalizeWhatsAppParticipantPhone(settings.GroupAutomationManagerPhone);
                    if (string.IsNullOrEmpty(managerPhone))
                    {
                        managerPhone = "201068690092";
                    }

                    // Create the group via WhatsApp Gateway
                    var payload = new
                    {
                        projectId = appointment.ProjectId,
                        subject = groupSubject,
                        participants = new[] { managerPhone }
                    };

                    var jsonPayload = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var gatewayResponse = await httpClient.PostAsync($"{gatewayUrl}/api/whatsapp/group/create", content);

                    if (!gatewayResponse.IsSuccessStatusCode)
                    {
                        var errorBody = await gatewayResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[Hangfire Group Lifecycle] Failed to create group for appointment {appointment.Id}. Gateway response: {errorBody}");
                        continue;
                    }

                    var responseBody = await gatewayResponse.Content.ReadAsStringAsync();
                    using var responseDoc = JsonDocument.Parse(responseBody);
                    var responseRoot = responseDoc.RootElement;

                    string groupJid = responseRoot.GetProperty("jid").GetString() ?? "";
                    string inviteLink = responseRoot.GetProperty("inviteLink").GetString() ?? "";

                    if (string.IsNullOrEmpty(groupJid) || string.IsNullOrEmpty(inviteLink))
                    {
                        Console.WriteLine($"[Hangfire Group Lifecycle] Invalid response from gateway for appointment {appointment.Id}: {responseBody}");
                        continue;
                    }

                    appointment.WhatsAppGroupJid = groupJid;
                    appointment.WhatsAppGroupInviteLink = inviteLink;
                    dbContext.Entry(appointment).State = EntityState.Modified;
                    await dbContext.SaveChangesAsync();

                    Console.WriteLine($"[Hangfire Group Lifecycle] Successfully created group: {groupJid}, link: {inviteLink}");

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

                        var reminderFollowUp = new CRM.Domain.FollowUp
                        {
                            ProjectId = appointment.ProjectId,
                            CustomerId = booking.CustomerId,
                            DueDate = DateTime.UtcNow,
                            Status = "Pending",
                            Type = "AppointmentReminder",
                            AppointmentTime = appointment.DateTime,
                            Notes = reminderNotes,
                            Tone = "Default"
                        };
                        dbContext.FollowUps.Add(reminderFollowUp);

                        // Schedule Post-Session 2-day FollowUp
                        var postSessionFollowUp = new CRM.Domain.FollowUp
                        {
                            ProjectId = appointment.ProjectId,
                            CustomerId = booking.CustomerId,
                            DueDate = appointment.DateTime.AddDays(2),
                            Status = "Pending",
                            Type = "Nurturing",
                            AppointmentTime = appointment.DateTime,
                            Notes = "طمننا يا فندم، هل حضرت السيشن واشتركت معانا؟",
                            Tone = "Default"
                        };
                        dbContext.FollowUps.Add(postSessionFollowUp);
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
