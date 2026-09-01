using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Jobs;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ConversionConsentTests
{
    [Theory]
    [InlineData(ConsentState.Granted, null, true)]
    [InlineData(ConsentState.Denied, "contract", false)]
    [InlineData(ConsentState.Unknown, "contract", false)]
    [InlineData(ConsentState.NotRequired, "legitimate_interest", true)]
    [InlineData(ConsentState.NotRequired, null, false)]
    public void Delivery_rechecks_current_consent_and_legal_basis(ConsentState state, string? legalBasis, bool expected) =>
        Assert.Equal(expected, ConversionConsentPolicy.CanDeliver(state, legalBasis));

    [Fact]
    public void Only_in_thread_ctwa_events_use_business_messaging_capi()
    {
        Assert.Equal(ConversionDeliveryChannel.MetaBusinessMessaging,
            ConversionAttributionPolicy.Route("MessagingThread", true));
        Assert.Equal(ConversionDeliveryChannel.InternalOnly,
            ConversionAttributionPolicy.Route("MessagingThread", false));
        Assert.Equal(ConversionDeliveryChannel.WebConversionsApi,
            ConversionAttributionPolicy.Route("Website", true));
        Assert.Null(WhatsAppJourneyEventMapper.Map("ConversationStarted").MetaMessagingEvent);
        Assert.Equal("Purchase", WhatsAppJourneyEventMapper.Map("Purchase").MetaMessagingEvent);
    }

    [Fact]
    public void Latest_consent_projection_overrides_stale_conversion_consent()
    {
        var revoked = new CustomerAdvertisingConsentProjection
        {
            ConsentState = "Denied", LegalBasis = string.Empty, EffectiveAtUtc = DateTime.UtcNow
        };

        var effective = ConversionConsentPolicy.ResolveCurrent(ConsentState.Granted, "contract", revoked);

        Assert.Equal(ConsentState.Denied, effective.State);
        Assert.False(ConversionConsentPolicy.CanDeliver(effective.State, effective.LegalBasis));
    }

    [Fact]
    public async Task Delivery_job_rechecks_revocation_before_any_provider_call()
    {
        var setup = await SetupDeliveryAsync(HttpStatusCode.OK);
        setup.Db.CustomerAdvertisingConsentProjections.Add(new CustomerAdvertisingConsentProjection
        {
            ProjectId = setup.ProjectId, CustomerId = setup.CustomerId, ConsentState = "Denied",
            EffectiveAtUtc = DateTime.UtcNow, ConsentVersion = 2
        });
        await setup.Db.SaveChangesAsync();

        await setup.Job.RunAsync();

        var delivery = await setup.Db.AdvertisingConversionDeliveries.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ConversionDeliveryState.Suppressed, delivery.State);
        Assert.Equal("ADS_CONSENT_NOT_ELIGIBLE", delivery.SuppressionReason);
        Assert.Equal(0, setup.Handler.CallCount);
    }

    [Fact]
    public async Task Failed_delivery_is_scheduled_and_not_retried_before_due_time()
    {
        var setup = await SetupDeliveryAsync(HttpStatusCode.ServiceUnavailable);

        await setup.Job.RunAsync();
        await setup.Job.RunAsync();

        var delivery = await setup.Db.AdvertisingConversionDeliveries.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ConversionDeliveryState.RetryScheduled, delivery.State);
        Assert.True(delivery.NextAttemptAtUtc > DateTime.UtcNow);
        Assert.Single(await setup.Db.AdvertisingConversionDeliveryAttempts.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(1, setup.Handler.CallCount);
    }

    private static async Task<DeliverySetup> SetupDeliveryAsync(HttpStatusCode statusCode)
    {
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"delivery-{Guid.NewGuid():N}"));
        var provider = DataProtectionProvider.Create(directory);
        var vault = new AdvertisingSecretVault(provider);
        IAdvertisingReferralProtector referrals = new AdvertisingReferralProtector(provider);
        var connection = new AdvertisingConnection
        {
            ProjectId = projectId, ProtectedAccessToken = vault.Protect("token"), State = AdvertisingConnectionState.Ready
        };
        var destination = new AuthorizedWhatsAppDestination
        {
            ProjectId = projectId, ConnectionId = connection.Id, WabaExternalId = "waba-1",
            PhoneNumberExternalId = "phone-1", DatasetExternalId = "dataset-1", State = AuthorizedDestinationState.Eligible
        };
        var touch = new AdvertisingAttributionTouch
        {
            ProjectId = projectId, DestinationId = destination.Id, ConversationId = Guid.NewGuid(),
            Method = "CtwaClid", ProtectedCtwaClid = referrals.ProtectIdentifier("click-1"), TouchedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        var conversion = new CanonicalConversion
        {
            ProjectId = projectId, CanonicalKey = "crm:purchase-1", EventType = "Purchase",
            OccurredAtUtc = DateTime.UtcNow, CustomerReference = customerId.ToString("N"), CurrentValue = 100,
            Currency = "EGP", ConsentState = ConsentState.Granted, LegalBasis = "Consent",
            State = ConversionState.Verified, AttributionState = AttributionState.Attributed,
            AttributionTouchId = touch.Id
        };
        db.AddRange(connection, destination, touch, conversion);
        await db.SaveChangesAsync();
        var handler = new StatusHandler(statusCode);
        var client = new MetaBusinessMessagingClient(new HttpClient(handler)
            { BaseAddress = new Uri("https://graph.facebook.com/v26.0/") });
        return new(projectId, customerId, db, handler, new ConversionDeliveryJob(db, client, vault, referrals));
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(statusCode == HttpStatusCode.OK ? "{\"events_received\":1}" : "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record DeliverySetup(Guid ProjectId, Guid CustomerId, AppDbContext Db,
        StatusHandler Handler, ConversionDeliveryJob Job);
}
