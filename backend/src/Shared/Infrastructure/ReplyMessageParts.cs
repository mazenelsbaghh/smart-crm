namespace Shared.Infrastructure;

internal static class ReplyMessageParts
{
    public static IReadOnlyList<string> SplitParagraphs(string content)
    {
        var paragraphs = content.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (trimmed.Length == 0) continue;
            if (IsStandaloneSignature(trimmed) && parts.Count > 0)
                parts[^1] += "\n" + trimmed;
            else
                parts.Add(trimmed);
        }
        return parts;
    }

    public static bool IsStandaloneSignature(string text) => text.Length <= 40 &&
        (text.StartsWith("-") || text.StartsWith("–") || text.StartsWith("—"));
}
