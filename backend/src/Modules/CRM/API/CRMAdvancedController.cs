using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.CRM.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.CRM.API
{
    [ApiController]
    [Route("api")]
    public class CRMAdvancedController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CRMAdvancedController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // Pipeline Stage Endpoints
        // ==========================================

        [HttpGet("projects/{projectId}/pipelines/stages")]
        public async Task<IActionResult> GetStages(Guid projectId)
        {
            var stages = await _context.PipelineStages
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.Order)
                .ToListAsync();

            if (stages.Count == 0)
            {
                var defaultStageNames = new[] { "New", "Contacted", "Proposal", "Negotiation", "Won", "Lost" };
                for (int i = 0; i < defaultStageNames.Length; i++)
                {
                    var stage = new PipelineStage
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = projectId,
                        Name = defaultStageNames[i],
                        Order = i
                    };
                    _context.PipelineStages.Add(stage);
                    stages.Add(stage);
                }
                await _context.SaveChangesAsync();
            }

            return Ok(stages);
        }

        [HttpPost("projects/{projectId}/pipelines/stages")]
        public async Task<IActionResult> CreateStage(Guid projectId, [FromBody] PipelineStage stage)
        {
            if (stage == null || string.IsNullOrEmpty(stage.Name))
            {
                return BadRequest("Stage Name is required.");
            }

            stage.ProjectId = projectId;
            _context.PipelineStages.Add(stage);
            await _context.SaveChangesAsync();

            return Created($"/api/projects/{projectId}/pipelines/stages/{stage.Id}", stage);
        }

        // ==========================================
        // Deals Endpoints
        // ==========================================

        [HttpGet("projects/{projectId}/deals")]
        public async Task<IActionResult> GetDeals(Guid projectId)
        {
            var deals = await _context.Deals
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();
            return Ok(deals);
        }

        [HttpPost("projects/{projectId}/deals")]
        public async Task<IActionResult> CreateDeal(Guid projectId, [FromBody] Deal deal)
        {
            if (deal == null || string.IsNullOrEmpty(deal.Title) || deal.CustomerId == Guid.Empty || deal.PipelineStageId == Guid.Empty)
            {
                return BadRequest("Title, CustomerId, and PipelineStageId are required.");
            }

            deal.ProjectId = projectId;
            deal.Status = DealStatus.Open;
            _context.Deals.Add(deal);
            await _context.SaveChangesAsync();

            return Created($"/api/projects/{projectId}/deals/{deal.Id}", deal);
        }

        [HttpPut("deals/{id}/stage")]
        public async Task<IActionResult> UpdateDealStage(Guid id, [FromBody] UpdateStageRequest request)
        {
            if (request == null || request.PipelineStageId == Guid.Empty)
            {
                return BadRequest("PipelineStageId is required.");
            }

            var deal = await _context.Deals.FindAsync(id);
            if (deal == null) return NotFound();

            deal.PipelineStageId = request.PipelineStageId;
            await _context.SaveChangesAsync();

            return Ok(deal);
        }

        [HttpPut("deals/{id}/status")]
        public async Task<IActionResult> UpdateDealStatus(Guid id, [FromBody] UpdateStatusRequest request)
        {
            var deal = await _context.Deals.FindAsync(id);
            if (deal == null) return NotFound();

            deal.Status = request.Status;
            if (request.Status == DealStatus.Won || request.Status == DealStatus.Lost)
            {
                deal.ClosedAt = DateTime.UtcNow;
            }
            else
            {
                deal.ClosedAt = null;
            }

            await _context.SaveChangesAsync();
            return Ok(deal);
        }
    }

    public class UpdateStageRequest
    {
        public Guid PipelineStageId { get; set; }
    }

    public class UpdateStatusRequest
    {
        public DealStatus Status { get; set; }
    }
}
