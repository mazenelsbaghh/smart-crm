using System.Text.Json;

namespace Modules.AI.Services;

internal static class GeneratedMessageText
{
    public const string OutputContract = "Return exactly one JSON object with a single string field named message. "
        + "The message value must contain only the final customer-facing text, without JSON, reasoning or explanations.";

    public static string? Read(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.String) return null;
            var text = message.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var nested = JsonDocument.Parse(text);
                if (nested.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return null;
            }
            catch (JsonException) { }
            return text;
        }
        catch (JsonException) { return null; }
    }
}
