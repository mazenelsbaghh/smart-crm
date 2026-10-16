using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.GroupAppointments.API;

[ApiController]
[Authorize]
[Route("api/group-appointments")]
public sealed class GroupParticipationController(GroupParticipationService participation,
    ITenantContext tenant, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet("{groupId:guid}/participation")]
    public async Task<IActionResult> Get(Guid groupId, CancellationToken ct)
    {
        if (!authorization.CanRead(User, tenant.ProjectId)) return Forbid();
        var report = await participation.GetAsync(tenant.ProjectId, groupId, ct);
        return report is null ? NotFound() : Ok(report);
    }
}
