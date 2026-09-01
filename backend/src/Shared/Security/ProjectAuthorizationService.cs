using System.Security.Claims;

namespace Shared.Security;

public interface IProjectAuthorizationService
{
    bool CanRead(ClaimsPrincipal user, Guid projectId);
    bool CanManageProject(ClaimsPrincipal user, Guid projectId);
    bool CanManageAdvertising(ClaimsPrincipal user, Guid projectId);
    Guid? GetProjectId(ClaimsPrincipal user);
    Guid? GetUserId(ClaimsPrincipal user);
    bool IsSystemAutopilot(ClaimsPrincipal user, Guid projectId);
}

public sealed class ProjectAuthorizationService : IProjectAuthorizationService
{
    public bool CanRead(ClaimsPrincipal user, Guid projectId) =>
        user.Identity?.IsAuthenticated == true && GetProjectId(user) == projectId;

    public bool CanManageProject(ClaimsPrincipal user, Guid projectId)
    {
        if (!CanRead(user, projectId)) return false;
        var role = user.FindFirstValue(ClaimTypes.Role);
        return role is not null && (role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    }

    public bool CanManageAdvertising(ClaimsPrincipal user, Guid projectId) =>
        CanManageProject(user, projectId);

    public Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsSystemAutopilot(ClaimsPrincipal user, Guid projectId) =>
        CanRead(user, projectId) && string.Equals(user.FindFirstValue("actor_type"), "SystemAutopilot", StringComparison.Ordinal);

    public Guid? GetProjectId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("ProjectId") ?? user.FindFirstValue("project_id");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
