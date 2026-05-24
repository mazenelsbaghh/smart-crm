using Microsoft.AspNetCore.Mvc;
using Modules.Integrations.Services;
using System;
using System.Threading.Tasks;

namespace Modules.Integrations.API
{
    [ApiController]
    [Route("api")]
    public class IntegrationsController : ControllerBase
    {
        private readonly IProjectIntegrationService _integrationService;

        public IntegrationsController(IProjectIntegrationService integrationService)
        {
            _integrationService = integrationService;
        }

        [HttpPost("projects/{projectId}/integrations")]
        public async Task<IActionResult> ConfigureIntegration(Guid projectId, [FromBody] ConfigureIntegrationRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ProviderName))
            {
                return BadRequest("ProviderName is required.");
            }

            var integration = await _integrationService.ConfigureIntegrationAsync(
                projectId,
                request.ProviderName,
                request.ConfigJson ?? "{}",
                request.IsActive,
                request.SyncIntervalMinutes
            );

            return Created($"/api/projects/{projectId}/integrations/{integration.Id}", integration);
        }

        [HttpPost("projects/{projectId}/integrations/{integrationId}/sync")]
        public async Task<IActionResult> TriggerSync(Guid projectId, Guid integrationId)
        {
            try
            {
                await _integrationService.TriggerSyncAsync(projectId, integrationId);
                return Accepted(new { message = "Sync job triggered successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    public class ConfigureIntegrationRequest
    {
        public string ProviderName { get; set; } = string.Empty;
        public string ConfigJson { get; set; } = "{}";
        public bool IsActive { get; set; } = true;
        public int SyncIntervalMinutes { get; set; } = 60;
    }
}
