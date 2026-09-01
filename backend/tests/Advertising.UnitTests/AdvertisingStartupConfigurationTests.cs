using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingStartupConfigurationTests
{
    [Fact]
    public void Defaults_are_no_spend_and_pin_the_supported_graph_version()
    {
        var options = new AdvertisingOptions();

        Assert.False(options.Enabled);
        Assert.False(options.AllowRealActivation);
        Assert.False(options.Meta.UseMock);
        Assert.Equal("v26.0", options.Meta.GraphApiVersion);
        Assert.Equal(1, options.Tracking.PolicyVersion);
    }

    [Theory]
    [InlineData("v25.0")]
    [InlineData("26.0")]
    [InlineData("v27.0")]
    public void Enabled_advertising_rejects_an_unsupported_graph_version(string version)
    {
        var options = ValidProductionOptions(version);

        var errors = AdvertisingStartupValidator.Validate(options, "Production");

        Assert.Contains(errors, error => error.Code == "ADS_GRAPH_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void Production_rejects_the_fake_provider()
    {
        var options = ValidProductionOptions(useMock: true);

        var errors = AdvertisingStartupValidator.Validate(options, "Production");

        Assert.Contains(errors, error => error.Code == "ADS_META_MOCK_NOT_ALLOWED");
    }

    [Fact]
    public void Enabled_production_requires_complete_provider_credentials()
    {
        var errors = AdvertisingStartupValidator.Validate(
            new AdvertisingOptions { Enabled = true },
            "Production");

        Assert.Contains(errors, error => error.Code == "ADS_META_APP_ID_MISSING");
        Assert.Contains(errors, error => error.Code == "ADS_META_APP_SECRET_MISSING");
        Assert.Contains(errors, error => error.Code == "ADS_META_OAUTH_REDIRECT_INVALID");
    }

    [Fact]
    public void Development_allows_the_fake_provider_without_real_credentials()
    {
        var options = new AdvertisingOptions
        {
            Enabled = true,
            Meta = new MetaOptions { UseMock = true }
        };

        var errors = AdvertisingStartupValidator.Validate(options, "Development");

        Assert.Empty(errors);
    }

    private static AdvertisingOptions ValidProductionOptions(
        string graphApiVersion = "v26.0",
        bool useMock = false) => new()
    {
        Enabled = true,
        Meta = new MetaOptions
        {
            UseMock = useMock,
            GraphApiVersion = graphApiVersion,
            AppId = "app-id",
            AppSecret = "app-secret",
            OAuthRedirectUri = "https://example.test/api/ad-manager/meta/oauth/callback"
        }
    };
}
