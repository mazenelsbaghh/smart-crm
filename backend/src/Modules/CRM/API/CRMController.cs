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

namespace Modules.CRM.API
{
    [ApiController]
    [Route("api")]
    public class CRMController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEventBus _eventBus;

        public CRMController(AppDbContext context, IEventBus eventBus)
        {
            _context = context;
            _eventBus = eventBus;
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
            customer.LeadScore = request.LeadScore ?? customer.LeadScore;
            customer.Tags = request.Tags ?? customer.Tags;
            customer.Notes = request.Notes ?? customer.Notes;
            customer.Label = request.Label ?? customer.Label;
            if (request.IsBudgetSet)
            {
                customer.Budget = request.Budget;
            }

            // Handle pipeline stage update
            string resolvedStageName = "New";
            if (!string.IsNullOrEmpty(request.PipelineStage))
            {
                var stage = await _context.PipelineStages
                    .FirstOrDefaultAsync(s => s.ProjectId == customer.ProjectId && s.Name.ToLower() == request.PipelineStage.ToLower());

                if (stage == null)
                {
                    int maxOrder = await _context.PipelineStages
                        .Where(s => s.ProjectId == customer.ProjectId)
                        .Select(s => s.Order)
                        .DefaultIfEmpty(-1)
                        .MaxAsync();

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
                AppointmentTime = apptTime
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
            if (memory == null) return NotFound();
            return Ok(memory);
        }
    }

    public class UpdateCustomerRequest
    {
        public string? Name { get; set; }
        public string? City { get; set; }
        public int? LeadScore { get; set; }
        public string[]? Tags { get; set; }
        public string? Notes { get; set; }
        public string? Label { get; set; }

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
    }
}
