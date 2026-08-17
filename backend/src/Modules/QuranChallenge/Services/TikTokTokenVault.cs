using Microsoft.AspNetCore.DataProtection;

namespace Modules.QuranChallenge.Services;

public sealed class TikTokTokenVault
{
    private readonly IDataProtector _protector;

    public TikTokTokenVault(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("QuranChallenge.TikTok.Tokens.v1");
    }

    public string Protect(string token) => _protector.Protect(token);
    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);
}
