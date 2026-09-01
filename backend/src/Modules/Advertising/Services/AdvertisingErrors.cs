using System.Text.RegularExpressions;

namespace Modules.Advertising.Services;

public class AdvertisingException(string code, string message, int statusCode = 422) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record AdvertisingOperationReceipt(Guid OperationId, Guid CorrelationId, string State, string StatusUrl);

public sealed record AdvertisingErrorEnvelope(string Code, string Message, Guid CorrelationId, string? ProviderTraceId = null)
{
    public static AdvertisingErrorEnvelope ProviderFailure(string operation, string providerMessage, string? providerTraceId) =>
        new("ADS_PROVIDER_FAILURE", $"Meta rejected {SafeOperation(operation)}. Review the provider trace and retry safely. {Sanitize(providerMessage)}",
            Guid.NewGuid(), providerTraceId);

    public static string Sanitize(string? value) => AdvertisingLogSanitizer.Redact(value);

    private static string SafeOperation(string value) => Regex.Replace(value, "[^A-Za-z0-9_.-]", string.Empty);
}

public static class AdvertisingMutationProtocol
{
    public static string RequireIdempotencyKey(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128)
            throw new AdvertisingException("ADS_IDEMPOTENCY_REQUIRED", "A valid Idempotency-Key header is required.", 400);
        return normalized;
    }

    public static long RequireIfMatch(string? value)
    {
        var normalized = value?.Trim();
        if (normalized is null || normalized.Length < 3 || normalized[0] != '"' || normalized[^1] != '"' ||
            !long.TryParse(normalized[1..^1], out var version) || version < 0)
            throw new AdvertisingException("ADS_IF_MATCH_REQUIRED", "A quoted numeric If-Match version is required.", 428);
        return version;
    }

    public static AdvertisingOperationReceipt Accepted(Guid projectId, Guid operationId, Guid correlationId, string state) =>
        new(operationId, correlationId, state, $"/api/projects/{projectId}/ad-manager/operations/{operationId}");
}
