using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ConversionLedgerTests
{
    [Fact]
    public void Signature_is_stable_and_tampering_is_rejected()
    {
        const string secret = "test-only-secret"; const long timestamp = 1786957200; const string body = "{\"externalEventId\":\"pay-1\"}";
        var signature = "v1=" + ConversionSecurity.Sign(secret, timestamp, body);
        Assert.True(ConversionSecurity.Verify(secret, timestamp, body, signature));
        Assert.False(ConversionSecurity.Verify(secret, timestamp, body + "x", signature));
    }

    [Theory]
    [InlineData(ConsentState.Granted, "a@example.com", null, true)]
    [InlineData(ConsentState.Denied, "a@example.com", null, false)]
    [InlineData(ConsentState.Unknown, null, "+20100", false)]
    public void Match_data_requires_explicit_consent(ConsentState consent, string? email, string? phone, bool expected) =>
        Assert.Equal(expected, ConversionSecurity.CanUseMatchData(consent, email, phone));

    [Theory]
    [InlineData("Refund")]
    [InlineData("Absent")]
    [InlineData("Churn")]
    public void Negative_business_outcomes_are_corrections(string eventType) => Assert.True(ConversionSecurity.IsCorrection(eventType));
}
