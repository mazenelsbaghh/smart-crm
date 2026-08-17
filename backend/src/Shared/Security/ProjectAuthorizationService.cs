using System.Security.Claims;

namespace Shared.Security;

public interface IProjectAuthorizationService
{
    bool CanRead(ClaimsPrincipal user, Guid projectId);
    bool CanManageAdvertising(ClaimsPrincipal user, Guid projectId);
    Guid? GetUserId(ClaimsPrincipal user);
}

public sealed class ProjectAuthorizationService : IProjectAuthorizationService
{
    public bool CanRead(ClaimsPrincipal user, Guid projectId) =>
        user.Identity?.IsAuthenticated == true && GetProjectId(user) == projectId;

    public bool CanManageAdvertising(ClaimsPrincipal user, Guid projectId)
    {
        if (!CanRead(user, projectId)) return false;
        var role = user.FindFirstValue(ClaimTypes.Role);
        return role is not null && (role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    }

    public Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static Guid? GetProjectId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("ProjectId") ?? user.FindFirstValue("project_id");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
