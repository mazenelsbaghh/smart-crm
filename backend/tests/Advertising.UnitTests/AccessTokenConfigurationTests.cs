using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AccessTokenConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("short-key")]
    [InlineData("a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long")]
    [InlineData("change-me-before-production-signing-secret")]
    public void Missing_or_public_signing_keys_cannot_issue_access_tokens(string? secret)
    {
        var configuration = Configuration(secret);
        Assert.Throws<InvalidOperationException>(() => new JwtService(configuration));
    }

    [Fact]
    public void Access_token_is_short_lived_even_when_legacy_configuration_requests_a_year()
    {
        var token = new JwtService(Configuration("independently-generated-test-signing-key-123456789"))
            .GenerateToken(new(Guid.NewGuid(), "test@example.test", "Agent", Guid.NewGuid(), Guid.NewGuid()));
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.InRange(decoded.ValidTo - decoded.ValidFrom, TimeSpan.FromMinutes(14.9), TimeSpan.FromMinutes(15.1));
    }

    private static IConfiguration Configuration(string? secret) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["JWT:Secret"] = secret,
            ["JWT:ExpiryInMinutes"] = "525600" }).Build();
}
