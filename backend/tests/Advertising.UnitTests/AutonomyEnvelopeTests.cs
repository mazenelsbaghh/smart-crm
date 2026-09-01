using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AutonomyEnvelopeTests
{
    [Fact]
    public void Hard_audience_controls_and_multi_period_caps_are_required()
    {
        var valid = new AutonomyEnvelopeDefinition(100, 2000, "Monthly", "EGP", ["EG"], [], 21, ["ar"], ["customers"], "Africa/Cairo");
        Assert.True(AutonomyEnvelopePolicy.Validate(valid).IsValid);

        Assert.Contains("ADS_PERIOD_CAP_BELOW_DAILY_CAP", AutonomyEnvelopePolicy.Validate(valid with { PeriodCap = 50 }).Errors);
        Assert.Contains("ADS_HARD_LOCATION_REQUIRED", AutonomyEnvelopePolicy.Validate(valid with { IncludedCountries = [] }).Errors);
    }

    [Fact]
    public void Empty_language_constraint_keeps_advantage_plus_audience_broad()
    {
        var definition = new AutonomyEnvelopeDefinition(100, 2000, "Monthly", "EGP", ["EG"], [], 18, [], [], "Africa/Cairo");

        Assert.True(AutonomyEnvelopePolicy.Validate(definition).IsValid);
    }

    [Theory]
    [InlineData("", "Africa/Cairo", "ADS_CURRENCY_INVALID")]
    [InlineData(null, "Africa/Cairo", "ADS_CURRENCY_INVALID")]
    [InlineData("egp", "Africa/Cairo", "ADS_CURRENCY_INVALID")]
    [InlineData("€€€", "Africa/Cairo", "ADS_CURRENCY_INVALID")]
    [InlineData("EGP", "", "ADS_TIMEZONE_INVALID")]
    [InlineData("EGP", null, "ADS_TIMEZONE_INVALID")]
    [InlineData("EGP", "Mars/Olympus", "ADS_TIMEZONE_INVALID")]
    public void Invalid_reporting_contract_is_rejected(string? currency, string? timezone, string expectedError)
    {
        var definition = new AutonomyEnvelopeDefinition(
            100, 2000, "Monthly", currency!, ["EG"], [], 18, ["ar"], [], timezone!);

        Assert.Contains(expectedError, AutonomyEnvelopePolicy.Validate(definition).Errors);
    }

    [Fact]
    public void Suggested_audience_may_narrow_but_never_widen_authority()
    {
        var authority = new AudienceAuthority(["EG", "SA"], 21, ["ar", "en"]);
        Assert.True(AutonomyEnvelopePolicy.IsWithinAuthority(authority, new(["EG"], 25, ["ar"])));
        Assert.False(AutonomyEnvelopePolicy.IsWithinAuthority(authority, new(["EG", "US"], 18, ["ar"])));
    }
}
