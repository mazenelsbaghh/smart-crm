using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
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

using Modules.Customers.Services;

namespace Modules.CRM.API
{
    [ApiController]
    [Route("api")]
    public class CRMController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEventBus _eventBus;
        private readonly ICustomerMemoryService _customerMemoryService;
        private readonly IConfiguration _configuration;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ITenantContext _tenantContext;

        public CRMController(
            AppDbContext context, 
            IEventBus eventBus, 
            ICustomerMemoryService customerMemoryService, 
            IConfiguration configuration, 
            IHubContext<NotificationHub> hubContext,
            ITenantContext tenantContext)
        {
            _context = context;
            _eventBus = eventBus;
            _customerMemoryService = customerMemoryService;
            _configuration = configuration;
            _hubContext = hubContext;
            _tenantContext = tenantContext;
        }

        [HttpGet("projects/{projectId}/customers")]
        public async Task<IActionResult> GetCustomers(Guid projectId)
        {
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
            return Ok(followUp);
        }

        [HttpGet("projects/{projectId}/follow-ups")]
        public async Task<IActionResult> GetFollowUps(Guid projectId, [FromQuery] string status = null)
        {
            var query = _context.FollowUps.AsQueryable();

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

            followUp.Status = "Completed";
            await _context.SaveChangesAsync();
            return Ok(followUp);
        }

        [HttpDelete("follow-ups/{id}")]
        public async Task<IActionResult> DeleteFollowUp(Guid id)
        {
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();

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

            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == followUp.CustomerId);

            if (customer == null) return BadRequest("Customer not found");

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
                        var aiMarketingBrain = HttpContext.RequestServices.GetService(typeof(Modules.AI.Services.IAIMarketingBrain)) as Modules.AI.Services.IAIMarketingBrain;
                        if (aiMarketingBrain != null)
                        {
                            var projectSettings = await _context.ProjectSettings
                                .IgnoreQueryFilters()
                                .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
                            string apiKey = projectSettings?.GeminiApiKey;
                            if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("mock_"))
                            {
                                apiKey = null; // Use default system key
                            }
                            string model = projectSettings?.GeminiModel;

                            var hasAttended = await _context.GroupAppointmentBookings
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

            // Call WhatsApp Gateway
            var gatewayUrl = _configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            using var httpClient = new HttpClient();

            var gatewayPayload = new
            {
                projectId = followUp.ProjectId,
                to = customer.PhoneNumber,
                message = messageContent
            };

            var jsonPayload = JsonSerializer.Serialize(gatewayPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            try
            {
                var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(httpClient, $"{gatewayUrl}/api/whatsapp/send", jsonPayload);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[CRMController] WhatsApp Gateway returned error {response.StatusCode} for follow-up {followUp.Id}: {responseBody}");
                    return StatusCode((int)response.StatusCode, $"Failed to send WhatsApp message: {responseBody}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRMController] Exception while calling WhatsApp Gateway: {ex.Message}");
                return StatusCode(500, $"Internal error communicating with WhatsApp Gateway: {ex.Message}");
            }

            // Create/get active conversation
            var conversation = await _context.Conversations
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
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageTimestamp = DateTime.UtcNow;
                _context.Entry(conversation).State = EntityState.Modified;
            }

            // Add outgoing message
            var message = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = $"msg_fu_{Guid.NewGuid().ToString("N")}",
                Direction = "Outgoing",
                Content = messageContent,
                MessageType = "Text",
                Timestamp = DateTime.UtcNow
            };
            _context.Messages.Add(message);

            // Mark this specific follow-up as Completed
            followUp.Status = "Completed";
            _context.Entry(followUp).State = EntityState.Modified;

            // Also complete any other pending follow-ups for this customer
            var otherPending = await _context.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.CustomerId == customer.Id && f.Status == "Pending" && f.Id != followUp.Id)
                .ToListAsync();

            foreach (var fu in otherPending)
            {
                fu.Status = "Bypassed";
                _context.Entry(fu).State = EntityState.Modified;
            }

            // Schedule a new default follow-up 24 hours in the future only if AI auto-reply is enabled and customer is not blacklisted
            var settings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == followUp.ProjectId);
            
            bool shouldScheduleFollowUp = settings != null && settings.AiAutoReplyEnabled && !customer.IsBlacklisted;

            if (shouldScheduleFollowUp)
            {
                var defaultFollowUp = new FollowUp
                {
                    Id = Guid.NewGuid(),
                    ProjectId = followUp.ProjectId,
                    CustomerId = customer.Id,
                    Type = "Nurturing",
                    DueDate = DateTime.UtcNow.AddHours(24),
                    Notes = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟",
                    Status = "Pending"
                };
                _context.FollowUps.Add(defaultFollowUp);
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

            await _hubContext.Clients.Group($"project_{followUp.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);

            return Ok(followUp);
        }

        [HttpPost("projects/{projectId}/follow-ups/re-evaluate-all")]
        public async Task<IActionResult> ReEvaluateAllFollowUps(Guid projectId)
        {
            _tenantContext.SetProjectId(projectId);

            var pendingFollowUps = await _context.FollowUps
                .Where(f => f.ProjectId == projectId && f.Status == "Pending")
                .ToListAsync();

            if (!pendingFollowUps.Any())
            {
                return Ok(new { message = "No pending follow-ups found for this project.", count = 0 });
            }

            var projectSettings = await _context.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == projectId);
            string apiKey = projectSettings?.GeminiApiKey;
            if (string.IsNullOrEmpty(apiKey) || apiKey.StartsWith("mock_"))
            {
                apiKey = null;
            }
            string model = projectSettings?.GeminiModel;

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
                    var conversation = await _context.Conversations
                        .FirstOrDefaultAsync(c => c.CustomerId == customer.Id && c.Status != "Closed" && c.ProjectId == projectId);

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
            var query = _context.CRMUpdateProposals.AsQueryable();

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
            var memory = await _context.CustomerMemories
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
            var memory = await _context.CustomerMemories
                .FirstOrDefaultAsync(m => m.CustomerId == customerId);
            
            if (memory == null)
            {
                var customer = await _context.Customers.FindAsync(customerId);
                if (customer == null) return NotFound("Customer not found");

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
        public string? AutomationRules { get; set; }
    }

    public class CreateFollowUpRequest
    {
        public DateTime DueDate { get; set; }
        public string Notes { get; set; }
        public string? Type { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public string? Tone { get; set; }
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
