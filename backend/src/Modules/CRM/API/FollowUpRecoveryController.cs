using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.CRM.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/follow-ups/recover")]
public sealed class FollowUpRecoveryController(
    AppDbContext db, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Recover(
        Guid projectId, [FromBody] RecoverFollowUpRequest request, CancellationToken cancellationToken)
    {
        if (!authorization.CanWrite(User, projectId)) return Forbid();
        var recovery = await new FollowUpRecoveryService(db).RecoverAsync(projectId, request, cancellationToken);
        var response = new
        {
            recovery.Code,
            recovery.CustomerName,
            followUpId = recovery.FollowUp?.Id,
            dueDate = recovery.FollowUp?.DueDate,
            status = recovery.FollowUp?.Status
        };
        return recovery.Code switch
        {
            "CREATED" or "ALREADY_ACTIVE" => Ok(response),
            "CUSTOMER_NOT_FOUND" => NotFound(response),
            "INVALID_PHONE" or "INVALID_DUE_DATE" => BadRequest(response),
            _ => Conflict(response)
        };
    }
}
