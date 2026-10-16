using System.Text;

namespace Shared.Security;

public static class JwtSettings
{
    public const int LifetimeMinutes = 15;

    public static string SigningSecret(IConfiguration configuration)
    {
        var secret = configuration["JWT:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32
            || secret == "a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long"
            || secret.Contains("change-me", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("JWT:Secret must contain an independently generated secret of at least 32 bytes.");
        return secret;
    }
}
