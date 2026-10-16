using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Shared.Security;

public static class ProjectSecurity
{
    public static IServiceCollection AddProjectSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options => options.Filters.Add<ProjectRequestAuthorizationFilter>());
        services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddScoped<AccessSessionValidator>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(JwtSettings.SigningSecret(configuration))),
                ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ReadHubToken,
                OnTokenValidated = ValidateSession
            };
        });
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();
        return services;
    }

    private static Task ReadHubToken(MessageReceivedContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs/notifications")
            && context.Request.Query.TryGetValue("access_token", out var token))
            context.Token = token.ToString();
        return Task.CompletedTask;
    }

    private static async Task ValidateSession(TokenValidatedContext context)
    {
        var validator = context.HttpContext.RequestServices.GetRequiredService<AccessSessionValidator>();
        if (context.Principal is null || !await validator.IsActiveAsync(context.Principal, context.HttpContext.RequestAborted))
            context.Fail("Session is no longer active.");
    }
}
