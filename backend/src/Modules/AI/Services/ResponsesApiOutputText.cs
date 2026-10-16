using System.Text;
using System.Text.Json;

namespace Modules.AI.Services;

internal static class ResponsesApiOutputText
{
    public static string? Extract(string responseJson, ILogger logger, string provider, string model)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        LogUsage(root, logger, provider, model);
        if (IsUnfinished(root) || root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            logger.LogWarning("{Provider} response was incomplete or failed for model {Model}", provider, model);
            return null;
        }

        if (root.TryGetProperty("output", out var outputEntries) && outputEntries.ValueKind == JsonValueKind.Array)
            return ReadOutputMessages(outputEntries);

        return root.TryGetProperty("output_text", out var directOutput) && directOutput.ValueKind == JsonValueKind.String
            ? NonEmptyText(directOutput.GetString())
            : null;
    }

    private static string? ReadOutputMessages(JsonElement outputEntries)
    {
        var outputText = new StringBuilder();
        foreach (var outputEntry in outputEntries.EnumerateArray())
        {
            if (outputEntry.ValueKind != JsonValueKind.Object ||
                outputEntry.TryGetProperty("type", out var entryType)
                    && (entryType.ValueKind != JsonValueKind.String || entryType.GetString() != "message"))
                continue;
            if (IsUnfinished(outputEntry)) return null;
            if (!outputEntry.TryGetProperty("content", out var contentParts) || contentParts.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var contentPart in contentParts.EnumerateArray())
            {
                if (contentPart.ValueKind != JsonValueKind.Object || !contentPart.TryGetProperty("type", out var contentType)
                    || contentType.ValueKind != JsonValueKind.String) continue;
                if (contentType.GetString() == "refusal") return null;
                if (contentType.GetString() == "output_text" && contentPart.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                    outputText.Append(text.GetString());
            }
        }
        return NonEmptyText(outputText.ToString());
    }

    private static bool IsUnfinished(JsonElement response) =>
        response.TryGetProperty("status", out var status)
        && (status.ValueKind != JsonValueKind.String || status.GetString() != "completed");

    private static string? NonEmptyText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static void LogUsage(JsonElement root, ILogger logger, string provider, string model)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        logger.LogInformation(
            "AI response usage Provider={Provider} Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} CachedTokens={CachedTokens} ReasoningTokens={ReasoningTokens} TotalTokens={TotalTokens}",
            provider, model, TokenCount(usage, "input_tokens"), TokenCount(usage, "output_tokens"),
            DetailTokenCount(usage, "input_tokens_details", "cached_tokens"),
            DetailTokenCount(usage, "output_tokens_details", "reasoning_tokens"), TokenCount(usage, "total_tokens"));
    }

    private static long? TokenCount(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt64(out var tokens)
            ? tokens
            : null;

    private static long? DetailTokenCount(JsonElement usage, string detailsName, string name) =>
        usage.TryGetProperty(detailsName, out var details) && details.ValueKind == JsonValueKind.Object
            ? TokenCount(details, name)
            : null;
}
