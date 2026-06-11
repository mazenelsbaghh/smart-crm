using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Shared.Infrastructure;
using Shared.Events;
using Shared.Queue;
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

        public CRMController(AppDbContext context, IEventBus eventBus, ICustomerMemoryService customerMemoryService, IConfiguration configuration, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _eventBus = eventBus;
            _customerMemoryService = customerMemoryService;
            _configuration = configuration;
            _hubContext = hubContext;
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
                pipelineStage = customerStages.TryGetValue(c.Id, out var stage) ? stage : "New"
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
                pipelineStage = stageName
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
                pipelineStage = resolvedStageName
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
