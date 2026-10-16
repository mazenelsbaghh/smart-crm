using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Services;
using Shared.Infrastructure;

namespace Shared.Security;

public sealed class AccessSessionValidator(AppDbContext db)
{
    public async Task<bool> IsActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue("ProjectId"), out var projectId)
            || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)) return false;
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null || await ProjectAccess.RoleFor(db, user, projectId) != principal.FindFirstValue(ClaimTypes.Role))
            return false;
        return await db.RefreshTokens.AsNoTracking().AnyAsync(token => (token.SessionId ?? token.Id) == sessionId
            && token.UserId == userId && (token.ProjectId ?? user.ProjectId) == projectId
            && token.RevokedAt == null && token.ExpiresAt > DateTime.UtcNow, cancellationToken);
    }
}
