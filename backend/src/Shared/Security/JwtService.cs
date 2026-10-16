using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Shared.Security
{
    public class JwtService : IJwtService
    {
        private readonly string _secret;

        public JwtService(IConfiguration configuration)
        {
            _secret = JwtSettings.SigningSecret(configuration);
        }

        private static readonly Dictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
        {
            { "Owner", new[] { "read", "write", "delete", "invite", "manage_roles", "approve_actions" } },
            { "Admin", new[] { "read", "write", "delete", "invite", "manage_roles", "approve_actions" } },
            { "Supervisor", new[] { "read", "write", "invite", "approve_actions" } },
            { "Agent", new[] { "read", "write" } },
            { "AI Reviewer", new[] { "read", "review" } },
            { "Analyst", new[] { "read", "analyze" } }
        };

        public static bool HasPermission(ClaimsPrincipal user, string permission) =>
            user.Identity?.IsAuthenticated == true
            && RolePermissions.TryGetValue(user.FindFirstValue(ClaimTypes.Role) ?? "", out var permissions)
            && permissions.Contains(permission);

        public string GenerateToken(AccessTokenIdentity identity)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, identity.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, identity.Email),
                new Claim(ClaimTypes.Role, identity.Role),
                new Claim("ProjectId", identity.ProjectId.ToString()),
                new Claim("sid", identity.SessionId.ToString())
            };

            if (identity.Role != null && RolePermissions.TryGetValue(identity.Role, out var permissions))
            {
                foreach (var perm in permissions)
                {
                    claims.Add(new Claim("permission", perm));
                }
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(JwtSettings.LifetimeMinutes),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
                ValidateLifetime = false // Here we validate an expired token intentionally
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;

            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }
    }
}
