using Microsoft.Extensions.Options;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingRolloutSafetyTests
{
    [Fact]
    public void Financial_readiness_requires_a_fresh_healthy_snapshot_and_no_open_incident()
    {
        var now = DateTime.UtcNow;
        var healthy = new Modules.Advertising.Domain.TrackingHealthSnapshot
        {
            State = Modules.Advertising.Domain.TrackingHealthState.Healthy,
            EvaluatedAtUtc = now.AddMinutes(-5)
        };

        Assert.True(AdvertisingOperationalPolicy.HasFreshHealthyTracking(healthy, false, now, TimeSpan.FromMinutes(30)));
        Assert.False(AdvertisingOperationalPolicy.HasFreshHealthyTracking(null, false, now, TimeSpan.FromMinutes(30)));
        Assert.False(AdvertisingOperationalPolicy.HasFreshHealthyTracking(healthy, true, now, TimeSpan.FromMinutes(30)));
        healthy.EvaluatedAtUtc = now.AddMinutes(-31);
        Assert.False(AdvertisingOperationalPolicy.HasFreshHealthyTracking(healthy, false, now, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task Disabled_rollout_blocks_provider_creation_before_http()
    {
        var client = CreateClient(new AdvertisingOptions());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CreateCampaignPausedAsync("token", "act_1", "plan", "OUTCOME_ENGAGEMENT", default));

        Assert.StartsWith("ADS_ADVERTISING_DISABLED", error.Message);
    }

    [Fact]
    public async Task Enabled_rollout_still_blocks_real_activation_until_the_canary_gate()
    {
        var client = CreateClient(new AdvertisingOptions { Enabled = true });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ActivateManagedAdHierarchyAsync("token", new MetaAdHierarchy("campaign", "adset", "ad"), default));

        Assert.StartsWith("ADS_REAL_ACTIVATION_DISABLED", error.Message);
    }

    [Fact]
    public async Task Automatic_campaign_archival_is_forbidden()
    {
        var client = CreateClient(new AdvertisingOptions { Enabled = true });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ArchiveCampaignAsync("token", "campaign", default));

        Assert.StartsWith("ADS_PROVIDER_DELETE_FORBIDDEN", error.Message);
    }

    private static MetaAdsClient CreateClient(AdvertisingOptions options) =>
        new(new HttpClient(new UnexpectedHttpHandler())
        {
            BaseAddress = new Uri("https://graph.facebook.com/v26.0/")
        }, Options.Create(options));

    private sealed class UnexpectedHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException($"Provider HTTP should not be called: {request.RequestUri}");
    }
}
