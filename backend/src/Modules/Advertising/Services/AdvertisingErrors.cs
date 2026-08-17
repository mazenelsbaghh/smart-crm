using System.Text.RegularExpressions;

namespace Modules.Advertising.Services;

public sealed class AdvertisingException(string code, string safeMessage, int statusCode = 400) : Exception(safeMessage)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public static partial class AdvertisingLogSanitizer
{
    [GeneratedRegex("(?i)[\\\"']?(access[_-]?token|secret|email|phone|match[_-]?data)[\\\"']?\\s*[:=]\\s*[\\\"']?[^,;}\\s\\\"']+")]
    private static partial Regex SensitiveValuePattern();

    public static string Redact(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : SensitiveValuePattern().Replace(value, "$1=[REDACTED]");
}
