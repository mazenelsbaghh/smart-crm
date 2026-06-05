using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Modules.Media.Services;
using Shared.Security;

namespace Modules.Media.API
{
    [ApiController]
    [Route("api/assets")]
    public class AssetsController : ControllerBase
    {
        private readonly IAssetService _assetService;
        private readonly ITenantContext _tenantContext;

        public AssetsController(IAssetService assetService, ITenantContext tenantContext)
        {
            _assetService = assetService;
            _tenantContext = tenantContext;
        }

        [HttpPost("upload")]
        [Authorize]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAsset([FromForm] IFormFile file, [FromForm] Guid projectId)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file was uploaded." });
            }

            if (projectId == Guid.Empty)
            {
                return BadRequest(new { error = "ProjectId is required." });
            }

            _tenantContext.SetProjectId(projectId);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            var uploadedBy = userIdClaim != null ? Guid.Parse(userIdClaim.Value) : Guid.Empty;

            try
            {
                using var stream = file.OpenReadStream();
                var asset = await _assetService.UploadAssetAsync(projectId, file.FileName, file.ContentType, stream, uploadedBy);
                return StatusCode(201, asset);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("/api/projects/{projectId}/assets/upload")]
        [AllowAnonymous]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAssetAnonymous(Guid projectId, [FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file was uploaded." });
            }

            if (projectId == Guid.Empty)
            {
                return BadRequest(new { error = "ProjectId is required." });
            }

            _tenantContext.SetProjectId(projectId);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            var uploadedBy = userIdClaim != null ? Guid.Parse(userIdClaim.Value) : Guid.Empty;

            try
            {
                using var stream = file.OpenReadStream();
                var asset = await _assetService.UploadAssetAsync(projectId, file.FileName, file.ContentType, stream, uploadedBy);
                return StatusCode(201, asset);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("/api/projects/{projectId}/assets/{id}/url")]
        [Authorize]
        public async Task<IActionResult> GetPresignedUrl(Guid projectId, Guid id)
        {
            try
            {
                var downloadUrl = await _assetService.GetSignedUrlAsync(id);
                return Ok(new
                {
                    assetId = id,
                    url = downloadUrl,
                    expiry = DateTime.UtcNow.AddHours(1)
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}/download")]
        [Authorize]
        public async Task<IActionResult> GetDownloadUrl(Guid id)
        {
            try
            {
                var downloadUrl = await _assetService.GetSignedUrlAsync(id);
                return Ok(new
                {
                    assetId = id,
                    downloadUrl = downloadUrl,
                    expiresAt = DateTime.UtcNow.AddHours(1)
                });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}/thumbnail")]
        [Authorize]
        public async Task<IActionResult> GetThumbnailUrl(Guid id)
        {
            try
            {
                var thumbnailUrl = await _assetService.GetThumbnailUrlAsync(id);
                return Redirect(thumbnailUrl);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteAsset(Guid id)
        {
            try
            {
                var remainingAsset = await _assetService.DeleteAssetAsync(id);
                if (remainingAsset == null)
                {
                    return NoContent(); // 204 Deleted completely
                }
                return Ok(remainingAsset); // 200 Decremented reference count
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
