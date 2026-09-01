namespace Shared.Security;

public interface IAdvertisingReferralProtector
{
    string ProtectIdentifier(string rawIdentifier);
    string UnprotectForBusinessMessaging(string protectedIdentifier);
    string ProtectInboundJson(string rawJson);
    string UnprotectInboundJson(string protectedJson);
    string Hash(string value);
}
