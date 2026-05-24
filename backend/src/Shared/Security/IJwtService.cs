using System;
using System.Security.Claims;

namespace Shared.Security
{
    public interface IJwtService
    {
        string GenerateToken(Guid userId, string email, string role, Guid projectId);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
    }
}
