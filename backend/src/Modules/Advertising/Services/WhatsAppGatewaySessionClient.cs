using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modules.WhatsApp.Services;

namespace Modules.Advertising.Services;

public sealed record WhatsAppGatewaySessionStatus(string Status, string? PhoneNumber, string? Error)
{
    [JsonPropertyName("connectedAt")]
    public DateTimeOffset? ConnectedAt { get; init; }

    public bool Connected => string.Equals(Status, "Connected", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(PhoneNumber);
}

public sealed class WhatsAppGatewaySessionClient(HttpClient httpClient, IConfiguration configuration)
{
    public Task<WhatsAppGatewaySessionStatus> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetAsync(projectId, null, cancellationToken);

    public Task<WhatsAppGatewaySessionStatus> GetAsync(
        Guid projectId,
        Guid whatsappAccountId,
        CancellationToken cancellationToken = default) =>
        GetAsync(
            projectId,
            WhatsAppAccountService.GatewayAccountId(projectId, whatsappAccountId),
            cancellationToken);

    private async Task<WhatsAppGatewaySessionStatus> GetAsync(
        Guid projectId,
        Guid? gatewayAccountId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchAsync(projectId, gatewayAccountId, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException
            or NotSupportedException
            or UriFormatException)
        {
            return Unavailable(exception);
        }
    }

    private async Task<WhatsAppGatewaySessionStatus> FetchAsync(
        Guid projectId,
        Guid? gatewayAccountId,
        CancellationToken cancellationToken)
    {
        var gatewayUrl = (configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000").TrimEnd('/');
        var statusUrl = $"{gatewayUrl}/api/whatsapp/session/status?projectId={Uri.EscapeDataString(projectId.ToString())}";
        if (gatewayAccountId.HasValue)
            statusUrl += $"&whatsappAccountId={Uri.EscapeDataString(gatewayAccountId.Value.ToString())}";
        using var response = await httpClient.GetAsync(
            statusUrl,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new("Disconnected", null, $"GATEWAY_HTTP_{(int)response.StatusCode}");

        return await response.Content.ReadFromJsonAsync<WhatsAppGatewaySessionStatus>(cancellationToken: cancellationToken)
            ?? new("Disconnected", null, "GATEWAY_EMPTY_RESPONSE");
    }

    private static WhatsAppGatewaySessionStatus Unavailable(Exception exception) =>
        new("Disconnected", null, $"GATEWAY_STATUS_UNAVAILABLE_{exception.GetType().Name}");

    public static string NormalizePhone(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
