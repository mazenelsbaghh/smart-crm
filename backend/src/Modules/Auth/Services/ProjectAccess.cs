using Microsoft.EntityFrameworkCore;
using Modules.Auth.Domain;
using Shared.Infrastructure;

namespace Modules.Auth.Services;

public static class ProjectAccess
{
    public static async Task<string?> RoleFor(AppDbContext db, User user, Guid projectId)
    {
        if (user.ProjectId == projectId) return user.Role;
        return await db.ProjectMemberships
            .Where(membership => membership.UserId == user.Id && membership.ProjectId == projectId)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync();
    }
}
