using Microsoft.AspNetCore.Http;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Security
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
        {
            // 1. Check if X-Project-Id header exists (especially useful for integration tests or external calls)
            if (context.Request.Headers.TryGetValue("X-Project-Id", out var projectIdHeader))
            {
                if (Guid.TryParse(projectIdHeader.ToString(), out var projectId))
                {
                    tenantContext.SetProjectId(projectId);
                }
            }
            // 2. Otherwise extract from JWT token if Authorization header is present or access_token query parameter
            else if (context.Request.Headers.ContainsKey("Authorization") || context.Request.Query.ContainsKey("access_token"))
            {
                string token = "";
                if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    token = authHeader.ToString().Replace("Bearer ", "").Trim();
                }

                if (string.IsNullOrEmpty(token) && context.Request.Query.TryGetValue("access_token", out var accessTokenQuery))
                {
                    token = accessTokenQuery.ToString().Trim();
                }

                if (!string.IsNullOrEmpty(token))
                {
                    try
                    {
                        var handler = new JwtSecurityTokenHandler();
                        if (handler.CanReadToken(token))
                        {
                            var jwtToken = handler.ReadJwtToken(token);
                            var projectIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "ProjectId")?.Value;
                            if (Guid.TryParse(projectIdClaim, out var projectId))
                            {
                                tenantContext.SetProjectId(projectId);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors, auth will fail down the request pipe if required
                    }
                }
            }

            await _next(context);
        }
    }
}
