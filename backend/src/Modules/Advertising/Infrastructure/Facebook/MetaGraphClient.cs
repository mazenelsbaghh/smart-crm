using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Modules.Advertising.Services;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaProviderTrace(string? TraceId, int StatusCode, string? UsageJson, DateTime ObservedAtUtc);
public sealed record MetaGraphResult<T>(T Value, MetaProviderTrace Trace);

public sealed class MetaGraphException(string code, string message, HttpStatusCode statusCode, MetaProviderTrace trace)
    : AdvertisingException(code, message, (int)statusCode)
{
    public MetaProviderTrace Trace { get; } = trace;
}

public sealed class MetaGraphClient(HttpClient httpClient)
{
    private const int MaximumPaginationPages = 20;

    public Task<MetaGraphResult<JsonDocument>> GetAsync(string path, string accessToken, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, accessToken, null, cancellationToken);

    public async Task<MetaGraphResult<JsonDocument>> PostFormAsync(string path, string accessToken,
        IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await SendAsync(HttpMethod.Post, path, accessToken, content, cancellationToken);
    }

    public async Task<IReadOnlyList<JsonElement>> GetAllAsync(string path, string accessToken, CancellationToken cancellationToken = default)
    {
        var values = new List<JsonElement>();
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);
        string? next = path;
        MetaProviderTrace? lastTrace = null;
        while (!string.IsNullOrWhiteSpace(next))
        {
            var pageUri = ResolveTrustedGraphUri(next);
            if (visitedPages.Count >= MaximumPaginationPages || !visitedPages.Add(pageUri.AbsoluteUri))
                throw new MetaGraphException(
                    "ADS_META_PAGINATION_INVALID",
                    "Meta pagination exceeded the safe page limit or repeated a cursor.",
                    HttpStatusCode.BadGateway,
                    lastTrace ?? new MetaProviderTrace(null, (int)HttpStatusCode.BadGateway, null, DateTime.UtcNow));

            var pageResponse = await GetAsync(pageUri.AbsoluteUri, accessToken, cancellationToken);
            lastTrace = pageResponse.Trace;
            using var document = pageResponse.Value;
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new MetaGraphException("ADS_META_SCHEMA_INVALID", "Meta returned an unexpected resource shape.", HttpStatusCode.BadGateway, pageResponse.Trace);
            values.AddRange(data.EnumerateArray().Select(item => item.Clone()));
            next = root.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var nextValue)
                ? nextValue.GetString()
                : null;
        }
        return values;
    }

    private async Task<MetaGraphResult<JsonDocument>> SendAsync(HttpMethod method, string path, string accessToken,
        HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, ResolveTrustedGraphUri(path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var trace = new MetaProviderTrace(
            response.Headers.TryGetValues("x-fb-trace-id", out var traceValues) ? traceValues.FirstOrDefault() : null,
            (int)response.StatusCode,
            response.Headers.TryGetValues("x-business-use-case-usage", out var usage) ? usage.FirstOrDefault() : null,
            DateTime.UtcNow);
        if (!response.IsSuccessStatusCode)
            throw Classify(response.StatusCode, body, trace);
        try { return new(JsonDocument.Parse(body), trace); }
        catch (JsonException) { throw new MetaGraphException("ADS_META_SCHEMA_INVALID", "Meta returned invalid JSON.", HttpStatusCode.BadGateway, trace); }
    }

    private Uri ResolveTrustedGraphUri(string path)
    {
        var graphBaseAddress = httpClient.BaseAddress
            ?? throw InvalidRequestUri();
        if (!Uri.TryCreate(graphBaseAddress, path, out var requestUri)
            || !string.Equals(requestUri.Scheme, graphBaseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requestUri.IdnHost, graphBaseAddress.IdnHost, StringComparison.OrdinalIgnoreCase)
            || requestUri.Port != graphBaseAddress.Port
            || !requestUri.AbsolutePath.StartsWith(graphBaseAddress.AbsolutePath, StringComparison.Ordinal))
            throw InvalidRequestUri();
        return requestUri;
    }

    private static MetaGraphException InvalidRequestUri() => new(
        "ADS_META_REQUEST_URI_INVALID",
        "Meta request URI must stay on the configured Graph API origin.",
        HttpStatusCode.BadRequest,
        new MetaProviderTrace(null, (int)HttpStatusCode.BadRequest, null, DateTime.UtcNow));

    private static MetaGraphException Classify(HttpStatusCode status, string body, MetaProviderTrace trace)
    {
        var code = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "ADS_META_AUTHORIZATION_FAILED",
            HttpStatusCode.TooManyRequests => "ADS_META_RATE_LIMITED",
            HttpStatusCode.BadRequest => "ADS_META_VALIDATION_FAILED",
            _ when (int)status >= 500 => "ADS_META_TRANSIENT_FAILURE",
            _ => "ADS_META_PROVIDER_FAILURE"
        };
        return new(code, AdvertisingErrorEnvelope.Sanitize(body[..Math.Min(body.Length, 1000)]), status, trace);
    }
}
