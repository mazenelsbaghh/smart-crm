using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
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
using Shared.Security;

namespace Modules.Campaigns.API
{
    [ApiController]
    [Authorize]
    [Route("api")]
    public class CampaignsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ICampaignAIService _campaignAIService;
        private readonly Modules.WhatsApp.Services.WhatsAppAccountService _whatsAppAccounts;
        private readonly IProjectAuthorizationService _authorization;

        public CampaignsController(
            AppDbContext context,
            ICampaignAIService campaignAIService,
            Modules.WhatsApp.Services.WhatsAppAccountService whatsAppAccounts,
            IProjectAuthorizationService authorization)
        {
            _context = context;
            _campaignAIService = campaignAIService;
            _whatsAppAccounts = whatsAppAccounts;
            _authorization = authorization;
        }

        // ==========================================
        // Segments Endpoints
        // ==========================================

        [HttpGet("projects/{projectId}/segments")]
        public async Task<IActionResult> GetSegments(Guid projectId)
        {
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var segments = await _context.Segments
                .Where(s => s.ProjectId == projectId)
                .ToListAsync();
            return Ok(segments);
        }

        [HttpPost("projects/{projectId}/segments")]
        public async Task<IActionResult> CreateSegment(Guid projectId, [FromBody] Segment segment)
        {
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
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
            if (!_authorization.CanRead(User, projectId)) return Forbid();
            var campaigns = await _context.Campaigns
                .Where(c => c.ProjectId == projectId)
                .ToListAsync();
            return Ok(campaigns);
        }

        [HttpPost("projects/{projectId}/campaigns")]
        public async Task<IActionResult> CreateCampaign(Guid projectId, [FromBody] Campaign campaign)
        {
            if (!_authorization.CanManageProject(User, projectId)) return Forbid();
            if (campaign == null || string.IsNullOrEmpty(campaign.Name) || string.IsNullOrEmpty(campaign.MessageTemplateA))
            {
                return BadRequest("Campaign Name and MessageTemplateA are required.");
            }
            var segmentExists = await _context.Segments
                .AnyAsync(segment => segment.Id == campaign.SegmentId && segment.ProjectId == projectId);
            if (!segmentExists) return BadRequest(new { code = "CAMPAIGN_SEGMENT_NOT_IN_PROJECT" });

            campaign.ProjectId = projectId;
            var account = await _whatsAppAccounts.ResolveAsync(projectId, campaign.WhatsAppAccountId);
            if (account is null) return BadRequest(new { code = "WHATSAPP_ACCOUNT_NOT_IN_PROJECT" });
            campaign.WhatsAppAccountId = account.Id;
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
            if (!_authorization.CanRead(User, campaign.ProjectId)) return Forbid();
            return Ok(campaign);
        }

        [HttpPost("campaigns/{id}/schedule")]
        public async Task<IActionResult> ScheduleCampaign(Guid id, [FromBody] DateTime? scheduledAt = null)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            if (!_authorization.CanManageProject(User, campaign.ProjectId)) return Forbid();

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
            if (!_authorization.CanManageProject(User, campaign.ProjectId)) return Forbid();

            campaign.Status = CampaignStatus.Paused;
            await _context.SaveChangesAsync();

            return Ok(new { status = "Paused" });
        }

        [HttpPost("campaigns/{id}/resume")]
        public async Task<IActionResult> ResumeCampaign(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            if (!_authorization.CanManageProject(User, campaign.ProjectId)) return Forbid();
            if (campaign.Status != CampaignStatus.Paused) return Conflict("Only paused campaigns can be resumed.");

            BackgroundJob.Enqueue<CampaignSenderJob>(job => job.ResumeFirstBatchAsync(campaign.Id));
            return Accepted(new { status = "Resuming", batchSize = 50 });
        }

        [HttpPost("campaigns/{id}/accelerate-two-hours")]
        public async Task<IActionResult> AccelerateCampaign(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            if (!_authorization.CanManageProject(User, campaign.ProjectId)) return Forbid();
            if (campaign.Status != CampaignStatus.Running) return Conflict("Only running campaigns can be accelerated.");

            BackgroundJob.Enqueue<CampaignSenderJob>(job => job.AccelerateAfterFirstBatchAsync(campaign.Id));
            return Accepted(new { status = "Accelerating", targetMinutes = 120 });
        }

        [HttpPost("campaigns/{id}/accelerate-now")]
        public async Task<IActionResult> AccelerateCampaignNow(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            if (!_authorization.CanManageProject(User, campaign.ProjectId)) return Forbid();
            if (campaign.Status != CampaignStatus.Running) return Conflict("Only running campaigns can be accelerated.");

            BackgroundJob.Enqueue<CampaignSenderJob>(job => job.AccelerateAllPendingAsync(campaign.Id));
            return Accepted(new { status = "Accelerating", messageGapSeconds = "8-12" });
        }

        [HttpGet("campaigns/{id}/results")]
        public async Task<IActionResult> GetCampaignResults(Guid id)
        {
            var campaign = await _context.Campaigns.FindAsync(id);
            if (campaign == null) return NotFound();
            if (!_authorization.CanRead(User, campaign.ProjectId)) return Forbid();

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
                        sent = variantA.Count(r => IsSent(r.Status)),
                        delivered = variantA.Count(r => r.Status == RecipientStatus.Delivered || r.Status == RecipientStatus.Read || r.Status == RecipientStatus.Responded),
                        responded = variantA.Count(r => r.Status == RecipientStatus.Responded)
                    },
                    B = new
                    {
                        sent = variantB.Count(r => IsSent(r.Status)),
                        delivered = variantB.Count(r => r.Status == RecipientStatus.Delivered || r.Status == RecipientStatus.Read || r.Status == RecipientStatus.Responded),
                        responded = variantB.Count(r => r.Status == RecipientStatus.Responded)
                    }
                }
            };

            return Ok(results);
        }

        private static bool IsSent(RecipientStatus status) =>
            status is RecipientStatus.Sent
                or RecipientStatus.Delivered
                or RecipientStatus.Read
                or RecipientStatus.Responded;

        [HttpPost("campaigns/generate-copy")]
        public async Task<IActionResult> GenerateCopy([FromBody] GenerateCopyRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Prompt))
            {
                return BadRequest("Prompt is required.");
            }
            if (request.ProjectId.HasValue && !_authorization.CanRead(User, request.ProjectId.Value)) return Forbid();

            try
            {
                var generatedCopy = request.ProjectId.HasValue
                    ? await _campaignAIService.GenerateProjectCampaignCopyAsync(request.ProjectId.Value, request.Prompt, request.BaseTemplate ?? "", request.TargetContext ?? "")
                    : await _campaignAIService.GenerateCampaignCopyAsync(request.Prompt, request.BaseTemplate ?? "", request.TargetContext ?? "");
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
        public Guid? ProjectId { get; set; }
        public string Prompt { get; set; }
        public string? BaseTemplate { get; set; }
        public string? TargetContext { get; set; }
    }
}
