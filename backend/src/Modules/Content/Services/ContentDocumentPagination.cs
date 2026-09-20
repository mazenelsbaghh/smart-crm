using System.Globalization;
using System.Text.RegularExpressions;
using Modules.Content.Domain;

namespace Modules.Content.Services;

internal static class ContentDocumentPagination
{
    internal static PageCountSuggestion Suggest(string content, ContentDocumentKind kind)
    {
        var pages = CreatePageBodies(content, kind).Skip(1).ToList();
        var suggested = pages.Count + 1;
        var maximum = Math.Max(suggested, Math.Min(200, pages.Sum(page => SplitPoints(page).Count + 1) + 1));
        while (MergeSmallest(pages)) { }
        return new PageCountSuggestion(suggested, pages.Count + 1, maximum);
    }

    internal static IReadOnlyList<string> CreatePageBodies(string content, ContentDocumentKind kind, int pageCount)
    {
        var limits = Suggest(content, kind);
        if (pageCount < limits.MinPageCount || pageCount > limits.MaxPageCount)
            throw new ArgumentException($"اختر عددًا بين {limits.MinPageCount} و{limits.MaxPageCount}، شامل الغلاف.");
        var pages = CreatePageBodies(content, kind).Skip(1).ToList();
        while (pages.Count > pageCount - 1 && MergeSmallest(pages)) { }
        while (pages.Count < pageCount - 1 && SplitLongest(pages)) { }
        pages.Insert(0, string.Empty);
        return pages;
    }

    internal static bool SplitLongest(List<string> passages, int minimumLength = 0)
    {
        var candidates = Enumerable.Range(0, passages.Count)
            .Where(index => passages[index].Length > minimumLength && !ContentDocumentSessions.IsHeading(passages[index])
                && PassageSplitPoints(passages[index]).Count > 0).ToList();
        if (candidates.Count == 0) return false;
        var index = candidates.MaxBy(index => passages[index].Length);
        var body = passages[index];
        var split = PassageSplitPoints(body).MinBy(point => Math.Abs(point - body.Length / 2));
        passages[index] = body[..split];
        passages.Insert(index + 1, body[split..]);
        return true;
    }

    private static bool MergeSmallest(List<string> pages)
    {
        var index = -1;
        var shortest = int.MaxValue;
        for (var candidate = 0; candidate < pages.Count - 1; candidate++)
        {
            var length = pages[candidate].Length + pages[candidate + 1].Length;
            if (length <= 4_000 && length < shortest) { index = candidate; shortest = length; }
        }
        if (index < 0) return false;
        pages[index] += pages[index + 1];
        pages.RemoveAt(index + 1);
        return true;
    }

    private static List<int> SplitPoints(string body) => Regex.Matches(body, @"\S\s+(?=\S)")
        .Select(match => match.Index + match.Length).ToList();

    // A break inside prose must not turn a mention of a session into a new section heading.
    private static List<int> PassageSplitPoints(string body) => SplitPoints(body)
        .Where(point => !ContentDocumentSessions.IsHeading(body.AsSpan(0, point)) && !ContentDocumentSessions.IsHeading(body.AsSpan(point))).ToList();

    internal static IReadOnlyList<string> CreatePageBodies(string content, ContentDocumentKind kind)
    {
        // Keep every source character, including boundary whitespace; never split an Arabic mark or emoji.
        var pageLength = kind == ContentDocumentKind.Presentation ? 700 : 1_800;
        var textBoundaries = StringInfo.ParseCombiningCharacters(content);
        // The introductory cover has a title and artwork; all source text starts on the next page.
        var pages = new List<string> { string.Empty };
        var start = 0;
        while (content.Length - start > pageLength)
        {
            var end = FindPageEnd(content, start, pageLength);
            var boundary = Array.BinarySearch(textBoundaries, end);
            if (boundary < 0) end = textBoundaries[~boundary - 1];
            if (end <= start) end = textBoundaries.FirstOrDefault(index => index > start, content.Length);
            pages.Add(content[start..end]);
            start = end;
        }
        if (start < content.Length)
        {
            var remainder = content[start..];
            if (string.IsNullOrWhiteSpace(remainder) && pages.Count > 1) pages[^1] += remainder;
            else pages.Add(remainder);
        }
        return pages;
    }

    private static int FindPageEnd(string content, int start, int pageLength)
    {
        var window = content.Substring(start, pageLength);
        foreach (var pattern in new[] { @"\r?\n", @"[.!؟?。]\s+", @"\s+" })
        {
            var breaks = Regex.Matches(window, pattern);
            if (breaks.Count == 0) continue;
            var lastBreak = breaks[^1];
            var end = lastBreak.Index + lastBreak.Length;
            if (end >= pageLength / 2) return start + end;
        }
        return start + pageLength;
    }

    internal sealed record PageCountSuggestion(int PageCount, int MinPageCount, int MaxPageCount);
}
