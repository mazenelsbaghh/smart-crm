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

            // Save updates
            await _context.SaveChangesAsync();
            Console.WriteLine($"[CRMAutoUpdateEngine] CRM updates saved. High Confidence: {highConfidence}");
        }
    }
}
