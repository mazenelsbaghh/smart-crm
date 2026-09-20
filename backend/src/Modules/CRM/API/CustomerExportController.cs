using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.CRM.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/customers/export")]
public sealed class CustomerExportController(AppDbContext context, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetExport(Guid projectId, CancellationToken cancellationToken,
        [FromQuery] string audience = "inactive30")
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        if (audience is not ("inactive30" or "nonSubscribers"))
            return BadRequest(new { code = "INVALID_EXPORT_AUDIENCE" });
        var cutoff = audience == "inactive30" ? DateTime.UtcNow.AddDays(-30) : (DateTime?)null;
        var customers = await new CustomerExportQuery(context).GetCustomersAsync(projectId, cutoff, cancellationToken);
        return Ok(customers);
    }
}
