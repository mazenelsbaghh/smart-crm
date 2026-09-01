using System.Security.Cryptography;
using System.Text;

namespace Shared.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WhatsAppGatewayAuthenticatedAttribute : Attribute
{
}

public sealed class WhatsAppGatewayWebhookAuthenticationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-WhatsApp-Gateway-Secret";

    public async Task InvokeAsync(
        HttpContext context,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<WhatsAppGatewayAuthenticatedAttribute>() is null)
        {
            await next(context);
            return;
        }

        var expectedSecret = configuration["WhatsAppGateway:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            if (environment.IsEnvironment("Test"))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { code = "WHATSAPP_GATEWAY_AUTH_NOT_CONFIGURED" });
            return;
        }

        var suppliedSecret = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!Matches(expectedSecret, suppliedSecret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { code = "WHATSAPP_GATEWAY_AUTH_INVALID" });
            return;
        }

        await next(context);
    }

    private static bool Matches(string expectedSecret, string? suppliedSecret)
    {
        if (string.IsNullOrEmpty(suppliedSecret))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedSecret));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
