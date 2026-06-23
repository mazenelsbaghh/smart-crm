using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Facebook.Domain;
using Modules.Facebook.Services;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Facebook.API
{
    [ApiController]
    [Route("api/projects/{projectId}/facebook/pages")]
    public class FacebookPageController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IFacebookGraphService _graphService;

        public FacebookPageController(AppDbContext context, IFacebookGraphService graphService)
        {
            _context = context;
            _graphService = graphService;
        }

        /// <summary>
        /// Confirm and save a Page selected from the OAuth flow
        /// </summary>
        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmPage(Guid projectId, [FromBody] ConfirmPageRequest request)
        {
            if (string.IsNullOrEmpty(request.FacebookPageId) || string.IsNullOrEmpty(request.PageAccessToken))
                return BadRequest(new { error = "facebookPageId and pageAccessToken are required" });

            // Check if page is already connected
            var existing = await _context.ConnectedPages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cp => cp.FacebookPageId == request.FacebookPageId);

            if (existing != null)
            {
                if (!existing.IsActive)
                {
                    // Reactivate page and update token details
                    existing.ProjectId = projectId;
                    existing.PageName = request.PageName ?? existing.PageName;
                    existing.PageAccessToken = request.PageAccessToken;
                    existing.UserAccessToken = request.UserAccessToken;
                    existing.FacebookUserId = request.FacebookUserId;
                    existing.IsActive = true;
                    existing.TokenExpiresAt = DateTime.UtcNow.AddDays(60);
                    existing.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();

                    try
                    {
                        await _graphService.SubscribePageToAppAsync(request.FacebookPageId, request.PageAccessToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Failed to subscribe page to webhooks: {ex.Message}");
                    }

                    return Ok(new
                    {
                        existing.Id,
                        existing.FacebookPageId,
                        existing.PageName,
                        existing.IsActive,
                        existing.TokenExpiresAt,
                        existing.CreatedAt
                    });
                }

                return Conflict(new { error = "This Facebook Page is already connected to a project" });
            }

            var connectedPage = new ConnectedPage
            {
                ProjectId = projectId,
                FacebookPageId = request.FacebookPageId,
                PageName = request.PageName ?? "Facebook Page",
                PageAccessToken = request.PageAccessToken,
                UserAccessToken = request.UserAccessToken,
                FacebookUserId = request.FacebookUserId,
                IsActive = true,
                TokenExpiresAt = DateTime.UtcNow.AddDays(60) // Long-lived token ~60 days
            };

            _context.ConnectedPages.Add(connectedPage);
            await _context.SaveChangesAsync();

            // Subscribe page to app webhooks
            try
            {
                await _graphService.SubscribePageToAppAsync(request.FacebookPageId, request.PageAccessToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to subscribe page to webhooks: {ex.Message}");
                // Page is saved even if subscription fails — can retry later
            }

            return Created($"/api/projects/{projectId}/facebook/pages/{connectedPage.Id}", new
            {
                connectedPage.Id,
                connectedPage.FacebookPageId,
                connectedPage.PageName,
                connectedPage.IsActive,
                connectedPage.TokenExpiresAt,
                connectedPage.CreatedAt
            });
        }

        /// <summary>
        /// List all connected Facebook Pages for a project
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPages(Guid projectId)
        {
            var pages = await _context.ConnectedPages
                .Where(cp => cp.ProjectId == projectId && cp.IsActive)
                .OrderByDescending(cp => cp.CreatedAt)
                .Select(cp => new
                {
                    cp.Id,
                    cp.FacebookPageId,
                    cp.PageName,
                    cp.IsActive,
                    cp.TokenExpiresAt,
                    cp.CreatedAt
                })
                .ToListAsync();

            return Ok(pages);
        }

        /// <summary>
        /// Disconnect (deactivate) a Facebook Page
        /// </summary>
        [HttpDelete("{pageId}")]
        public async Task<IActionResult> DisconnectPage(Guid projectId, Guid pageId)
        {
            var page = await _context.ConnectedPages
                .Where(cp => cp.ProjectId == projectId && cp.Id == pageId)
                .FirstOrDefaultAsync();

            if (page == null)
                return NotFound(new { error = "Connected page not found" });

            page.IsActive = false;
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    public class ConfirmPageRequest
    {
        public string FacebookPageId { get; set; } = string.Empty;
        public string? PageName { get; set; }
        public string PageAccessToken { get; set; } = string.Empty;
        public string? UserAccessToken { get; set; }
        public string? FacebookUserId { get; set; }
    }
}
