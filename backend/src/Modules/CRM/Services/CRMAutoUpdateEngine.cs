using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Events;
using Shared.Infrastructure;
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

        public CRMAutoUpdateEngine(AppDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
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

            // Process Budget
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
                    customer.LeadScore += 20;
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
                    var activeConversation = await _context.Conversations
                        .FirstOrDefaultAsync(c => c.CustomerId == @event.CustomerId && c.Status == "Open");
                    
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

            // 3. Process Suggested Pipeline Stage
            if (!string.IsNullOrEmpty(@event.PipelineStage))
            {
                var stage = await _context.PipelineStages
                    .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId && s.Name.ToLower() == @event.PipelineStage.ToLower());

                if (stage == null)
                {
                    int maxOrder = await _context.PipelineStages
                        .Where(s => s.ProjectId == @event.ProjectId)
                        .Select(s => s.Order)
                        .DefaultIfEmpty(-1)
                        .MaxAsync();

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

            // Save updates
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
    }
}
