using Microsoft.AspNetCore.DataProtection;

namespace Modules.QuranChallenge.Services;

public sealed class YouTubeTokenVault
{
    private readonly IDataProtector _protector;

    public YouTubeTokenVault(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("QuranChallenge.YouTube.RefreshToken.v1");
    }

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string Unprotect(string protectedRefreshToken) => _protector.Unprotect(protectedRefreshToken);
}
