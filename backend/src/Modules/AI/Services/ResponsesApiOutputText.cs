using System.Text.Json;

namespace Modules.AI.Services;

internal static class ResponsesApiOutputText
{
    public static string? Extract(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        if (document.RootElement.TryGetProperty("output_text", out var directOutput))
        {
            var directText = directOutput.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }
        }

        if (!document.RootElement.TryGetProperty("output", out var outputEntries) ||
            outputEntries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outputEntry in outputEntries.EnumerateArray())
        {
            if (!outputEntry.TryGetProperty("content", out var contentParts) ||
                contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentPart in contentParts.EnumerateArray())
            {
                if (contentPart.TryGetProperty("type", out var contentType) &&
                    contentType.GetString() == "output_text" &&
                    contentPart.TryGetProperty("text", out var text))
                {
                    var outputText = text.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(outputText))
                    {
                        return outputText;
                    }
                }
            }
        }

        return null;
    }
}
