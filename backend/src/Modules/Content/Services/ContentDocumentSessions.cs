using System.Text.RegularExpressions;
using Modules.Content.Domain;

namespace Modules.Content.Services;

internal static partial class ContentDocumentSessions
{
    [GeneratedRegex(@"\A(?:\#{1,6}[\t ]*)?(?:\*\*|__)?(?:session|(?:ال)?سيشن|(?:ال)?جلس[ةه])[\t ]*[:：]?[\t ]*(?:\p{Nd}+|one|two|three|four|five|six|seven|eight|nine|ten|(?:ال)?(?:أول|اول|أولى|اولي|اولى|ثاني|ثانية|ثانيه|ثالث|ثالثة|ثالثه|رابع|رابعة|رابعه|خامس|خامسة|خامسه|سادس|سادسة|سادسه|سابع|ثامن|تاسع|عاشر))(?![\p{L}\p{N}])[^\r\n]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SessionHeading();

    internal static bool IsHeading(string text) => SessionHeading().IsMatch(text.Trim());
    internal static bool IsHeading(ReadOnlySpan<char> text) => SessionHeading().IsMatch(text.Trim());

    internal static string ForSeparateFiles(string content)
    {
        var lines = ContentDocumentText.ForDisplay(content).Split('\n').ToList();
        var firstHeading = lines.FindIndex(IsHeading);
        if (firstHeading > 0)
        {
            var title = lines[firstHeading];
            lines.RemoveAt(firstHeading);
            lines.Insert(0, title);
        }
        return string.Join('\n', lines);
    }

    internal static int[] SectionIds(IReadOnlyList<string> passages)
    {
        var sections = new int[passages.Count];
        for (var index = 1; index < passages.Count; index++)
            sections[index] = sections[index - 1] + (IsHeading(passages[index]) ? 1 : 0);
        return sections;
    }

    internal static Suggestion Suggest(string content, ContentDocumentKind kind)
    {
        var lines = ContentDocumentText.ForDisplay(content).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (lines.Count == 0) throw new ArgumentException("اكتب محتوى فعليًا بعد إزالة الإيموجي وعلامات السلايدز.");
        var sections = SectionIds(lines);
        var suggestions = lines.Select((line, index) => (line, section: sections[index])).GroupBy(item => item.section)
            .Select(group =>
            {
                var text = group.Select(item => item.line).ToList();
                var count = ContentDocumentPagination.Suggest(string.Join("\n\n", text), kind);
                var maximum = Math.Min(199, text.Sum(line => IsHeading(line) ? 1 : Regex.Matches(line, @"\S+").Count));
                var title = IsHeading(text[0]) ? text[0] : lines.Any(IsHeading) ? "المقدمة قبل السيشنات" : "المحتوى";
                return new SectionSuggestion(group.Key, title, count.PageCount - 1, count.MinPageCount - 1, maximum);
            }).ToList();
        var recommended = 1 + suggestions.Sum(suggestion => suggestion.PageCount);
        var minimum = 1 + suggestions.Sum(suggestion => suggestion.MinPageCount);
        var maximum = 1 + lines.Sum(line => IsHeading(line) ? 1 : Regex.Matches(line, @"\S+").Count);
        return new Suggestion(recommended, minimum, Math.Max(recommended, Math.Min(200, maximum)), lines.Count(IsHeading), suggestions);
    }

    internal static void ValidateRanges(Suggestion suggestion, IReadOnlyList<ContentDocumentSessionRange> ranges)
    {
        ValidateSectionRanges(suggestion, ranges);
        if (ranges.Sum(range => (long)range.MaxPageCount) > 199)
            throw new ArgumentException("مجموع الحدود القصوى للسيشنات يجب ألا يزيد عن 199 صفحة، بالإضافة إلى صفحة الغلاف.");
    }

    internal static List<ContentDocumentSessionRange> UniformRanges(Suggestion suggestion, int pagesPerSession)
    {
        if (suggestion.SessionCount == 0) throw new ArgumentException("اكتب اسم كل سيشن في سطر مستقل، ثم محتواه.");
        if (suggestion.Sections.Any(section => section.Title.Length > 300))
            throw new ArgumentException("اختصر اسم كل سيشن إلى 300 حرف كحد أقصى؛ حط التفاصيل في السطور التالية لاسم السيشن.");
        if (suggestion.Sections.Count > 50 || pagesPerSession < 2 || (long)pagesPerSession * suggestion.Sections.Count > 200)
            throw new ArgumentException("اختَر صفحتين على الأقل لكل ملف، وبحد أقصى 50 ملفًا و200 صفحة في الدفعة، شامل أغلفة السيشنات.");
        var ranges = suggestion.Sections.Select(section => new ContentDocumentSessionRange(section.Section, pagesPerSession - 1, pagesPerSession - 1)).ToList();
        ValidateSectionRanges(suggestion, ranges);
        return ranges;
    }

    private static void ValidateSectionRanges(Suggestion suggestion, IReadOnlyList<ContentDocumentSessionRange> ranges)
    {
        if (ranges.Count != suggestion.Sections.Count || ranges.Where((range, index) => range is null || range.Section != index).Any())
            throw new ArgumentException("حدد نطاق العدد لكل سيشن بالترتيب الحالي للمحتوى، ثم اعرض التقسيم من جديد.");
        foreach (var (range, section) in ranges.Zip(suggestion.Sections))
            if (range.MinPageCount < section.MinPageCount || range.MaxPageCount > section.MaxPageCount || range.MinPageCount > range.MaxPageCount)
                throw new ArgumentException($"{section.Title}: اختر حدًا أدنى وأقصى بين {section.MinPageCount} و{section.MaxPageCount}، والأقصى لا يقل عن الأدنى.");
    }

    internal static IReadOnlyList<string> SourcePassages(string content, IReadOnlyList<ContentDocumentSessionRange> ranges)
    {
        var lines = ContentDocumentText.ForDisplay(content).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        return lines.Zip(SectionIds(lines), (text, section) => (text, section)).GroupBy(item => item.section)
            .SelectMany(group => ContentDocumentPreview.SourcePassages(string.Join("\n", group.Select(item => item.text)), ranges[group.Key].MaxPageCount + 1)).ToList();
    }

    // Called after outline validation, so every page has valid IDs from exactly one section.
    internal static List<Allocation> Allocations(IReadOnlyList<string> passages, ContentDocumentPreview.Outline outline,
        IReadOnlyList<ContentDocumentSessionRange> ranges)
    {
        var sections = SectionIds(passages);
        var allocations = outline.Pages.Select((page, index) => (section: sections[page.Blocks[0].Ids[0]], number: index + 2))
            .GroupBy(page => page.section).Select(group => new Allocation(group.Key,
                IsHeading(passages[Array.IndexOf(sections, group.Key)]) ? passages[Array.IndexOf(sections, group.Key)] : "المحتوى",
                group.Count(), group.First().number, group.Last().number)).ToList();
        foreach (var allocation in allocations)
        {
            var range = ranges[allocation.Section];
            if (allocation.PageCount < range.MinPageCount || allocation.PageCount > range.MaxPageCount)
                throw new InvalidOperationException($"section {allocation.Section}: عدد صفحاته {allocation.PageCount} خارج النطاق من {range.MinPageCount} إلى {range.MaxPageCount}. أعد توزيعه داخل نطاقه مع الحفاظ على كل فقراته.");
        }
        return allocations;
    }

    internal static void Validate(IReadOnlyList<string> passages, ContentDocumentPreview.Outline outline)
    {
        var sections = SectionIds(passages);
        var previousSection = -1;
        var seen = new HashSet<int>();
        foreach (var page in outline.Pages)
        {
            var ids = page.Blocks.SelectMany(block => block.Ids).ToArray();
            var section = sections[ids[0]];
            if (section < previousSection || ids.Any(id => sections[id] != section))
                throw new InvalidOperationException("لا تخلط سيشنين في صفحة واحدة، وحافظ على ترتيب السيشنات كما وردت في المصدر.");
            var first = Array.IndexOf(sections, section);
            if (seen.Add(section) && IsHeading(passages[first]) && ids[0] != first)
                throw new InvalidOperationException($"ابدأ أول صفحة للسيشن بفقرة عنوانه رقم {first}، ثم باقي محتواه.");
            previousSection = section;
        }
    }

    internal sealed record Suggestion(int PageCount, int MinPageCount, int MaxPageCount, int SessionCount, List<SectionSuggestion> Sections,
        List<SectionSuggestion>? FileSections = null);
    internal sealed record SectionSuggestion(int Section, string Title, int PageCount, int MinPageCount, int MaxPageCount);
    internal sealed record Allocation(int Section, string Title, int PageCount, int FromPage, int ToPage);
}

public sealed record ContentDocumentSessionRange(int Section, int MinPageCount, int MaxPageCount);
