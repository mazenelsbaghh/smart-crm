using System;
using System.Security.Claims;

namespace Shared.Security
{
    public sealed record AccessTokenIdentity(Guid UserId, string Email, string Role, Guid ProjectId, Guid SessionId);

    public interface IJwtService
    {
        string GenerateToken(AccessTokenIdentity identity);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
    }
}
