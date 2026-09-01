using Modules.Advertising.Services;
using Shared.Infrastructure;

namespace Modules.Advertising.API;

public sealed class AdvertisingExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdvertisingAuditService audit, AppDbContext db)
    {
        try
        {
            await next(context);
            if (context.Response.StatusCode == StatusCodes.Status403Forbidden &&
                context.Request.Path.Value?.Contains("/ad-manager", StringComparison.OrdinalIgnoreCase) == true &&
                Guid.TryParse(context.Request.RouteValues["projectId"]?.ToString(), out var projectId))
            {
                Guid? userId = Guid.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsedUser)
                    ? parsedUser
                    : null;
                audit.Append(new(projectId, "Security", "CrossProjectDenied", "AdvertisingApi", context.Request.Path,
                    "User", userId, "{}", Guid.NewGuid()));
                await db.SaveChangesAsync();
            }
        }
        catch (AdvertisingException ex) when (context.Request.Path.Value?.Contains("/ad-manager", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/problem+json";
            var correlationId = Guid.TryParse(context.TraceIdentifier, out var parsed) ? parsed : Guid.NewGuid();
            await context.Response.WriteAsJsonAsync(new AdvertisingErrorEnvelope(ex.Code,
                AdvertisingErrorEnvelope.Sanitize(ex.Message), correlationId));
        }
    }
}
