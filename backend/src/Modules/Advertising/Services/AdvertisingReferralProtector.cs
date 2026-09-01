using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Shared.Security;

namespace Modules.Advertising.Services;

public sealed class AdvertisingReferralProtector(IDataProtectionProvider provider) : IAdvertisingReferralProtector
{
    private readonly IDataProtector _identifier = provider.CreateProtector("Advertising.Referral.CtwaClid.v1");
    private readonly IDataProtector _inbound = provider.CreateProtector("WhatsApp.Inbound.Referral.v1");

    public string ProtectIdentifier(string rawIdentifier) => _identifier.Protect(rawIdentifier);
    public string UnprotectForBusinessMessaging(string protectedIdentifier) => _identifier.Unprotect(protectedIdentifier);
    public string ProtectInboundJson(string rawJson) => _inbound.Protect(rawJson);
    public string UnprotectInboundJson(string protectedJson) => _inbound.Unprotect(protectedJson);
    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
