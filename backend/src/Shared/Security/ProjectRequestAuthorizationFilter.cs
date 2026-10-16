using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shared.Security;

public sealed class ProjectRequestAuthorizationFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        var user = context.HttpContext.User;
        var authorization = new ProjectAuthorizationService();
        var projectId = authorization.GetProjectId(user);
        if (projectId is null || !authorization.CanRead(user, projectId.Value))
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (context.RouteData.Values.TryGetValue("projectId", out var requested)
            && (!Guid.TryParse(requested?.ToString(), out var requestedId) || requestedId != projectId))
        {
            context.Result = new ForbidResult();
            return;
        }
        var method = context.HttpContext.Request.Method;
        var permission = context.ActionDescriptor.EndpointMetadata.OfType<ProjectPermissionAttribute>().LastOrDefault()?.Permission
            ?? (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ? "read"
                : HttpMethods.IsDelete(method) ? "delete" : "write");
        if (!JwtService.HasPermission(user, permission)) context.Result = new ForbidResult();
    }
}
