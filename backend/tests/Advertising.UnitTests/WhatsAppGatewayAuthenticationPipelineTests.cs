using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modules.Conversations.Services;
using Modules.Media.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppGatewayAuthenticationPipelineTests
{
    private const string WebhookSecret = "synthetic-pipeline-gateway-secret";
    private const string GatewaySecretHeader = "X-WhatsApp-Gateway-Secret";

    private static readonly GatewayRoute[] GatewayRoutes =
    [
        new("/api/webhooks/whatsapp/message", GatewayRequestKind.Message),
        new("/api/webhooks/whatsapp/message/", GatewayRequestKind.Message),
        new($"/api/projects/{Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")}/assets/upload", GatewayRequestKind.Media),
        new($"/api/projects/{Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")}/assets/upload/", GatewayRequestKind.Media)
    ];

    [Fact]
    public async Task Actual_gateway_pipeline_enforces_authentication_for_message_and_media_routes()
    {
        await using var factory = new GatewayApplicationFactory();
        using var client = factory.CreateClient();

        await AssertRoutesReturn(
            client,
            suppliedSecret: null,
            HttpStatusCode.ServiceUnavailable,
            "WHATSAPP_GATEWAY_AUTH_NOT_CONFIGURED");

        factory.Services.GetRequiredService<IConfiguration>()["WhatsAppGateway:WebhookSecret"] = WebhookSecret;

        await AssertRoutesReturn(
            client,
            "wrong-synthetic-pipeline-secret",
            HttpStatusCode.Unauthorized,
            "WHATSAPP_GATEWAY_AUTH_INVALID");
        await AssertRoutesReturn(client, WebhookSecret, HttpStatusCode.BadRequest);
    }

    private static async Task AssertRoutesReturn(
        HttpClient client,
        string? suppliedSecret,
        HttpStatusCode expectedStatus,
        string? expectedCode = null)
    {
        foreach (var route in GatewayRoutes)
        {
            using var request = Request(route);
            if (suppliedSecret is not null)
                request.Headers.Add(GatewaySecretHeader, suppliedSecret);

            using var response = await client.SendAsync(request);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedCode is not null)
                Assert.Equal(expectedCode, await ResponseCode(response));
        }
    }

    private static HttpRequestMessage Request(GatewayRoute route)
    {
        HttpContent content = route.Kind == GatewayRequestKind.Message
            ? new StringContent("{}", Encoding.UTF8, "application/json")
            : new MultipartFormDataContent();
        return new HttpRequestMessage(HttpMethod.Post, route.Path) { Content = content };
    }

    private static async Task<string?> ResponseCode(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private sealed class GatewayApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Advertising:Enabled"] = "false",
                    ["Advertising:Meta:UseMock"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;Port=1;Database=gateway_auth_test;Username=test;Password=test;Timeout=1",
                    ["WhatsAppGateway:WebhookSecret"] = string.Empty
                }));
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType?.FullName !=
                            "Microsoft.AspNetCore.Hosting.GenericWebHostService")
                    .ToArray())
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"gateway-auth-{Guid.NewGuid():N}"));

                services.RemoveAll<IEventBus>();
                services.AddSingleton<IEventBus, InMemoryEventBus>();
                services.Replace(ServiceDescriptor.Singleton(
                    NoOpProxy.Create<IRecurringJobManager>()));
                services.Replace(ServiceDescriptor.Singleton(
                    NoOpProxy.Create<IBackgroundJobClient>()));
                services.Replace(ServiceDescriptor.Singleton(
                    RedisConnectionProxy.Create()));
                services.Replace(ServiceDescriptor.Scoped(
                    _ => NoOpProxy.Create<IMessageAggregator>()));
                services.Replace(ServiceDescriptor.Scoped(
                    _ => NoOpProxy.Create<IAssignmentEngine>()));
                services.Replace(ServiceDescriptor.Scoped(
                    _ => NoOpProxy.Create<IAssetService>()));
                services.Replace(ServiceDescriptor.Scoped(
                    _ => NoOpProxy.Create<IAdvertisingReferralProtector>()));
            });
        }
    }

    private class NoOpProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, NoOpProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            DefaultValue(targetMethod?.ReturnType);

        protected static object? DefaultValue(Type? returnType)
        {
            if (returnType is null || returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            if (returnType == typeof(ValueTask)) return ValueTask.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [resultType.IsValueType ? Activator.CreateInstance(resultType) : null]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class RedisConnectionProxy : NoOpProxy
    {
        public static IConnectionMultiplexer Create() =>
            DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? NoOpProxy.Create<IDatabase>()
                : DefaultValue(targetMethod?.ReturnType);
    }

    private sealed record GatewayRoute(string Path, GatewayRequestKind Kind);

    private enum GatewayRequestKind
    {
        Message,
        Media
    }
}
