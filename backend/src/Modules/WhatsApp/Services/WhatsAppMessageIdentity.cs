using System.Security.Cryptography;
using System.Text;

namespace Modules.WhatsApp.Services;

public static class WhatsAppMessageIdentity
{
    public static Guid Outgoing(
        Guid projectId,
        Guid whatsAppAccountId,
        string providerMessageId)
    {
        var identity = $"whatsapp-outgoing:{projectId:N}:{whatsAppAccountId:N}:{providerMessageId.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(hash.AsSpan(0, 16));
    }
}
