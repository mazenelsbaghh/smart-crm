using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Shared.Infrastructure;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppGatewayWebhookAuthenticationMiddlewareTests
{
    private const string WebhookSecret = "synthetic-gateway-webhook-secret";
    private const string GatewaySecretHeader = "X-WhatsApp-Gateway-Secret";

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Non_test_webhook_without_configured_secret_returns_service_unavailable_without_calling_next(
        string environmentName)
    {
        var nextCallCount = 0;
        var middleware = Middleware(() => nextCallCount++);
        var context = WebhookContext();

        await middleware.InvokeAsync(context, Configuration(), HostEnvironment(environmentName));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("WHATSAPP_GATEWAY_AUTH_NOT_CONFIGURED", await ResponseCode(context));
        Assert.Equal(0, nextCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-synthetic-gateway-webhook-secret")]
    public async Task Production_webhook_with_missing_or_wrong_header_returns_unauthorized_without_calling_next(
        string? suppliedSecret)
    {
        var nextCallCount = 0;
        var middleware = Middleware(() => nextCallCount++);
        var context = WebhookContext();
        if (suppliedSecret is not null)
            context.Request.Headers[GatewaySecretHeader] = suppliedSecret;

        await middleware.InvokeAsync(
            context,
            Configuration(WebhookSecret),
            HostEnvironment(Environments.Production));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("WHATSAPP_GATEWAY_AUTH_INVALID", await ResponseCode(context));
        Assert.Equal(0, nextCallCount);
    }

    [Fact]
    public async Task Production_webhook_with_valid_secret_calls_next_once()
    {
        var nextCallCount = 0;
        var middleware = Middleware(() => nextCallCount++);
        var context = WebhookContext();
        context.Request.Headers[GatewaySecretHeader] = WebhookSecret;

        await middleware.InvokeAsync(
            context,
            Configuration(WebhookSecret),
            HostEnvironment(Environments.Production));

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal(1, nextCallCount);
    }

    private static WhatsAppGatewayWebhookAuthenticationMiddleware Middleware(Action onNext) => new(
        context =>
        {
            onNext();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

    private static DefaultHttpContext WebhookContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/webhooks/whatsapp/message";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new WhatsAppGatewayAuthenticatedAttribute()),
            "WhatsApp gateway endpoint"));
        return context;
    }

    private static IConfiguration Configuration(string? secret = null)
    {
        var values = secret is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>
            {
                ["WhatsAppGateway:WebhookSecret"] = secret
            };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IHostEnvironment HostEnvironment(string environmentName) => new HostingEnvironment
    {
        EnvironmentName = environmentName,
        ApplicationName = "Advertising.UnitTests",
        ContentRootPath = AppContext.BaseDirectory
    };

    private static async Task<string?> ResponseCode(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.GetProperty("code").GetString();
    }
}
