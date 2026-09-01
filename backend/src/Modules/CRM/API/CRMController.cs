using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Shared.Infrastructure;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

using Modules.Customers.Services;
using Modules.WhatsApp.Services;

namespace Modules.CRM.API
{
    [ApiController]
    [Authorize]
    [Route("api")]
    public class CRMController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEventBus _eventBus;
        private readonly ICustomerMemoryService _customerMemoryService;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ITenantContext _tenantContext;
        private readonly IProjectSecretVault _secretVault;
        private readonly IProjectAuthorizationService _authorization;

        public CRMController(
            AppDbContext context, 
            IEventBus eventBus, 
            ICustomerMemoryService customerMemoryService, 
            IConfiguration configuration, 
            IHubContext<NotificationHub> hubContext,
            ITenantContext tenantContext,
            IProjectSecretVault secretVault,
            IProjectAuthorizationService authorization)
        {
            _context = context;
            _eventBus = eventBus;
            _customerMemoryService = customerMemoryService;
            _configuration = configuration;
            _hubContext = hubContext;
            _tenantContext = tenantContext;
            _secretVault = secretVault;
            _authorization = authorization;
        }

        [HttpGet("projects/{projectId}/customers")]
        public async Task<IActionResult> GetCustomers(Guid projectId)
        {
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var customers = await _context.Customers
                .Where(c => c.ProjectId == projectId)
                .ToListAsync();

            // Find all deals for this project to map pipeline stages
            var allDeals = await _context.Deals
                .Where(d => d.ProjectId == projectId)
                .OrderByDescending(d => d.ClosedAt ?? d.CreatedAt)
                .ToListAsync();

            var stages = await _context.PipelineStages
                .Where(s => s.ProjectId == projectId)
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            var customerStages = allDeals
                .GroupBy(d => d.CustomerId)
                .ToDictionary(g => g.Key, g => 
                {
                    var stageId = g.First().PipelineStageId;
                    return stages.TryGetValue(stageId, out var name) ? name : "New";
                });

            var result = customers.Select(c => new
            {
                c.Id,
                c.ProjectId,
                c.PhoneNumber,
                c.Name,
                c.City,
                c.LeadScore,
                c.Tags,
                c.Notes,
                c.Budget,
                c.Interests,
                c.Label,
                c.IsBlacklisted,
                pipelineStage = customerStages.TryGetValue(c.Id, out var stage) ? stage : "New",
                c.PurchaseProbability,
                c.AIInsights,
                c.AutomationRules
            });

            return Ok(result);
        }

        [HttpGet("customers/{id}")]
        public async Task<IActionResult> GetCustomer(Guid id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();
            if (!_authorization.CanRead(User, customer.ProjectId)) return Forbid();

            var lastDeal = await _context.Deals
                .Where(d => d.CustomerId == id)
                .OrderByDescending(d => d.ClosedAt ?? d.CreatedAt)
                .FirstOrDefaultAsync();
            
            string stageName = "New";
            if (lastDeal != null)
            {
                var stage = await _context.PipelineStages.FindAsync(lastDeal.PipelineStageId);
                if (stage != null)
                {
                    stageName = stage.Name;
                }
            }

            return Ok(new
            {
                customer.Id,
                customer.ProjectId,
                customer.PhoneNumber,
                customer.Name,
                customer.City,
                customer.LeadScore,
                customer.Tags,
                customer.Notes,
                customer.Budget,
                customer.Interests,
                customer.Label,
                customer.IsBlacklisted,
                pipelineStage = stageName,
                customer.PurchaseProbability,
                customer.AIInsights,
                customer.AutomationRules
            });
        }

        [HttpPut("customers/{id}")]
        public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequest request)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();
            if (!_authorization.CanRead(User, customer.ProjectId)) return Forbid();

            var oldTags = customer.Tags ?? Array.Empty<string>();
            var newTags = request.Tags ?? customer.Tags ?? Array.Empty<string>();

            customer.Name = request.Name ?? customer.Name;
            customer.City = request.City ?? customer.City;
            customer.LeadScore = Math.Min(100, Math.Max(0, request.LeadScore ?? customer.LeadScore));
            customer.Tags = request.Tags ?? customer.Tags;
            customer.Notes = request.Notes ?? customer.Notes;
            customer.Label = request.Label ?? customer.Label;
            if (request.IsBudgetSet)
            {
                customer.Budget = request.Budget;
            }
            if (request.IsBlacklisted.HasValue)
            {
                customer.IsBlacklisted = request.IsBlacklisted.Value;
            }
            if (request.PurchaseProbability.HasValue)
            {
                customer.PurchaseProbability = request.PurchaseProbability.Value;
            }
            if (request.AIInsights != null)
            {
                customer.AIInsights = request.AIInsights;
            }
            if (request.AutomationRules != null)
            {
                customer.AutomationRules = request.AutomationRules;
            }

            // Handle pipeline stage update
            string resolvedStageName = "New";
            if (!string.IsNullOrEmpty(request.PipelineStage))
            {
                var stage = await _context.PipelineStages
                    .FirstOrDefaultAsync(s => s.ProjectId == customer.ProjectId && s.Name.ToLower() == request.PipelineStage.ToLower());

                if (stage == null)
                {
                    var orders = await _context.PipelineStages
                        .Where(s => s.ProjectId == customer.ProjectId)
                        .Select(s => s.Order)
                        .ToListAsync();
                    int maxOrder = orders.Any() ? orders.Max() : -1;

                    stage = new PipelineStage
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = customer.ProjectId,
                        Name = request.PipelineStage,
                        Order = maxOrder + 1
                    };
                    _context.PipelineStages.Add(stage);
                    await _context.SaveChangesAsync();
                }

                resolvedStageName = stage.Name;

                var activeDeal = await _context.Deals
                    .FirstOrDefaultAsync(d => d.CustomerId == id && d.Status == DealStatus.Open);

                if (activeDeal != null)
                {
                    activeDeal.PipelineStageId = stage.Id;
                    if (request.IsBudgetSet)
                    {
                        activeDeal.Amount = request.Budget ?? 0;
                    }
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
                    var status = DealStatus.Open;
                    DateTime? closedAt = null;
                    if (stage.Name.Equals("Won", StringComparison.OrdinalIgnoreCase))
                    {
                        status = DealStatus.Won;
                        closedAt = DateTime.UtcNow;
                    }
                    else if (stage.Name.Equals("Lost", StringComparison.OrdinalIgnoreCase))
                    {
                        status = DealStatus.Lost;
                        closedAt = DateTime.UtcNow;
                    }

                    var deal = new Deal
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = customer.ProjectId,
                        CustomerId = customer.Id,
                        Title = $"{customer.Name}'s Deal",
                        Amount = customer.Budget ?? 0,
                        PipelineStageId = stage.Id,
                        Status = status,
                        ClosedAt = closedAt
                    };
                    _context.Deals.Add(deal);
                }
            }
            else
            {
                // Resolve existing stage name
                var activeDeal = await _context.Deals
                    .FirstOrDefaultAsync(d => d.CustomerId == id && d.Status == DealStatus.Open);
                if (activeDeal != null)
                {
                    var stage = await _context.PipelineStages.FindAsync(activeDeal.PipelineStageId);
                    if (stage != null)
                    {
                        resolvedStageName = stage.Name;
                    }

                    if (request.IsBudgetSet)
                    {
                        activeDeal.Amount = request.Budget ?? 0;
                        _context.Entry(activeDeal).State = EntityState.Modified;
                    }
                }
            }

            foreach (var changedDeal in _context.ChangeTracker.Entries<Deal>()
                         .Where(x => x.State is EntityState.Added or EntityState.Modified)
                         .Select(x => x.Entity)
                         .Where(x => x.Status is DealStatus.Won or DealStatus.Lost))
            {
                IntegrationOutbox.Enqueue(_context, new AdvertisingDealOutcomeChanged
                {
                    ProjectId = changedDeal.ProjectId, DealId = changedDeal.Id, CustomerId = changedDeal.CustomerId,
                    Outcome = changedDeal.Status == DealStatus.Won ? "Won" : "Lost", Value = changedDeal.Amount, Currency = "EGP",
                    OutcomeOccurredAtUtc = DateTime.UtcNow, SourceAggregateType = nameof(Deal), SourceAggregateId = changedDeal.Id, SourceVersion = 1
                });
            }
            if (!string.IsNullOrWhiteSpace(request.SalesClassification))
            {
                var classification = request.SalesClassification.Trim();
                var allowedClassifications = new[] { "Spam", "Support", "Unqualified", "Qualified", "BookingIntent", "PurchaseIntent", "ConfirmedPayment" };
                if (!allowedClassifications.Contains(classification, StringComparer.OrdinalIgnoreCase))
                    return UnprocessableEntity(new { code = "CRM_SALES_CLASSIFICATION_INVALID" });
                var conversationId = await _context.Conversations.IgnoreQueryFilters()
                    .Where(item => item.ProjectId == customer.ProjectId && item.CustomerId == customer.Id)
                    .OrderByDescending(item => item.LastMessageTimestamp).Select(item => (Guid?)item.Id).FirstOrDefaultAsync();
                if (conversationId is not null)
                    IntegrationOutbox.Enqueue(_context, new AdvertisingQualifiedMessageChanged
                    {
                        ProjectId = customer.ProjectId, ConversationId = conversationId.Value, CustomerId = customer.Id,
                        Classification = classification, Confidence = Math.Clamp(request.ClassificationConfidence ?? 1m, 0m, 1m),
                        ClassifierVersion = "crm-explicit-v1", ClassifiedAtUtc = DateTime.UtcNow,
                        SourceAggregateType = nameof(Customer), SourceAggregateId = customer.Id, SourceVersion = DateTime.UtcNow.Ticks
                    });
            }
            if (!string.IsNullOrWhiteSpace(request.AdvertisingConsentState))
            {
                var consentState = request.AdvertisingConsentState.Trim();
                var allowedConsent = new[] { "Granted", "Denied", "NotRequired", "Unknown" };
                if (!allowedConsent.Contains(consentState, StringComparer.OrdinalIgnoreCase))
                    return UnprocessableEntity(new { code = "CRM_ADVERTISING_CONSENT_INVALID" });
                if (consentState.Equals("NotRequired", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(request.AdvertisingLegalBasis))
                    return UnprocessableEntity(new { code = "CRM_ADVERTISING_LEGAL_BASIS_REQUIRED" });
                IntegrationOutbox.Enqueue(_context, new CustomerAdvertisingConsentChanged
                {
                    ProjectId = customer.ProjectId, CustomerId = customer.Id, ConsentState = consentState,
                    LegalBasis = request.AdvertisingLegalBasis?.Trim() ?? string.Empty, EffectiveAtUtc = DateTime.UtcNow,
                    SourceAggregateType = nameof(Customer), SourceAggregateId = customer.Id, SourceVersion = DateTime.UtcNow.Ticks
                });
            }
            await _context.SaveChangesAsync();

            // Find newly added tags
            var addedTags = newTags.Except(oldTags).ToList();
            foreach (var tag in addedTags)
            {
                await _eventBus.PublishAsync(new CustomerTagAddedEvent
                {
                    ProjectId = customer.ProjectId,
                    CustomerId = customer.Id,
                    Tag = tag
                });
            }

            return Ok(new
            {
                customer.Id,
                customer.ProjectId,
                customer.PhoneNumber,
                customer.Name,
                customer.City,
                customer.LeadScore,
                customer.Tags,
                customer.Notes,
                customer.Budget,
                customer.Interests,
                customer.Label,
                customer.IsBlacklisted,
                pipelineStage = resolvedStageName,
                customer.PurchaseProbability,
                customer.AIInsights,
                customer.AutomationRules
            });
        }

        [HttpPost("customers/{customerId}/follow-ups")]
        public async Task<IActionResult> CreateFollowUp(Guid customerId, [FromBody] CreateFollowUpRequest request)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound("Customer not found");
            if (!_authorization.CanRead(User, customer.ProjectId)) return Forbid();
            var whatsAppAccounts = HttpContext.RequestServices
                .GetRequiredService<Modules.WhatsApp.Services.WhatsAppAccountService>();
            Conversation? sourceConversation = null;
            Guid whatsAppAccountId;
            if (request.WhatsAppAccountId.HasValue)
            {
                var selectedAccount = await whatsAppAccounts.ResolveAsync(
                    customer.ProjectId,
                    request.WhatsAppAccountId.Value,
                    HttpContext.RequestAborted);
                if (selectedAccount is null)
                    return BadRequest(new { code = "WHATSAPP_ACCOUNT_NOT_IN_PROJECT" });
                whatsAppAccountId = selectedAccount.Id;
                sourceConversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .Where(conversation => conversation.ProjectId == customer.ProjectId
                        && conversation.CustomerId == customer.Id
                        && conversation.Channel == "WhatsApp"
                        && conversation.WhatsAppAccountId == whatsAppAccountId
                        && conversation.WhatsAppDestinationId == null
                        && conversation.Status != "Closed")
                    .OrderByDescending(conversation => conversation.LastMessageTimestamp)
                    .FirstOrDefaultAsync();
            }
            else
            {
                sourceConversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .Where(conversation => conversation.ProjectId == customer.ProjectId
                        && conversation.CustomerId == customer.Id
                        && conversation.Channel == "WhatsApp"
                        && conversation.WhatsAppAccountId != null
                        && conversation.WhatsAppDestinationId == null
                        && conversation.Status != "Closed")
                    .OrderByDescending(conversation => conversation.LastMessageTimestamp)
                    .FirstOrDefaultAsync();
                whatsAppAccountId = sourceConversation?.WhatsAppAccountId
                    ?? (await whatsAppAccounts.GetDefaultAsync(customer.ProjectId)).Id;
            }

            string resolvedType = string.IsNullOrEmpty(request.Type) ? "Nurturing" : request.Type;
            DateTime calculatedDueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc);
            DateTime? apptTime = null;

            if (resolvedType == "AppointmentReminder")
            {
                if (!request.AppointmentTime.HasValue)
                {
                    return BadRequest("AppointmentTime is required for AppointmentReminder type");
                }

                apptTime = DateTime.SpecifyKind(request.AppointmentTime.Value, DateTimeKind.Utc);
                calculatedDueDate = apptTime.Value.AddDays(-1);

                if (calculatedDueDate < DateTime.UtcNow)
                {
                    calculatedDueDate = DateTime.UtcNow;
                }
            }

            var followUp = new FollowUp
            {
                CustomerId = customerId,
                ProjectId = customer.ProjectId, // Inherit from customer
                ConversationId = sourceConversation?.Id,
                WhatsAppAccountId = whatsAppAccountId,
                Channel = "WhatsApp",
                DueDate = calculatedDueDate,
                Status = "Pending",
                Notes = request.Notes ?? string.Empty,
                Type = resolvedType,
                AppointmentTime = apptTime,
                Tone = request.Tone ?? "Default"
            };

            _context.FollowUps.Add(followUp);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFollowUp), new { id = followUp.Id }, followUp);
        }

        [HttpGet("follow-ups/{id}")]
        public async Task<IActionResult> GetFollowUp(Guid id)
        {
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();
            if (!_authorization.CanRead(User, followUp.ProjectId)) return Forbid();
            if (followUp.DependsOnFollowUpId.HasValue
                && !await _context.FollowUps.IgnoreQueryFilters()
                    .AnyAsync(candidate => candidate.ProjectId == followUp.ProjectId
                        && candidate.Id == followUp.DependsOnFollowUpId.Value
                        && candidate.Status == "Completed"))
                return Conflict(new { code = "FOLLOW_UP_PREDECESSOR_NOT_COMPLETED" });
            return Ok(followUp);
        }

        [HttpGet("projects/{projectId}/follow-ups")]
        public async Task<IActionResult> GetFollowUps(Guid projectId, [FromQuery] string status = null)
        {
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var query = _context.FollowUps.Where(followUp => followUp.ProjectId == projectId);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(f => f.Status == status);
            }

            var followUps = await query.ToListAsync();
            return Ok(followUps);
        }

        [HttpPost("follow-ups/{id}/complete")]
        public async Task<IActionResult> CompleteFollowUp(Guid id)
        {
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();
            if (!_authorization.CanRead(User, followUp.ProjectId)) return Forbid();

            followUp.Status = "Completed";
            await _context.SaveChangesAsync();
            return Ok(followUp);
        }

        [HttpDelete("follow-ups/{id}")]
        public async Task<IActionResult> DeleteFollowUp(Guid id)
        {
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();
            if (!_authorization.CanRead(User, followUp.ProjectId)) return Forbid();

            _context.FollowUps.Remove(followUp);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("follow-ups/{id}")]
        public async Task<IActionResult> UpdateFollowUp(Guid id, [FromBody] UpdateFollowUpRequest request)
        {
            var followUp = await _context.FollowUps
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Id == id);
            
            if (followUp == null) return NotFound();
            if (!_authorization.CanRead(User, followUp.ProjectId)) return Forbid();

            if (!string.IsNullOrEmpty(request.Type))
            {
                followUp.Type = request.Type;
            }

            if (request.Notes != null)
            {
                followUp.Notes = request.Notes;
            }

            if (followUp.Type == "AppointmentReminder")
            {
                if (request.AppointmentTime.HasValue)
                {
                    followUp.AppointmentTime = DateTime.SpecifyKind(request.AppointmentTime.Value, DateTimeKind.Utc);
                    var calculatedDueDate = followUp.AppointmentTime.Value.AddDays(-1);
                    if (calculatedDueDate < DateTime.UtcNow)
                    {
                        calculatedDueDate = DateTime.UtcNow;
                    }
                    followUp.DueDate = calculatedDueDate;
                }
                else if (followUp.AppointmentTime.HasValue)
                {
                    var calculatedDueDate = followUp.AppointmentTime.Value.AddDays(-1);
                    if (calculatedDueDate < DateTime.UtcNow)
                    {
                        calculatedDueDate = DateTime.UtcNow;
                    }
                    followUp.DueDate = calculatedDueDate;
                }
            }
            else // Nurturing
            {
                if (request.DueDate.HasValue)
                {
                    followUp.DueDate = DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc);
                }
                followUp.AppointmentTime = null;
            }

            if (!string.IsNullOrEmpty(request.Status))
            {
                followUp.Status = request.Status;
            }

            if (request.Tone != null)
            {
                followUp.Tone = request.Tone;
            }

            _context.Entry(followUp).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            return Ok(followUp);
        }

        [HttpPost("follow-ups/{id}/send")]
        public async Task<IActionResult> SendFollowUp(Guid id)
        {
            var followUp = await _context.FollowUps
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.Id == id);
            
            if (followUp == null) return NotFound();
            if (!_authorization.CanRead(User, followUp.ProjectId)) return Forbid();

            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == followUp.CustomerId);

            if (customer == null) return BadRequest("Customer not found");
            if (followUp.Channel is not null && followUp.Channel != "WhatsApp")
                return BadRequest("This follow-up must be sent from its original conversation channel.");
            Conversation? targetConversation = null;
            if (followUp.ConversationId.HasValue)
            {
                targetConversation = await _context.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(conversation =>
                        conversation.Id == followUp.ConversationId.Value
                        && conversation.ProjectId == followUp.ProjectId
                        && conversation.CustomerId == followUp.CustomerId
                        && conversation.Channel == "WhatsApp"
                        && conversation.Status != "Closed");
                if (targetConversation is null)
                    return Conflict("The target WhatsApp conversation is no longer available.");
                if (targetConversation.WhatsAppDestinationId.HasValue)
                    return Conflict(new { code = "WHATSAPP_CLOUD_OUTBOUND_NOT_CONFIGURED" });
            }
            if (string.IsNullOrWhiteSpace(customer.PhoneNumber))
                return BadRequest("Customer has no WhatsApp phone number");

            var gatewaySessionClient = HttpContext.RequestServices
                .GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();
            var whatsAppAccounts = HttpContext.RequestServices
                .GetRequiredService<Modules.WhatsApp.Services.WhatsAppAccountService>();
            var whatsAppAccountId = followUp.WhatsAppAccountId
                ?? targetConversation?.WhatsAppAccountId
                ?? (await whatsAppAccounts.GetDefaultAsync(followUp.ProjectId)).Id;
            followUp.WhatsAppAccountId = whatsAppAccountId;
            var projectSettings = await _context.ProjectSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
            var gatewaySession = await gatewaySessionClient.GetAsync(
                followUp.ProjectId,
                whatsAppAccountId);
            if (!gatewaySession.Connected || !gatewaySession.ConnectedAt.HasValue)
            {
                DeferFollowUpToNextDailySlot(followUp, projectSettings?.Timezone);
                await _context.SaveChangesAsync();
                return StatusCode(503, new
                {
                    code = "WHATSAPP_DELIVERY_DEFERRED",
                    followUp.DueDate,
                    followUp.Status
                });
            }

            // Check if customer has any paid group booking
            var hasPaid = await _context.GroupAppointmentBookings
                .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid && b.ProjectId == followUp.ProjectId);

            if (hasPaid)
            {
                followUp.Status = "Cancelled";
                _context.Entry(followUp).State = EntityState.Modified;
                await _context.SaveChangesAsync();
                return BadRequest("Customer has already paid. Follow-up is cancelled.");
            }

            // Define the message content
            string? talkTipsTrialInstructions = null;
            if (projectSettings?.IsTalkTipsTrialGateEnabled == true)
            {
                var trialStatusClient = HttpContext.RequestServices.GetRequiredService<Modules.TalkTips.Services.TalkTipsTrialStatusClient>();
                if (!await trialStatusClient.HasTriedAsync(customer.PhoneNumber))
                {
                    talkTipsTrialInstructions = Modules.TalkTips.Services.TalkTipsTrialCtaInstructions.ForCustomerWhoHasNotTried();
                }
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
                        var aiMarketingBrain = HttpContext.RequestServices.GetService(typeof(Modules.AI.Services.IAIMarketingBrain)) as Modules.AI.Services.IAIMarketingBrain;
                        if (aiMarketingBrain != null)
                        {
                            string? apiKey = _secretVault.Unprotect(
                                followUp.ProjectId,
                                projectSettings?.GeminiApiKey);
                            string model = projectSettings?.ResolveGeminiModel(DateTime.UtcNow);

                            var hasAttended = await _context.GroupAppointmentBookings
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
                        Console.WriteLine($"[CRMController] Failed to rewrite follow-up notes via Gemini: {ex.Message}");
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

            var claimed = _context.Database.IsRelational()
                ? await _context.FollowUps
                    .IgnoreQueryFilters()
                    .Where(item => item.Id == id && item.Status == "Pending")
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.Status, "Processing")
                        .SetProperty(item => item.UpdatedAt, DateTime.UtcNow))
                : followUp.Status == "Pending" ? 1 : 0;
            if (claimed == 0) return Conflict("Follow-up is already being processed or has been handled.");
            followUp.Status = "Processing";
            if (!_context.Database.IsRelational()) await _context.SaveChangesAsync();

            // Call WhatsApp Gateway
            var gatewayUrl = _configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            using var httpClient = new HttpClient();

            var gatewayPayload = new
            {
                projectId = followUp.ProjectId,
                whatsappAccountId = whatsAppAccountId,
                to = customer.PhoneNumber,
                message = messageContent,
                idempotencyKey = followUp.Id.ToString("N"),
                expectedConnectedAt = gatewaySession.ConnectedAt
            };

            var jsonPayload = JsonSerializer.Serialize(gatewayPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string? providerMessageId = null;

            try
            {
                var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(httpClient, $"{gatewayUrl}/api/whatsapp/send", jsonPayload);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[CRMController] WhatsApp Gateway returned error {response.StatusCode} for follow-up {followUp.Id}: {responseBody}");
                    if ((int)response.StatusCode is 412 or 503)
                        DeferFollowUpToNextDailySlot(followUp, projectSettings?.Timezone);
                    else if ((int)response.StatusCode == 409 || (int)response.StatusCode >= 500)
                    {
                        followUp.Status = "DeliveryUnknown";
                        MarkConversationDeliveryUnknown(targetConversation, followUp.Id.ToString("N"));
                    }
                    else
                        followUp.Status = "Missed";
                    await _context.SaveChangesAsync();
                    return StatusCode((int)response.StatusCode, $"Failed to send WhatsApp message: {responseBody}");
                }
                providerMessageId = ProviderMessageId(responseBody);
                if (string.IsNullOrWhiteSpace(providerMessageId))
                {
                    followUp.Status = "DeliveryUnknown";
                    MarkConversationDeliveryUnknown(targetConversation, followUp.Id.ToString("N"));
                    await _context.SaveChangesAsync();
                    return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRMController] Exception while calling WhatsApp Gateway: {ex.Message}");
                followUp.Status = "DeliveryUnknown";
                MarkConversationDeliveryUnknown(targetConversation, followUp.Id.ToString("N"));
                await _context.SaveChangesAsync();
                return StatusCode(502, new { code = "WHATSAPP_DELIVERY_UNKNOWN" });
            }

            // Create/get the deterministic account-scoped conversation slot.
            var sentAt = DateTime.UtcNow;
            var whatsAppConversations = HttpContext.RequestServices
                .GetRequiredService<WhatsAppConversationService>();
            var conversation = targetConversation
                ?? await whatsAppConversations.ResolveOrCreateAsync(
                    followUp.ProjectId,
                    customer.Id,
                    whatsAppAccountId,
                    sentAt);
            if (string.Equals(
                    conversation.WhatsAppDeliveryUnknownKey,
                    followUp.Id.ToString("N"),
                    StringComparison.Ordinal))
            {
                conversation.WhatsAppDeliveryUnknownAt = null;
                conversation.WhatsAppDeliveryUnknownKey = null;
            }
            if (sentAt > conversation.LastMessageTimestamp)
                conversation.LastMessageTimestamp = sentAt;

            var providerRecordId = WhatsAppMessageIdentity.Outgoing(
                followUp.ProjectId,
                whatsAppAccountId,
                providerMessageId);
            var message = await _context.Messages.IgnoreQueryFilters()
                .FirstOrDefaultAsync(existing => existing.Id == providerRecordId);
            var createdMessage = message is null;
            if (message is null)
            {
                message = new Message
                {
                    Id = providerRecordId,
                    ConversationId = conversation.Id,
                    ExternalMessageId = providerMessageId,
                    Direction = "Outgoing",
                    Content = messageContent,
                    MessageType = "Text",
                    Timestamp = sentAt
                };
                _context.Messages.Add(message);
            }

            // Mark this specific follow-up as Completed
            followUp.Status = "Completed";
            _context.Entry(followUp).State = EntityState.Modified;

            // Also complete any other pending follow-ups for this customer
            if (followUp.Type != "DeferredReplyChunk")
            {
                var otherPending = await _context.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.ProjectId == followUp.ProjectId
                        && f.CustomerId == customer.Id
                        && f.Status == "Pending"
                        && f.Id != followUp.Id
                        && (f.ConversationId == conversation.Id
                            || (f.ConversationId == null && f.WhatsAppAccountId == whatsAppAccountId)))
                    .ToListAsync();

                foreach (var fu in otherPending)
                {
                    fu.Status = "Bypassed";
                    _context.Entry(fu).State = EntityState.Modified;
                }
            }

            // Schedule a new default follow-up 24 hours in the future only if AI auto-reply is enabled and customer is not blacklisted and whatsapp reminder automation is enabled
            var settings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
            
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
                catch {}
            }

            bool shouldScheduleFollowUp = followUp.Type != "DeferredReplyChunk"
                && settings != null
                && settings.AiAutoReplyEnabled
                && !customer.IsBlacklisted
                && whatsappReminderEnabled;

            if (shouldScheduleFollowUp)
            {
                await HttpContext.RequestServices
                    .GetRequiredService<AutomationFollowUpService>()
                    .UpsertPendingAutomationFollowUpAsync(
                        new PendingAutomationFollowUpRequest(
                            followUp.ProjectId,
                            customer.Id,
                            $"whatsapp-ai-nurture:{whatsAppAccountId:N}:{conversation.Id:N}",
                            DateTime.UtcNow.AddHours(24),
                            "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                            ConversationId: conversation.Id,
                            WhatsAppAccountId: whatsAppAccountId,
                            Channel: "WhatsApp"),
                        HttpContext.RequestAborted);
            }

            await _context.SaveChangesAsync();

            // Broadcast message via SignalR so the Chat Inbox updates in real-time
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

            if (createdMessage)
            {
                try
                {
                    await _hubContext.Clients.Group($"project_{followUp.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                }
                catch (Exception notificationError)
                {
                    Console.WriteLine($"[CRMController] Follow-up {followUp.Id} was persisted, but SignalR failed: {notificationError.Message}");
                }
            }

            return Ok(followUp);
        }

        [HttpPost("projects/{projectId}/follow-ups/re-evaluate-all")]
        public async Task<IActionResult> ReEvaluateAllFollowUps(Guid projectId)
        {
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            _tenantContext.SetProjectId(projectId);

            var pendingFollowUps = await _context.FollowUps
                .Where(f => f.ProjectId == projectId && f.Status == "Pending")
                .ToListAsync();

            if (!pendingFollowUps.Any())
            {
                return Ok(new { message = "No pending follow-ups found for this project.", count = 0 });
            }

            var gatewaySessionClient = HttpContext.RequestServices
                .GetRequiredService<Modules.Advertising.Services.WhatsAppGatewaySessionClient>();
            var whatsAppAccounts = HttpContext.RequestServices
                .GetRequiredService<Modules.WhatsApp.Services.WhatsAppAccountService>();

            var projectSettings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == projectId);
            string? apiKey = _secretVault.Unprotect(projectId, projectSettings?.GeminiApiKey);
            string model = projectSettings?.ResolveGeminiModel(DateTime.UtcNow);

            var geminiClient = HttpContext.RequestServices.GetService(typeof(Modules.AI.Services.IGeminiClient)) as Modules.AI.Services.IGeminiClient;
            if (geminiClient == null)
            {
                return BadRequest("AI Engine client not found.");
            }

            int updatedCount = 0;
            foreach (var followUp in pendingFollowUps)
            {
                try
                {
                    var customer = await _context.Customers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == followUp.CustomerId && c.ProjectId == projectId);

                    if (customer == null) continue;

                    var conversation = followUp.ConversationId.HasValue
                        ? await _context.Conversations.FirstOrDefaultAsync(c =>
                            c.Id == followUp.ConversationId.Value
                            && c.CustomerId == customer.Id
                            && c.ProjectId == projectId
                            && c.Status != "Closed")
                        : await _context.Conversations.FirstOrDefaultAsync(c =>
                            c.CustomerId == customer.Id
                            && c.ProjectId == projectId
                            && c.Status != "Closed"
                            && (followUp.Channel != "WhatsApp"
                                || c.WhatsAppAccountId == followUp.WhatsAppAccountId));

                    if (followUp.Channel != "Messenger")
                    {
                        var accountId = followUp.WhatsAppAccountId
                            ?? conversation?.WhatsAppAccountId
                            ?? (await whatsAppAccounts.GetDefaultAsync(projectId)).Id;
                        if (!(await gatewaySessionClient.GetAsync(projectId, accountId)).Connected)
                            continue;
                        followUp.WhatsAppAccountId = accountId;
                    }

                    // Skip/cancel if already paid
                    var hasPaid = await _context.GroupAppointmentBookings
                        .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid && b.ProjectId == projectId);

                    if (hasPaid)
                    {
                        followUp.Status = "Cancelled";
                        _context.Entry(followUp).State = EntityState.Modified;
                        updatedCount++;
                        continue;
                    }

                    // Fetch bookings
                    var bookings = await _context.GroupAppointmentBookings
                        .Include(b => b.GroupAppointment)
                        .Where(b => b.CustomerId == customer.Id && b.ProjectId == projectId)
                        .ToListAsync();

                    var hasAttended = bookings.Any(b => b.IsAttended);
                    var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone(projectSettings?.Timezone ?? "Africa/Cairo");
                    var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cairoZone);

                    var bookingsListStr = "";
                    if (bookings.Any())
                    {
                        var listItems = bookings.Select(b => {
                            var localSessionTime = TimeZoneInfo.ConvertTimeFromUtc(b.GroupAppointment.DateTime, cairoZone);
                            return $"- Group: \"{b.GroupAppointment.Name}\", Time: {localSessionTime:yyyy-MM-dd HH:mm}, Status: {(b.IsPaid ? "Paid" : "Not Paid")}, Attended: {(b.IsAttended ? "Yes" : "No")}";
                        });
                        bookingsListStr = string.Join("\n", listItems);
                    }
                    else
                    {
                        bookingsListStr = "No group bookings found.";
                    }

                    // Fetch chat history
                    string chatHistory = "No recent chat history found.";
                    if (conversation != null)
                    {
                        var messages = await _context.Messages
                            .Where(m => m.ConversationId == conversation.Id)
                            .OrderByDescending(m => m.Timestamp)
                            .Take(15)
                            .ToListAsync();
                        
                        if (messages.Any())
                        {
                            messages.Reverse();
                            chatHistory = string.Join("\n", messages.Select(m => $"{(m.Direction == "Incoming" ? "Customer" : "Agent/AI")}: {m.Content}"));
                        }
                    }

                    // Build prompt for Gemini to evaluate and return new notes & time
                    var prompt = $@"You are a high-performing CRM assistant.
You need to re-evaluate and adjust a scheduled follow-up for the customer ""{customer.Name ?? customer.PhoneNumber}"".

Current Local Time: {localNow:yyyy-MM-dd HH:mm}
Customer City: {customer.City ?? "Missing"}
Customer Lead Score: {customer.LeadScore}
Has Student Attended Group session? {hasAttended}

Customer's Active Group Bookings:
{bookingsListStr}

Current scheduled follow-up:
- Type: {followUp.Type}
- Target Date (Current): {followUp.DueDate}
- Current message/note to send: {followUp.Notes}

Here is the recent WhatsApp chat history between the customer and our AI/Agents:
{chatHistory}

Analyze the chat history and active bookings to understand:
1. Did the customer ask for a specific time or day to contact them? Or did they confirm a booking? Or did they hesitate?
2. Write a highly personalized, natural follow-up message (in polite Egyptian Arabic, following their tone preference: {followUp.Tone}). Make it fit the conversation status perfectly (e.g. if they already booked, remind them of their exact session time; if they didn't book, ask them if they need help booking or have questions).
3. Determine a reasonable next follow-up date and time.
   - If they booked a group, the follow-up should be scheduled exactly 24 hours BEFORE their booked group session (if any booked group exists). If that target time has already passed (or is in the next few hours), schedule it for 2 to 4 hours from now.
   - If they are hesitant, follow up in 1 to 2 days.
   - If the chat shows they asked to be contacted at a specific time, use that time!

You MUST respond strictly in the following JSON format:
{{
  ""notes"": ""the rewritten personalized Egyptian Arabic message to send to the student"",
  ""hoursFromNow"": 24
}}
Note: 'hoursFromNow' is the number of hours from the current local time ({localNow:yyyy-MM-dd HH:mm}) when this follow-up should be sent. Return an integer.

JSON:";

                    var reply = await geminiClient.GenerateReplyAsync(prompt, apiKey, model);
                    if (!string.IsNullOrEmpty(reply) && !reply.StartsWith("[Mock") && !reply.StartsWith("[AI_ERROR]"))
                    {
                        var json = reply.Trim();
                        if (json.StartsWith("```"))
                        {
                            var firstLineBreak = json.IndexOf('\n');
                            var lastBackticks = json.LastIndexOf("```");
                            if (firstLineBreak != -1 && lastBackticks != -1 && lastBackticks > firstLineBreak)
                            {
                                json = json.Substring(firstLineBreak + 1, lastBackticks - firstLineBreak - 1).Trim();
                            }
                        }

                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("notes", out var notesProp) &&
                            doc.RootElement.TryGetProperty("hoursFromNow", out var hoursProp))
                        {
                            followUp.Notes = notesProp.GetString() ?? followUp.Notes;
                            var hours = hoursProp.GetInt32();
                            followUp.DueDate = DateTime.UtcNow.AddHours(hours);
                            _context.Entry(followUp).State = EntityState.Modified;
                            updatedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRMController] Error during follow-up re-evaluation for customer {followUp.CustomerId}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Re-evaluated and updated {updatedCount} pending follow-ups.", count = updatedCount });
        }

        [HttpGet("projects/{projectId}/crm-proposals")]
        public async Task<IActionResult> GetProposals(Guid projectId, [FromQuery] string status = null)
        {
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var query = _context.CRMUpdateProposals.Where(proposal => proposal.ProjectId == projectId);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var proposals = await query.ToListAsync();
            return Ok(proposals);
        }

        [HttpGet("customers/{customerId}/memory")]
        public async Task<IActionResult> GetCustomerMemory(Guid customerId)
        {
            var projectId = await _context.Customers
                .IgnoreQueryFilters()
                .Where(customer => customer.Id == customerId)
                .Select(customer => (Guid?)customer.ProjectId)
                .FirstOrDefaultAsync();
            if (!projectId.HasValue) return NotFound("Customer not found");
            if (!_authorization.CanRead(User, projectId.Value)) return Forbid();
            var memory = await _context.CustomerMemories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.CustomerId == customerId);
            if (memory == null)
            {
                return Ok(new
                {
                    CustomerId = customerId,
                    LongTermSummary = string.Empty,
                    FactsJson = "[]",
                    TriggersJson = "[]",
                    ObjectionsJson = "[]"
                });
            }
            return Ok(memory);
        }

        [HttpPut("customers/{customerId}/memory")]
        public async Task<IActionResult> UpdateCustomerMemory(Guid customerId, [FromBody] UpdateCustomerMemoryRequest request)
        {
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == customerId);
            if (customer == null) return NotFound("Customer not found");
            if (!_authorization.CanRead(User, customer.ProjectId)) return Forbid();
            var memory = await _context.CustomerMemories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.CustomerId == customerId);
            
            if (memory == null)
            {
                memory = new Modules.Customers.Domain.CustomerMemory
                {
                    CustomerId = customerId,
                    ProjectId = customer.ProjectId,
                    LongTermSummary = request.LongTermSummary ?? string.Empty,
                    FactsJson = request.FactsJson ?? "[]",
                    TriggersJson = request.TriggersJson ?? "[]",
                    ObjectionsJson = request.ObjectionsJson ?? "[]",
                    LastUpdatedAt = DateTime.UtcNow
                };
                _context.CustomerMemories.Add(memory);
            }
            else
            {
                memory.LongTermSummary = request.LongTermSummary ?? memory.LongTermSummary;
                memory.FactsJson = request.FactsJson ?? memory.FactsJson;
                memory.TriggersJson = request.TriggersJson ?? memory.TriggersJson;
                memory.ObjectionsJson = request.ObjectionsJson ?? memory.ObjectionsJson;
                memory.LastUpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(memory);
        }

        [HttpPost("projects/{projectId}/customers/{customerId}/memory/generate")]
        public async Task<IActionResult> GenerateCustomerProfile(Guid projectId, Guid customerId)
        {
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var customerExists = await _context.Customers
                .IgnoreQueryFilters()
                .AnyAsync(customer => customer.Id == customerId && customer.ProjectId == projectId);
            if (!customerExists) return NotFound("Customer not found");
            try
            {
                var memory = await _customerMemoryService.GenerateCompleteProfileAsync(projectId, customerId);
                return Ok(memory);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("customers/{customerId}/tasks")]
        public async Task<IActionResult> GetCustomerTasks(Guid customerId)
        {
            var projectId = await _context.Customers
                .IgnoreQueryFilters()
                .Where(customer => customer.Id == customerId)
                .Select(customer => (Guid?)customer.ProjectId)
                .FirstOrDefaultAsync();
            if (!projectId.HasValue) return NotFound("Customer not found");
            if (!_authorization.CanRead(User, projectId.Value)) return Forbid();
            var tasks = await _context.CustomerTasks
                .IgnoreQueryFilters()
                .Where(t => t.CustomerId == customerId)
                .OrderBy(t => t.IsCompleted)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync();
            return Ok(tasks);
        }

        [HttpPost("customers/{customerId}/tasks")]
        public async Task<IActionResult> CreateCustomerTask(Guid customerId, [FromBody] CreateCustomerTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required");
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return NotFound("Customer not found");
            if (!_authorization.CanRead(User, customer.ProjectId)) return Forbid();

            var task = new Modules.CRM.Domain.CustomerTask
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                ProjectId = customer.ProjectId,
                Title = request.Title,
                IsCompleted = false,
                DueDate = request.DueDate.HasValue ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) : null
            };

            _context.CustomerTasks.Add(task);
            await _context.SaveChangesAsync();
            return Ok(task);
        }

        [HttpPut("customers/tasks/{taskId}")]
        public async Task<IActionResult> UpdateCustomerTask(Guid taskId, [FromBody] UpdateCustomerTaskRequest request)
        {
            var task = await _context.CustomerTasks.FindAsync(taskId);
            if (task == null) return NotFound("Task not found");
            if (!_authorization.CanRead(User, task.ProjectId)) return Forbid();

            if (request.Title != null)
            {
                task.Title = request.Title;
            }
            if (request.IsCompleted.HasValue)
            {
                task.IsCompleted = request.IsCompleted.Value;
            }
            if (request.IsDueDateSet)
            {
                task.DueDate = request.DueDate.HasValue ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) : null;
            }

            _context.Entry(task).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(task);
        }

        [HttpDelete("customers/tasks/{taskId}")]
        public async Task<IActionResult> DeleteCustomerTask(Guid taskId)
        {
            var task = await _context.CustomerTasks.FindAsync(taskId);
            if (task == null) return NotFound("Task not found");
            if (!_authorization.CanRead(User, task.ProjectId)) return Forbid();

            _context.CustomerTasks.Remove(task);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("projects/{projectId}/import-blacklist")]
        public async Task<IActionResult> ImportBlacklist(Guid projectId, [FromBody] List<string> phones)
        {
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            const string paidBlacklistGroupName = "المحظورين للدفع";

            if (phones == null || phones.Count == 0)
            {
                return BadRequest("No data provided.");
            }

            var normalizedPhones = phones
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => NormalizePhoneNumber(p))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            if (normalizedPhones.Count == 0)
            {
                return BadRequest("No valid phone numbers found.");
            }

            var existingCustomers = await _context.Customers
                .Where(c => c.ProjectId == projectId && normalizedPhones.Contains(c.PhoneNumber))
                .ToListAsync();

            var existingPhones = existingCustomers.Select(c => c.PhoneNumber).ToHashSet();

            foreach (var customer in existingCustomers)
            {
                customer.IsBlacklisted = true;
                customer.Label = paidBlacklistGroupName;
                customer.Tags = AddUniqueTag(customer.Tags, paidBlacklistGroupName);
                customer.Notes = AppendImportNote(customer.Notes);
                _context.Entry(customer).State = EntityState.Modified;
            }

            var newPhones = normalizedPhones.Where(p => !existingPhones.Contains(p)).ToList();
            var newCustomersToCreate = newPhones
                .Select(phone => new Customer
                {
                    ProjectId = projectId,
                    PhoneNumber = phone,
                    Name = $"طالب مدفوع ({phone})",
                    IsBlacklisted = true,
                    Label = paidBlacklistGroupName,
                    Tags = new[] { paidBlacklistGroupName },
                    City = string.Empty,
                    Notes = "تمت إضافته كطالب مدفوع ومحظور تلقائياً عبر رفع ملف إكسل."
                })
                .ToList();

            foreach (var newCustomer in newCustomersToCreate)
            {
                _context.Customers.Add(newCustomer);
            }

            var allImportedCustomerIds = existingCustomers
                .Select(c => c.Id)
                .Concat(newCustomersToCreate.Select(c => c.Id))
                .ToList();

            var bookingsToRemove = await _context.GroupAppointmentBookings
                .IgnoreQueryFilters()
                .Where(b =>
                    b.ProjectId == projectId &&
                    (normalizedPhones.Contains(b.CustomerPhone) || allImportedCustomerIds.Contains(b.CustomerId)))
                .ToListAsync();

            var pendingFollowUpsToCancel = await _context.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.ProjectId == projectId && allImportedCustomerIds.Contains(f.CustomerId) && f.Status == "Pending")
                .ToListAsync();

            foreach (var followUp in pendingFollowUpsToCancel)
            {
                followUp.Status = "Cancelled";
                _context.Entry(followUp).State = EntityState.Modified;
            }

            _context.GroupAppointmentBookings.RemoveRange(bookingsToRemove);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                matchedCount = existingCustomers.Count,
                newCount = newCustomersToCreate.Count,
                removedBookingsCount = bookingsToRemove.Count,
                cancelledFollowUpsCount = pendingFollowUpsToCancel.Count,
                blacklistGroupName = paidBlacklistGroupName,
                matchedPhones = existingCustomers.Select(c => c.PhoneNumber).ToList(),
                newPhones = newPhones
            });
        }

        private static string[] AddUniqueTag(string[]? tags, string tag)
        {
            return (tags ?? Array.Empty<string>())
                .Concat(new[] { tag })
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void DeferFollowUpToNextDailySlot(FollowUp followUp, string? timezoneId)
        {
            var nowUtc = DateTime.UtcNow;
            var timezone = Shared.Infrastructure.TimezoneHelper.GetTimeZone(
                string.IsNullOrWhiteSpace(timezoneId) ? "Africa/Cairo" : timezoneId);
            var nextDueDate = Modules.WhatsApp.Services.WhatsAppDailyDeliverySchedule
                .NextOccurrenceAfter(followUp.DueDate, nowUtc, timezone);
            if (followUp.Type == "AppointmentReminder"
                && followUp.AppointmentTime.HasValue
                && nextDueDate >= followUp.AppointmentTime.Value)
            {
                followUp.Status = "Cancelled";
            }
            else
            {
                followUp.DueDate = nextDueDate;
                followUp.Status = "Pending";
            }
            followUp.UpdatedAt = nowUtc;
        }

        private static void MarkConversationDeliveryUnknown(
            Conversation? conversation,
            string deliveryKey)
        {
            if (conversation is null) return;
            conversation.WhatsAppDeliveryUnknownAt = DateTime.UtcNow;
            conversation.WhatsAppDeliveryUnknownKey = deliveryKey;
        }

        private static string? ProviderMessageId(string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return document.RootElement.TryGetProperty("messageId", out var messageId)
                    && messageId.ValueKind == JsonValueKind.String
                    ? messageId.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string AppendImportNote(string? notes)
        {
            const string importNote = "تمت إضافته كطالب مدفوع ومحظور تلقائياً عبر رفع ملف إكسل.";
            if (string.IsNullOrWhiteSpace(notes))
            {
                return importNote;
            }
            if (notes.Contains(importNote, StringComparison.OrdinalIgnoreCase))
            {
                return notes;
            }
            return $"{notes}\n{importNote}";
        }

        private static string NormalizePhoneNumber(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

            // Remove all non-digits
            var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());

            // If it starts with 00, remove it
            if (digitsOnly.StartsWith("00"))
            {
                digitsOnly = digitsOnly.Substring(2);
            }

            // If it starts with 01 (Egyptian phone number), add 2 (Egypt country code)
            if (digitsOnly.StartsWith("01") && digitsOnly.Length == 11)
            {
                digitsOnly = "2" + digitsOnly;
            }

            // If it is just 11 digits starting with 1, add 20
            if (digitsOnly.StartsWith("1") && digitsOnly.Length == 11)
            {
                digitsOnly = "20" + digitsOnly;
            }

            return digitsOnly;
        }
    }

    public class CreateCustomerTaskRequest
    {
        public string Title { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class UpdateCustomerTaskRequest
    {
        public string? Title { get; set; }
        public bool? IsCompleted { get; set; }
        public DateTime? DueDate { get; set; }
        public bool IsDueDateSet { get; set; } = false;
    }

    public class UpdateCustomerMemoryRequest
    {
        public string? LongTermSummary { get; set; }
        public string? FactsJson { get; set; }
        public string? TriggersJson { get; set; }
        public string? ObjectionsJson { get; set; }
    }

    public class UpdateCustomerRequest
    {
        public string? Name { get; set; }
        public string? City { get; set; }
        public int? LeadScore { get; set; }
        public string[]? Tags { get; set; }
        public string? Notes { get; set; }
        public string? Label { get; set; }
        public bool? IsBlacklisted { get; set; }
        private decimal? _budget;
        public bool IsBudgetSet { get; private set; }

        public decimal? Budget
        {
            get => _budget;
            set
            {
                _budget = value;
                IsBudgetSet = true;
            }
        }

        public string? PipelineStage { get; set; }
        public int? PurchaseProbability { get; set; }
        public string? AIInsights { get; set; }
        public string? SalesClassification { get; set; }
        public decimal? ClassificationConfidence { get; set; }
        public string? AdvertisingConsentState { get; set; }
        public string? AdvertisingLegalBasis { get; set; }
        public string? AutomationRules { get; set; }
    }

    public class CreateFollowUpRequest
    {
        public DateTime DueDate { get; set; }
        public string Notes { get; set; }
        public string? Type { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public string? Tone { get; set; }
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class UpdateFollowUpRequest
    {
        public DateTime? DueDate { get; set; }
        public string? Notes { get; set; }
        public string? Type { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public string? Status { get; set; }
        public string? Tone { get; set; }
    }

}
