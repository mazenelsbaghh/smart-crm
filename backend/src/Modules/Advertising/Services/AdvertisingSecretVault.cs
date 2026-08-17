using Microsoft.AspNetCore.DataProtection;

namespace Modules.Advertising.Services;

public sealed class AdvertisingSecretVault
{
    private readonly IDataProtector _protector;

    public AdvertisingSecretVault(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("Advertising.Meta.Credentials.v1");

    public string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Secret is required.", nameof(value));
        return _protector.Protect(value);
    }

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
