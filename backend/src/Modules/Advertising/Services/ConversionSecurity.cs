using System.Security.Cryptography;
using System.Text;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public static class ConversionSecurity
{
    public static string Sign(string secret, long timestamp, string body) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
    public static bool Verify(string secret, long timestamp, string body, string signature)
    {
        var expected = Sign(secret, timestamp, body); var supplied = signature.StartsWith("v1=", StringComparison.OrdinalIgnoreCase) ? signature[3..].ToLowerInvariant() : string.Empty;
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(expected));
    }
    public static bool CanUseMatchData(ConsentState consent, string? email, string? phone) => consent == ConsentState.Granted && (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone));
    public static bool IsCorrection(string eventType) => eventType is "Refund" or "Cancellation" or "Chargeback" or "Absent" or "Churn";
}
