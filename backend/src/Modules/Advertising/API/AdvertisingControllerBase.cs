using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Security;

namespace Modules.Advertising.API;

[ApiController]
[Authorize]
public abstract class AdvertisingControllerBase(IProjectAuthorizationService authorization) : ControllerBase
{
    protected IProjectAuthorizationService ProjectAuthorization { get; } = authorization;

    protected bool CanRead(Guid projectId) => ProjectAuthorization.CanRead(User, projectId);
    protected bool CanManage(Guid projectId) => ProjectAuthorization.CanManageAdvertising(User, projectId);
    protected Guid? UserId => ProjectAuthorization.GetUserId(User);
}
