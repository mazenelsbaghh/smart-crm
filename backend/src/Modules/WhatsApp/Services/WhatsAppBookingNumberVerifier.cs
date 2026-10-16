using System.Net.Http.Json;
using System.Text.Json;
using Modules.Advertising.Services;

namespace Modules.WhatsApp.Services;

public enum WhatsAppNumberVerification { Registered, NotRegistered, Unavailable }

public interface IWhatsAppBookingNumberVerifier
{
    Task<WhatsAppNumberVerification> VerifyAsync(Guid projectId, string phone, CancellationToken cancellationToken = default);
}

public sealed class WhatsAppBookingNumberVerifier(
    WhatsAppAccountService accounts, WhatsAppGatewaySessionClient sessions,
    IHttpClientFactory clients, IConfiguration configuration) : IWhatsAppBookingNumberVerifier
{
    public async Task<WhatsAppNumberVerification> VerifyAsync(Guid projectId, string phone, CancellationToken cancellationToken = default)
    {
        using var http = clients.CreateClient(nameof(WhatsAppBookingNumberVerifier));
        var gatewayUrl = (configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000").TrimEnd('/');
        foreach (var account in await accounts.ListAsync(projectId, cancellationToken))
        {
            var session = await sessions.GetAsync(projectId, account.Id, cancellationToken);
            if (!session.Connected || !session.ConnectedAt.HasValue) continue;
            try
            {
                using var response = await http.PostAsJsonAsync($"{gatewayUrl}/api/whatsapp/number/check", new
                {
                    projectId, whatsappAccountId = account.Id, phone, expectedConnectedAt = session.ConnectedAt
                }, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (result.RootElement.TryGetProperty("exists", out var exists) && exists.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return exists.GetBoolean() ? WhatsAppNumberVerification.Registered : WhatsAppNumberVerification.NotRegistered;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or TaskCanceledException or JsonException) { }
        }
        return WhatsAppNumberVerification.Unavailable;
    }
}
