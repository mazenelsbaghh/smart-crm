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
        private readonly int _expiryMinutes;

        public JwtService(IConfiguration configuration)
        {
            _secret = configuration["JWT:Secret"] ?? "a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long";
            _expiryMinutes = int.TryParse(configuration["JWT:ExpiryMinutes"], out var val) ? val : 15;
        }

        private static readonly Dictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
        {
            { "Owner", new[] { "read", "write", "delete", "invite", "manage_roles" } },
            { "Admin", new[] { "read", "write", "delete", "invite", "manage_roles" } },
            { "Supervisor", new[] { "read", "write", "invite" } },
            { "Agent", new[] { "read", "write" } },
            { "AI Reviewer", new[] { "read", "review" } },
            { "Analyst", new[] { "read", "analyze" } }
        };

        public string GenerateToken(Guid userId, string email, string role, Guid projectId)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Role, role),
                new Claim("ProjectId", projectId.ToString())
            };

            if (role != null && RolePermissions.TryGetValue(role, out var permissions))
            {
                foreach (var perm in permissions)
                {
                    claims.Add(new Claim("permission", perm));
                }
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_expiryMinutes),
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
