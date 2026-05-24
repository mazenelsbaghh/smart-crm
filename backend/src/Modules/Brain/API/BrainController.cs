using Microsoft.AspNetCore.Mvc;
using Modules.Brain.Services;
using System;
using System.Threading.Tasks;

namespace Modules.Brain.API
{
    [ApiController]
    [Route("api")]
    public class BrainController : ControllerBase
    {
        private readonly IAICompanyBrain _companyBrain;

        public BrainController(IAICompanyBrain companyBrain)
        {
            _companyBrain = companyBrain;
        }

        [HttpPost("projects/{projectId}/brain/sync")]
        public async Task<IActionResult> SyncBrain(Guid projectId)
        {
            await _companyBrain.SyncBrainAsync(projectId);
            return Accepted(new { message = "Sync completed successfully" });
        }

        [HttpGet("projects/{projectId}/brain/search")]
        public async Task<IActionResult> SearchBrain(Guid projectId, [FromQuery] string q, [FromQuery] int limit = 3)
        {
            if (string.IsNullOrEmpty(q))
            {
                return BadRequest("Query parameter 'q' is required");
            }

            var results = await _companyBrain.SearchBrainAsync(projectId, q, limit);
            return Ok(results);
        }
    }
}
