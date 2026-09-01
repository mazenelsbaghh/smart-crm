using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Advertising.API;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingReadPathPerformanceTests
{
    [Theory]
    [InlineData(ReadEndpoint.Readiness, WhatsAppIntegrationMode.CloudApi)]
    [InlineData(ReadEndpoint.Readiness, WhatsAppIntegrationMode.BaileysObservedExperimental)]
    [InlineData(ReadEndpoint.Overview, WhatsAppIntegrationMode.CloudApi)]
    [InlineData(ReadEndpoint.Overview, WhatsAppIntegrationMode.BaileysObservedExperimental)]
    public async Task August_2026_read_endpoints_keep_expired_capability_provider_free_and_read_only(
        ReadEndpoint endpoint,
        WhatsAppIntegrationMode integrationMode)
    {
        var observation = await InvokeReadEndpointAsync(endpoint, integrationMode);

        Assert.IsType<OkObjectResult>(observation.Response);
        Assert.Equal(0, observation.MetaRequestCount);
        Assert.Equal(0, observation.GatewayRequestCount);
        Assert.Equal(0, observation.SaveAttemptCount);
        Assert.False(observation.HasTrackedChanges);
    }

    [Fact]
    public async Task Activation_refresh_checks_live_gateway_and_fails_closed_on_disconnect()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(
            dbOptions,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        await SeedExpiredCapabilityAsync(
            db,
            projectId,
            vault,
            WhatsAppIntegrationMode.BaileysObservedExperimental);
        var capability = await db.AdvertisingCapabilitySnapshots.SingleAsync();
        capability.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using var metaBoundary = new CountingHttpHandler();
        using var metaHttp = new HttpClient(metaBoundary)
        {
            BaseAddress = new Uri("https://graph.facebook.com/v26.0/")
        };
        using var gatewayBoundary = new CountingHttpHandler(
            HttpStatusCode.OK,
            "{\"status\":\"Disconnected\",\"phoneNumber\":null}");
        using var gatewayHttp = new HttpClient(gatewayBoundary);
        var gatewayConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://whatsapp-gateway:3000"
            })
            .Build();
        var service = new AdvertisingReadinessService(
            db,
            new MetaCapabilityClient(new MetaGraphClient(metaHttp)),
            new WhatsAppGatewaySessionClient(gatewayHttp, gatewayConfiguration),
            vault,
            new AdvertisingAuditService(db),
            new Modules.WhatsApp.Services.WhatsAppAccountService(db));

        var readiness = await service.RefreshAsync(projectId);

        var destinationReadiness = readiness.Items.Single(item => item.Key == "destination");
        Assert.False(readiness.Ready);
        Assert.False(destinationReadiness.Ready);
        Assert.Equal("ADS_GATEWAY_NOT_CONNECTED", destinationReadiness.Reason);
        Assert.Equal(0, metaBoundary.RequestCount);
        Assert.Equal(1, gatewayBoundary.RequestCount);
    }

    [Fact]
    public async Task Overview_keeps_continuing_spend_visible_when_succeeded_pause_has_active_delivery()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(
            dbOptions,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        await SeedExpiredCapabilityAsync(
            db,
            projectId,
            vault,
            WhatsAppIntegrationMode.CloudApi);
        var connectionId = await db.AdvertisingConnections.Select(connection => connection.Id).SingleAsync();
        var ownership = new ManagedOwnershipRecord
        {
            ProjectId = projectId,
            ConnectionId = connectionId,
            ProviderCampaignExternalId = "campaign-1",
            OwnershipKind = ManagedOwnershipKind.AutopilotCreated
        };
        var disableRequest = new AutopilotDisableRequest
        {
            ProjectId = projectId,
            Mode = AutopilotDisableMode.PauseManaged,
            RequestedByUserId = Guid.NewGuid(),
            RequestedAtUtc = DateTime.UtcNow,
            State = "PausingManaged"
        };
        db.AddRange(
            ownership,
            new ManagedAdvertisement
            {
                ProjectId = projectId,
                ConnectionId = connectionId,
                OwnershipRecordId = ownership.Id,
                CampaignExternalId = "campaign-1",
                AdExternalId = "ad-1",
                ConfiguredStatus = ManagedDeliveryState.Active,
                EffectiveStatus = "ACTIVE"
            },
            disableRequest,
            new ExecutionCommand
            {
                ProjectId = projectId,
                DecisionId = Guid.NewGuid(),
                IdempotencyKey = $"disable:{disableRequest.Id:N}:Ad:ad-1",
                CommandType = "PauseAd",
                TargetExternalId = "ad-1",
                RequestFingerprint = "overview-regression",
                State = CommandState.Succeeded,
                CompletedAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var readiness = new AdvertisingReadinessService(
            db,
            capabilities: null!,
            gateway: null!,
            vault: null!,
            new AdvertisingAuditService(db),
            new Modules.WhatsApp.Services.WhatsAppAccountService(db));

        var response = await InvokeOverviewAsync(
            projectId,
            new ProjectAuthorizationService(),
            db,
            readiness);

        var ok = Assert.IsType<OkObjectResult>(response);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("NeedsAttention", json.RootElement.GetProperty("disableState").GetString());
        Assert.True(json.RootElement.GetProperty("pauseOngoing").GetBoolean());
        Assert.True(json.RootElement.GetProperty("continuingSpend").GetBoolean());
        Assert.True(json.RootElement.GetProperty("deliveryMayContinue").GetBoolean());
    }

    private static async Task<ReadObservation> InvokeReadEndpointAsync(
        ReadEndpoint endpoint,
        WhatsAppIntegrationMode integrationMode)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var saveAttempts = new SaveAttemptInterceptor();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(saveAttempts)
            .Options;
        await using var db = new AppDbContext(
            dbOptions,
            tenant,
            new ServiceCollection().BuildServiceProvider());

        var vault = new AdvertisingSecretVault(new EphemeralDataProtectionProvider());
        await SeedExpiredCapabilityAsync(db, projectId, vault, integrationMode);
        db.ChangeTracker.Clear();
        saveAttempts.Reset();

        using var metaBoundary = new CountingHttpHandler();
        using var metaHttp = new HttpClient(metaBoundary)
        {
            BaseAddress = new Uri("https://graph.facebook.com/v26.0/")
        };
        using var gatewayBoundary = new CountingHttpHandler();
        using var gatewayHttp = new HttpClient(gatewayBoundary);
        var gatewayConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://whatsapp-gateway:3000"
            })
            .Build();
        var readiness = new AdvertisingReadinessService(
            db,
            new MetaCapabilityClient(new MetaGraphClient(metaHttp)),
            new WhatsAppGatewaySessionClient(gatewayHttp, gatewayConfiguration),
            vault,
            new AdvertisingAuditService(db),
            new Modules.WhatsApp.Services.WhatsAppAccountService(db));
        var authorization = new ProjectAuthorizationService();

        var response = endpoint switch
        {
            ReadEndpoint.Readiness => await InvokeReadinessAsync(
                projectId, authorization, db, readiness),
            ReadEndpoint.Overview => await InvokeOverviewAsync(
                projectId, authorization, db, readiness),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null)
        };

        return new(
            response,
            metaBoundary.RequestCount,
            gatewayBoundary.RequestCount,
            saveAttempts.AttemptCount,
            db.ChangeTracker.HasChanges());
    }

    private static async Task SeedExpiredCapabilityAsync(
        AppDbContext db,
        Guid projectId,
        AdvertisingSecretVault vault,
        WhatsAppIntegrationMode integrationMode)
    {
        var connection = new AdvertisingConnection
        {
            ProjectId = projectId,
            State = AdvertisingConnectionState.Ready,
            ProtectedAccessToken = vault.Protect("test-access-token"),
            AdAccountExternalId = "act_1",
            PageExternalId = "page_1",
            WabaExternalId = "waba_1",
            DatasetExternalId = "dataset_1",
            AccountCurrency = "EGP",
            AccountTimezoneIana = "Africa/Cairo",
            WhatsAppIntegrationMode = integrationMode
        };
        var destination = new AuthorizedWhatsAppDestination
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            WabaExternalId = "waba_1",
            PhoneNumberExternalId = "phone_1",
            PageExternalId = "page_1",
            DatasetExternalId = "dataset_1",
            ReceivingIdentityExternalId = "phone_1",
            WhatsAppIntegrationMode = integrationMode,
            MessagingState = "Ready",
            State = AuthorizedDestinationState.Eligible,
            LastValidatedAtUtc = DateTime.UtcNow.AddHours(-7)
        };
        var capability = new AdvertisingCapabilitySnapshot
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            DestinationId = destination.Id,
            State = AdvertisingCapabilityState.Healthy,
            CheckedAtUtc = DateTime.UtcNow.AddHours(-7),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1),
            ObjectivesJson = "[\"OUTCOME_ENGAGEMENT\"]",
            OptimizationGoalsJson = "[\"CONVERSATIONS\"]",
            PlacementEligibilityJson = "{\"automatic\":true,\"whatsappDestinationEligible\":true}"
        };
        destination.CapabilitySnapshotId = capability.Id;
        var projectContext = new ProjectAdvertisingContextProjection
        {
            ProjectId = projectId,
            ReportingTimezoneIana = "Africa/Cairo",
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedFromEventId = Guid.NewGuid(),
            SourceVersion = 1
        };

        db.AddRange(connection, destination, capability, projectContext);
        await db.SaveChangesAsync();
    }

    private static async Task<IActionResult> InvokeReadinessAsync(
        Guid projectId,
        IProjectAuthorizationService authorization,
        AppDbContext db,
        AdvertisingReadinessService readiness)
    {
        var controller = new AdvertisingConnectionController(
            authorization,
            db,
            readiness,
            null!,
            null!,
            null!)
        {
            ControllerContext = AuthenticatedContext(projectId)
        };
        return await controller.GetReadiness(projectId, CancellationToken.None);
    }

    private static async Task<IActionResult> InvokeOverviewAsync(
        Guid projectId,
        IProjectAuthorizationService authorization,
        AppDbContext db,
        AdvertisingReadinessService readiness)
    {
        var controller = new AdvertisingOperationsController(
            authorization,
            db,
            readiness,
            new BudgetAllocator(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            Options.Create(new AdvertisingOptions()))
        {
            ControllerContext = AuthenticatedContext(projectId)
        };
        return await controller.Overview(projectId, CancellationToken.None);
    }

    private static ControllerContext AuthenticatedContext(Guid projectId)
    {
        var identity = new ClaimsIdentity(
            [new Claim("ProjectId", projectId.ToString())],
            authenticationType: "RegressionTest");
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    public enum ReadEndpoint
    {
        Readiness,
        Overview
    }

    private sealed record ReadObservation(
        IActionResult Response,
        int MetaRequestCount,
        int GatewayRequestCount,
        int SaveAttemptCount,
        bool HasTrackedChanges);

    private sealed class CountingHttpHandler(
        HttpStatusCode responseStatus = HttpStatusCode.ServiceUnavailable,
        string responseContent = "{}") : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(responseStatus)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SaveAttemptInterceptor : SaveChangesInterceptor
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public void Reset() => Volatile.Write(ref _attemptCount, 0);

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            Interlocked.Increment(ref _attemptCount);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attemptCount);
            return ValueTask.FromResult(result);
        }
    }
}
