namespace Shared.Security;

public sealed class TenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var projectId = new ProjectAuthorizationService().GetProjectId(context.User);
        if (context.User.Identity?.IsAuthenticated == true && projectId is { } claimedProject)
        {
            if (context.Request.Headers.TryGetValue("X-Project-Id", out var requested)
                && (!Guid.TryParse(requested.ToString(), out var requestedProject) || requestedProject != claimedProject))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "PROJECT_CONTEXT_MISMATCH" });
                return;
            }
            tenantContext.SetProjectId(claimedProject);
        }
        await next(context);
    }
}
