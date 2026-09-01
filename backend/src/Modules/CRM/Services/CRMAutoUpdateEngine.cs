using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.CRM.Services
{
    public interface ICRMAutoUpdateEngine
    {
        Task ProcessSuggestionAsync(CRMUpdateSuggestedEvent @event);
    }

    public class CRMAutoUpdateEngine : ICRMAutoUpdateEngine
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly AutomationFollowUpService _automationFollowUps;

        public CRMAutoUpdateEngine(
            AppDbContext context,
            IHubContext<NotificationHub> hubContext,
            AutomationFollowUpService automationFollowUps)
        {
            _context = context;
            _hubContext = hubContext;
            _automationFollowUps = automationFollowUps;
        }

        public async Task ProcessSuggestionAsync(CRMUpdateSuggestedEvent @event)
        {
            Console.WriteLine($"[CRMAutoUpdateEngine] Processing suggestion for Customer: {@event.CustomerId}, Confidence: {@event.Confidence}");

            var customer = await _context.Customers.FindAsync(@event.CustomerId);
            if (customer == null)
            {
                Console.WriteLine($"[CRMAutoUpdateEngine] Customer not found: {@event.CustomerId}");
                return;
            }

            // Always update customer label on every message
            customer.Label = !string.IsNullOrEmpty(@event.Label) ? @event.Label : "استفسار عام";

            // Update AI Insights if present
            if (@event.AIInsights != null && @event.AIInsights.Length > 0)
            {
                customer.AIInsights = string.Join("\n", @event.AIInsights);
            }

            bool highConfidence = @event.Confidence >= 0.8;
            string status = highConfidence ? "Applied" : "PendingApproval";

            // Process City
            if (!string.IsNullOrEmpty(@event.City))
            {
                var proposal = new CRMUpdateProposal
                {
                    CustomerId = @event.CustomerId,
                    ProjectId = @event.ProjectId,
                    FieldName = "City",
                    SuggestedValue = @event.City,
                    ConfidenceScore = @event.Confidence,
                    Status = status
                };
                _context.CRMUpdateProposals.Add(proposal);

                if (highConfidence)
                {
                    customer.City = @event.City;
                }
            }

            // Process Budget (Disabled - Deals/Profits removed)
            /*
            if (@event.Budget.HasValue)
            {
                var proposal = new CRMUpdateProposal
                {
                    CustomerId = @event.CustomerId,
                    ProjectId = @event.ProjectId,
                    FieldName = "Budget",
                    SuggestedValue = @event.Budget.Value.ToString(),
                    ConfidenceScore = @event.Confidence,
                    Status = status
                };
                _context.CRMUpdateProposals.Add(proposal);

                if (highConfidence)
                {
                    customer.Budget = @event.Budget.Value;

                    var activeDeal = await _context.Deals
                        .FirstOrDefaultAsync(d => d.CustomerId == @event.CustomerId && d.Status == DealStatus.Open);
                    if (activeDeal != null)
                    {
                        activeDeal.Amount = @event.Budget.Value;
                        _context.Entry(activeDeal).State = EntityState.Modified;
                    }
                }
            }
            */

            // Process Interests
            if (@event.Interests != null && @event.Interests.Length > 0)
            {
                var interestsJson = JsonSerializer.Serialize(@event.Interests);
                var proposal = new CRMUpdateProposal
                {
                    CustomerId = @event.CustomerId,
                    ProjectId = @event.ProjectId,
                    FieldName = "Interests",
                    SuggestedValue = interestsJson,
                    ConfidenceScore = @event.Confidence,
                    Status = status
                };
                _context.CRMUpdateProposals.Add(proposal);

                if (highConfidence)
                {
                    var merged = customer.Interests.ToList();
                    foreach (var interest in @event.Interests)
                    {
                        if (!merged.Contains(interest, StringComparer.OrdinalIgnoreCase))
                        {
                            merged.Add(interest);
                        }
                    }
                    customer.Interests = merged.ToArray();
                }
            }

            // Lead scoring and conversation status updates based on Intent & Sentiment
            if (!string.IsNullOrEmpty(@event.Intent))
            {
                if (@event.Intent.Equals("purchase", StringComparison.OrdinalIgnoreCase))
                {
                    customer.LeadScore = Math.Min(100, customer.LeadScore + 20);
                }
                else if (@event.Intent.Equals("complaint", StringComparison.OrdinalIgnoreCase))
                {
                    customer.LeadScore = Math.Max(customer.LeadScore - 5, 0);
                }
            }

            // Variable to hold conversationId for SignalR payload if needed
            Guid activeConversationId = Guid.Empty;

            if (!string.IsNullOrEmpty(@event.Sentiment))
            {
                if (@event.Sentiment.Equals("angry", StringComparison.OrdinalIgnoreCase) || 
                    @event.Sentiment.Equals("negative", StringComparison.OrdinalIgnoreCase))
                {
                    customer.LeadScore = Math.Max(customer.LeadScore - 10, 0);

                    // Flag active conversation for immediate human attention
                    var activeConversation = @event.ConversationId.HasValue
                        ? await _context.Conversations.FirstOrDefaultAsync(c =>
                            c.Id == @event.ConversationId.Value
                            && c.CustomerId == @event.CustomerId
                            && c.Status == "Open")
                        : await _context.Conversations.FirstOrDefaultAsync(c =>
                            c.CustomerId == @event.CustomerId && c.Status == "Open");
                    
                    if (activeConversation != null)
                    {
                        activeConversation.Status = "Pending";
                        activeConversationId = activeConversation.Id;
                    }

                    // Create NotificationAlert in DB
                    var alert = new NotificationAlert
                    {
                        ProjectId = @event.ProjectId,
                        UserId = Guid.Empty, // General project alert
                        Type = "Complaint",
                        Message = $"Negative sentiment detected from customer: {customer.Name ?? customer.PhoneNumber}",
                        IsRead = false
                    };
                    _context.NotificationAlerts.Add(alert);

                    // Save changes to generate ID and CreatedAt timestamps
                    await _context.SaveChangesAsync();

                    // Push via SignalR
                    await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveNotification", new
                    {
                        id = alert.Id,
                        type = "Complaint",
                        message = alert.Message,
                        createdAt = alert.CreatedAt.ToString("o"),
                        payload = new
                        {
                            customerId = @event.CustomerId,
                            conversationId = activeConversationId,
                            severity = "High"
                        }
                    });
                }
            }

            // Check if customer became VIP (lead score >= 80)
            if (customer.LeadScore >= 80)
            {
                var vipAlert = new NotificationAlert
                {
                    ProjectId = @event.ProjectId,
                    UserId = Guid.Empty,
                    Type = "VIP",
                    Message = $"VIP Customer activity detected: {customer.Name ?? customer.PhoneNumber}",
                    IsRead = false
                };
                _context.NotificationAlerts.Add(vipAlert);

                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("ReceiveNotification", new
                {
                    id = vipAlert.Id,
                    type = "VIP",
                    message = vipAlert.Message,
                    createdAt = vipAlert.CreatedAt.ToString("o"),
                    payload = new
                    {
                        customerId = @event.CustomerId,
                        conversationId = activeConversationId,
                        severity = "Medium"
                    }
                });
            }

            // 3. Process Suggested Pipeline Stage (Disabled - Deals/Profits removed)
            /*
            if (!string.IsNullOrEmpty(@event.PipelineStage))
            {
                var stage = await _context.PipelineStages
                    .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId && s.Name.ToLower() == @event.PipelineStage.ToLower());

                if (stage == null)
                {
                    var orders = await _context.PipelineStages
                        .Where(s => s.ProjectId == @event.ProjectId)
                        .Select(s => s.Order)
                        .ToListAsync();
                    int maxOrder = orders.Any() ? orders.Max() : -1;

                    stage = new PipelineStage
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = @event.ProjectId,
                        Name = @event.PipelineStage,
                        Order = maxOrder + 1
                    };
                    _context.PipelineStages.Add(stage);
                    await _context.SaveChangesAsync();
                }

                var activeDeal = await _context.Deals
                    .FirstOrDefaultAsync(d => d.CustomerId == @event.CustomerId && d.Status == DealStatus.Open);

                if (activeDeal != null)
                {
                    activeDeal.PipelineStageId = stage.Id;
                    if (stage.Name.Equals("Won", StringComparison.OrdinalIgnoreCase))
                    {
                        activeDeal.Status = DealStatus.Won;
                        activeDeal.ClosedAt = DateTime.UtcNow;
                    }
                    else if (stage.Name.Equals("Lost", StringComparison.OrdinalIgnoreCase))
                    {
                        activeDeal.Status = DealStatus.Lost;
                        activeDeal.ClosedAt = DateTime.UtcNow;
                    }
                    _context.Entry(activeDeal).State = EntityState.Modified;
                }
                else
                {
                    var dealStatus = DealStatus.Open;
                    DateTime? closedAt = null;
                    if (stage.Name.Equals("Won", StringComparison.OrdinalIgnoreCase))
                    {
                        dealStatus = DealStatus.Won;
                        closedAt = DateTime.UtcNow;
                    }
                    else if (stage.Name.Equals("Lost", StringComparison.OrdinalIgnoreCase))
                    {
                        dealStatus = DealStatus.Lost;
                        closedAt = DateTime.UtcNow;
                    }

                    var deal = new Deal
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = @event.ProjectId,
                        CustomerId = @event.CustomerId,
                        Title = $"{customer.Name ?? customer.PhoneNumber}'s Deal",
                        Amount = customer.Budget ?? 0,
                        PipelineStageId = stage.Id,
                        Status = dealStatus,
                        ClosedAt = closedAt
                    };
                    _context.Deals.Add(deal);
                }
            }

            // Get resolved stage name to return
            string resolvedStageName = "New";
            var activeOrLastDeal = await _context.Deals
                .Where(d => d.CustomerId == customer.Id)
                .OrderByDescending(d => d.ClosedAt ?? d.CreatedAt)
                .FirstOrDefaultAsync();
            if (activeOrLastDeal != null)
            {
                var currentStage = await _context.PipelineStages.FindAsync(activeOrLastDeal.PipelineStageId);
                if (currentStage != null)
                {
                    resolvedStageName = currentStage.Name;
                }
            }
            */
            string resolvedStageName = "New";

            // Enforce that complaints or angry/negative customers never get automated follow-ups
            bool isComplaintOrNegative = 
                (!string.IsNullOrEmpty(@event.Sentiment) && 
                 (@event.Sentiment.Equals("angry", StringComparison.OrdinalIgnoreCase) || 
                  @event.Sentiment.Equals("negative", StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrEmpty(@event.Intent) && 
                 @event.Intent.Equals("complaint", StringComparison.OrdinalIgnoreCase));

            if (isComplaintOrNegative)
            {
                Console.WriteLine($"[CRMAutoUpdateEngine] Overriding FollowUpNeeded to false due to Complaint/Negative sentiment.");
                @event.FollowUpNeeded = false;
            }

            if (customer.IsBlacklisted)
            {
                Console.WriteLine($"[CRMAutoUpdateEngine] Overriding FollowUpNeeded to false because customer {@event.CustomerId} is blacklisted.");
                @event.FollowUpNeeded = false;
            }

            // Process Suggested Follow-up. Webhook and CRM events share this stable
            // slot so concurrent/live/recovery workers cannot create two reminders.
            var activeAutomationSlotKey = AutomationSlotKey(@event);

            if (@event.FollowUpNeeded)
            {
                Console.WriteLine($"[CRMAutoUpdateEngine] Processing suggested follow-up for Customer: {@event.CustomerId}. Type: {@event.FollowUpType}");
                try
                {
                    DateTime? appTime = null;
                    if (!string.IsNullOrEmpty(@event.FollowUpAppointmentTime) && DateTime.TryParse(@event.FollowUpAppointmentTime, out var parsedAppTime))
                    {
                        appTime = DateTime.SpecifyKind(parsedAppTime, DateTimeKind.Utc);
                    }

                    DateTime dueDate = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(@event.FollowUpDueDate) && DateTime.TryParse(@event.FollowUpDueDate, out var parsedDueDate))
                    {
                        dueDate = DateTime.SpecifyKind(parsedDueDate, DateTimeKind.Utc);
                    }

                    string followUpType = !string.IsNullOrEmpty(@event.FollowUpType) ? @event.FollowUpType : "Nurturing";

                    var followUp = await _automationFollowUps
                        .UpsertPendingAutomationFollowUpAsync(
                            new PendingAutomationFollowUpRequest(
                                @event.ProjectId,
                                @event.CustomerId,
                                activeAutomationSlotKey,
                                dueDate,
                                @event.FollowUpNotes ?? string.Empty,
                                followUpType,
                                @event.ConversationId,
                                @event.WhatsAppAccountId,
                                @event.Channel,
                                appTime));
                    Console.WriteLine($"[CRMAutoUpdateEngine] Upserted pending follow-up {followUp.Id} for customer {@event.CustomerId}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRMAutoUpdateEngine] Error processing suggested follow-up: {ex.Message}");
                }
            }
            else
            {
                var pendingFollowUps = await _context.FollowUps
                    .Where(f => f.ProjectId == @event.ProjectId
                        && f.CustomerId == @event.CustomerId
                        && f.Status == "Pending"
                        && (f.ActiveAutomationSlotKey == activeAutomationSlotKey
                            || (f.ActiveAutomationSlotKey == null
                                && (@event.ConversationId.HasValue
                                    ? f.ConversationId == @event.ConversationId
                                    : !f.ConversationId.HasValue
                                        && f.Channel == @event.Channel
                                        && (@event.Channel != "WhatsApp"
                                            || (f.WhatsAppAccountId ?? f.ProjectId)
                                                == (@event.WhatsAppAccountId ?? @event.ProjectId))))))
                    .ToListAsync();
                if (pendingFollowUps.Any())
                {
                    foreach (var pendingFollowUp in pendingFollowUps)
                        pendingFollowUp.Status = "Bypassed";
                    Console.WriteLine($"[CRMAutoUpdateEngine] Bypassed {pendingFollowUps.Count} pending follow-ups because AI suggested no follow-up is needed or sentiment was negative.");
                }
            }

            // A Gateway conversation becomes a qualified advertising lead only when the
            // existing sales classifier sees explicit purchase intent with high confidence.
            // Inquiry/greeting/support messages remain ordinary leads and never inflate CPA.
            if (@event.Confidence >= 0.85
                && string.Equals(@event.Intent, "purchase", StringComparison.OrdinalIgnoreCase))
            {
                var salesConversationId = @event.ConversationId
                    ?? await _context.Conversations.IgnoreQueryFilters()
                        .Where(item => item.ProjectId == @event.ProjectId && item.CustomerId == @event.CustomerId)
                        .OrderByDescending(item => item.LastMessageTimestamp)
                        .Select(item => (Guid?)item.Id)
                        .FirstOrDefaultAsync();
                if (salesConversationId is not null)
                {
                    IntegrationOutbox.Enqueue(_context, new AdvertisingQualifiedMessageChanged
                    {
                        ProjectId = @event.ProjectId, ConversationId = salesConversationId.Value,
                        CustomerId = @event.CustomerId, Classification = "PurchaseIntent",
                        Confidence = (decimal)Math.Clamp(@event.Confidence, 0d, 1d),
                        ClassifierVersion = "gemini-sales-intent-v1", ClassifiedAtUtc = DateTime.UtcNow,
                        SourceAggregateType = nameof(Customer), SourceAggregateId = @event.CustomerId,
                        SourceVersion = DateTime.UtcNow.Ticks
                    });
                }
            }

            // Save updates and the qualification event atomically.
            await _context.SaveChangesAsync();
            Console.WriteLine($"[CRMAutoUpdateEngine] CRM updates saved. High Confidence: {highConfidence}");

            // Broadcast customer update via SignalR
            try
            {
                await _hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("CustomerUpdated", new
                {
                    id = customer.Id,
                    projectId = customer.ProjectId,
                    phoneNumber = customer.PhoneNumber,
                    name = customer.Name,
                    city = customer.City,
                    leadScore = customer.LeadScore,
                    tags = customer.Tags,
                    notes = customer.Notes,
                    budget = customer.Budget,
                    interests = customer.Interests,
                    label = customer.Label,
                    pipelineStage = resolvedStageName
                });
                Console.WriteLine($"[CRMAutoUpdateEngine] Broadcasted CustomerUpdated SignalR event for customer {customer.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRMAutoUpdateEngine] Failed to broadcast CustomerUpdated event: {ex.Message}");
            }
        }

        private static string AutomationSlotKey(CRMUpdateSuggestedEvent @event)
        {
            var channel = string.IsNullOrWhiteSpace(@event.Channel)
                ? "WhatsApp"
                : @event.Channel.Trim();
            if (string.Equals(channel, "WhatsApp", StringComparison.OrdinalIgnoreCase)
                && @event.ConversationId.HasValue)
            {
                var accountId = @event.WhatsAppAccountId ?? @event.ProjectId;
                return $"whatsapp-ai-nurture:{accountId:N}:{@event.ConversationId.Value:N}";
            }

            var sourceId = @event.ConversationId ?? @event.CustomerId;
            return $"{channel.ToLowerInvariant()}-ai-nurture:{sourceId:N}";
        }
    }
}
