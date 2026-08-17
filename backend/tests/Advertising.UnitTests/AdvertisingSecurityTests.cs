using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingSecurityTests
{
    [Fact]
    public void LogSanitizerRedactsJsonAndKeyValueSecrets()
    {
        var sanitized = AdvertisingLogSanitizer.Redact("{\"email\":\"buyer@example.com\",\"match_data\":\"hash\"} access_token=token");

        Assert.DoesNotContain("buyer@example.com", sanitized);
        Assert.DoesNotContain("hash", sanitized);
        Assert.DoesNotContain("token", sanitized.Replace("access_token", string.Empty, StringComparison.OrdinalIgnoreCase));
    }
}
