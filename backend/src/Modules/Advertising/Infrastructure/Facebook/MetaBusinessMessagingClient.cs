using System.Net.Http.Headers;
using System.Text.Json;
using Modules.Advertising.Services;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record BusinessMessagingEventRequest(string DatasetId, string WabaId, string CtwaClid,
    string EventName, string EventId, DateTime OccurredAtUtc, decimal? Value, string? Currency, string? TestEventCode = null);
public sealed record BusinessMessagingEventResult(int EventsReceived, string? ProviderRequestId, string? ProviderTraceId, string ResponseHash);

public sealed class MetaBusinessMessagingClient(HttpClient httpClient)
{
    public async Task<BusinessMessagingEventResult> SendAsync(string accessToken, BusinessMessagingEventRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            event_name = request.EventName,
            event_time = new DateTimeOffset(request.OccurredAtUtc).ToUnixTimeSeconds(),
            event_id = request.EventId,
            action_source = "business_messaging",
            messaging_channel = "whatsapp",
            user_data = new { whatsapp_business_account_id = request.WabaId, ctwa_clid = request.CtwaClid },
            custom_data = request.Value is null ? null : new { value = request.Value, currency = request.Currency }
        };
        var form = new Dictionary<string, string> { ["data"] = JsonSerializer.Serialize(new[] { payload }) };
        if (!string.IsNullOrWhiteSpace(request.TestEventCode)) form["test_event_code"] = request.TestEventCode;
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{request.DatasetId}/events")
        {
            Content = new FormUrlEncodedContent(form)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta Business Messaging event failed ({(int)response.StatusCode}).", null, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var accepted = json.RootElement.TryGetProperty("events_received", out var received) && received.TryGetInt32(out var count) ? count : 0;
        var requestId = response.Headers.TryGetValues("x-fb-request-id", out var requestIds) ? requestIds.FirstOrDefault() : null;
        var traceId = response.Headers.TryGetValues("x-fb-trace-id", out var traces) ? traces.FirstOrDefault() : null;
        return new(accepted, requestId, traceId, AdvertisingAuditService.HashState(body));
    }
}
