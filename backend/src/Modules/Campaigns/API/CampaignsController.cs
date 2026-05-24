using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Campaigns.Domain;
using Modules.CRM.Domain;
using Modules.Campaigns.Application.Services;
using Modules.Campaigns.Jobs;
using Hangfire;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Campaigns.API
{
    [ApiController]
    [Route("api")]
    public class CampaignsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICampaignAIService _campaignAIService;

        public CampaignsController(AppDbContext context, ICampaignAIService campaignAIService)
        {
            _context = context;
            _campaignAIService = campaignAIService;
        }

        // ==========================================
        // Segments Endpoints
        // ==========================================

        [HttpGet("projects/{projectId}/segments")]
        public async Task<IActionResult> GetSegments(Guid projectId)
        {
            var segments = await _context.Segments
                .Where(s => s.ProjectId == projectId)
                .ToListAsync();
            return Ok(segments);
        }

        [HttpPost("projects/{projectId}/segments")]
        public async Task<IActionResult> CreateSegment(Guid projectId, [FromBody] Segment segment)
        {
            if (segment == null || string.IsNullOrEmpty(segment.Name))
            {
                return BadRequest("Segment Name is required.");
            }

            segment.ProjectId = projectId;
            _context.Segments.Add(segment);
            await _context.SaveChangesAsync();

            return Created($"/api/projects/{projectId}/segments/{segment.Id}", segment);
        }

        // ==========================================
        // Campaigns Endpoints
        // ==========================================

        [HttpGet("projects/{projectId}/campaigns")]
        public async Task<IActionResult> GetCampaigns(Guid projectId)
        {
            var campaigns = await _context.Campaigns
                .Where(c => c.ProjectId == projectId)
                .ToListAsync();
            return Ok(campaigns);
        }

        [HttpPost("projects/{projectId}/campaigns")]
        public async Task<IActionResult> CreateCampaign(Guid projectId, [FromBody] Campaign campaign)
        {
            if (campaign == null || string.IsNullOrEmpty(campaign.Name) || string.IsNullOrEmpty(campaign.MessageTemplateA))
            {
                return BadRequest("Campaign Name and MessageTemplateA are required.");
            }

            campaign.ProjectId = projectId;
            campaign.Status = CampaignStatus.Draft;
            _context.Campaigns.Add(campaign);
            await _context.SaveChangesAsync();

            return Created($"/api/campaigns/{campaign.Id}", campaign);
        }

        [HttpGet("campaigns/{id}")]
        public async Task<IActionResult> GetCampaign(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            return Ok(campaign);
        }

        [HttpPost("campaigns/{id}/schedule")]
        public async Task<IActionResult> ScheduleCampaign(Guid id, [FromBody] DateTime? scheduledAt = null)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();

            campaign.Status = CampaignStatus.Scheduled;
            campaign.ScheduledAt = scheduledAt ?? DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Register background Hangfire job to run at schedule
            var delay = campaign.ScheduledAt.Value - DateTime.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            BackgroundJob.Schedule<CampaignSenderJob>(job => job.StartCampaignAsync(campaign.Id), delay);

            return Ok(new { status = "Scheduled", scheduledAt = campaign.ScheduledAt });
        }

        [HttpPost("campaigns/{id}/pause")]
        public async Task<IActionResult> PauseCampaign(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();

            campaign.Status = CampaignStatus.Paused;
            await _context.SaveChangesAsync();

            return Ok(new { status = "Paused" });
        }

        [HttpGet("campaigns/{id}/results")]
        public async Task<IActionResult> GetCampaignResults(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();

            var recipients = await _context.CampaignRecipients
                .Where(r => r.CampaignId == id)
                .ToListAsync();

            var variantA = recipients.Where(r => r.Variant == "A").ToList();
            var variantB = recipients.Where(r => r.Variant == "B").ToList();

            var results = new
            {
                campaignId = campaign.Id,
                name = campaign.Name,
                status = campaign.Status.ToString(),
                sentCount = campaign.SentCount,
                deliveredCount = campaign.DeliveredCount,
                readCount = campaign.ReadCount,
                responseCount = campaign.ResponseCount,
                variants = new
                {
                    A = new
                    {
                        sent = variantA.Count(r => r.Status != RecipientStatus.Pending && r.Status != RecipientStatus.Failed),
                        delivered = variantA.Count(r => r.Status == RecipientStatus.Delivered || r.Status == RecipientStatus.Read || r.Status == RecipientStatus.Responded),
                        responded = variantA.Count(r => r.Status == RecipientStatus.Responded)
                    },
                    B = new
                    {
                        sent = variantB.Count(r => r.Status != RecipientStatus.Pending && r.Status != RecipientStatus.Failed),
                        delivered = variantB.Count(r => r.Status == RecipientStatus.Delivered || r.Status == RecipientStatus.Read || r.Status == RecipientStatus.Responded),
                        responded = variantB.Count(r => r.Status == RecipientStatus.Responded)
                    }
                }
            };

            return Ok(results);
        }

        [HttpPost("campaigns/generate-copy")]
        public async Task<IActionResult> GenerateCopy([FromBody] GenerateCopyRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Prompt))
            {
                return BadRequest("Prompt is required.");
            }

            try
            {
                var generatedCopy = await _campaignAIService.GenerateCampaignCopyAsync(request.Prompt, request.BaseTemplate ?? "", request.TargetContext ?? "");
                return Ok(new { copy = generatedCopy });
            }
            catch (Exception ex)
            {
                return BadRequest($"Failed to generate copy: {ex.Message}");
            }
        }
    }

    public class GenerateCopyRequest
    {
        public string Prompt { get; set; }
        public string? BaseTemplate { get; set; }
        public string? TargetContext { get; set; }
    }
}
