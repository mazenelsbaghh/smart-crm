using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Modules.SystemHealth.Services;

namespace Modules.SystemHealth.API
{
    [ApiController]
    [Route("api/system")]
    public class SystemHealthController : ControllerBase
    {
        private readonly ISystemHealthService _healthService;

        public SystemHealthController(ISystemHealthService healthService)
        {
            _healthService = healthService;
        }

        [HttpGet("health")]
        public async Task<IActionResult> GetHealth()
        {
            var health = await _healthService.CheckHealthAsync();
            if (health.Status == "Healthy")
            {
                return Ok(health);
            }
            return StatusCode(503, health);
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics()
        {
            var metrics = await _healthService.GetMetricsAsync();
            return Ok(metrics);
        }
    }
}
