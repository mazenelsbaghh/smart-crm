using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Content.Services;
using Shared.Security;

namespace Modules.Content.API;

[ApiController]
[Authorize]
[Route("api/content/images")]
public sealed class ContentImageTrialsController(ITenantContext tenant,
    IProjectAuthorizationService authorization, ContentImageTrialService trials) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(ContentImageTrialRequest input, CancellationToken cancellationToken)
    {
        if (tenant.ProjectId == Guid.Empty || !authorization.CanManageProject(User, tenant.ProjectId)) return Forbid();
        try { return Ok(await trials.GenerateAsync(tenant.ProjectId, input, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }
}
