using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Security;
using Modules.Advertising.Services;

namespace Modules.Advertising.API;

[ApiController]
[Authorize]
public abstract class AdvertisingControllerBase(IProjectAuthorizationService authorization) : ControllerBase
{
    protected IProjectAuthorizationService ProjectAuthorization { get; } = authorization;

    protected bool CanRead(Guid projectId) => ProjectAuthorization.CanRead(User, projectId);
    protected bool CanManage(Guid projectId) => ProjectAuthorization.CanManageAdvertising(User, projectId);
    protected Guid? UserId => ProjectAuthorization.GetUserId(User);
    protected bool IsAutopilot(Guid projectId) => ProjectAuthorization.IsSystemAutopilot(User, projectId);
    protected string RequireIdempotencyKey() => AdvertisingMutationProtocol.RequireIdempotencyKey(Request.Headers["Idempotency-Key"].FirstOrDefault());
    protected long RequireIfMatch() => AdvertisingMutationProtocol.RequireIfMatch(Request.Headers.IfMatch.FirstOrDefault());

    protected IActionResult AcceptedOperation(Guid projectId, Guid operationId, Guid correlationId, string state = "Requested") =>
        Accepted(AdvertisingMutationProtocol.Accepted(projectId, operationId, correlationId, state));
}
