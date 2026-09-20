using System.Text.Json;
using System.Text.Json.Serialization;
using Modules.Content.Domain;

namespace Modules.Content.Services;

internal static class ContentDocumentPreview
{
    internal static IReadOnlyList<string> SourcePassages(string content, int pageCount)
    {
        var passages = ContentDocumentText.ForDisplay(content).Split('\n')
            .Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        // Move whole words: inserting a line break inside a URL or token would change the source.
        while (ContentDocumentPagination.SplitLongest(passages, 700)) { }
        if (passages.Any(passage => passage.Length > 4_000))
            throw new ArgumentException("في المحتوى كلمة أو رابط أطول من سعة الصفحة. راجعه قبل تقسيم المحتوى.");
        while (passages.Count < pageCount - 1 && ContentDocumentPagination.SplitLongest(passages)) { }
        if (passages.Count < pageCount - 1) throw new ArgumentException("قلل عدد الصفحات حتى تحتوي كل صفحة على محتوى.");
        return passages.Select(passage => passage.Trim()).ToList();
    }

    internal static Preview Create(string title, IReadOnlyList<string> passages, Outline outline, int pageCount)
    {
        ValidateOutline(outline, passages, pageCount);
        ContentDocumentSessions.Validate(passages, outline);
        var pages = new List<PreviewPage> { new(0, title, string.Empty, []) };
        foreach (var plannedPage in outline.Pages)
        {
            var blocks = plannedPage.Blocks.Select(block => new PreviewBlock(
                block.Type, block.Ids.Select(id => passages[id]).ToList())).ToList();
            var body = string.Join("\n\n", blocks.Select(block => string.Join("\n", block.Items)));
            if (body.Length > 4_000) throw new InvalidOperationException("توزيع النص كثيف جدًا على إحدى الصفحات.");
            pages.Add(new PreviewPage(pages.Count, string.Empty, body, blocks));
        }
        return new Preview(title, pageCount, pages, string.Empty);
    }

    private static void ValidateOutline(Outline outline, IReadOnlyList<string> passages, int pageCount)
    {
        if (outline.Pages?.Count != pageCount - 1 || outline.Pages.Any(page => page?.Blocks is not { Count: > 0 }))
            throw new InvalidOperationException("عدد الصفحات غير مكتمل.");
        var used = new HashSet<int>();
        foreach (var block in outline.Pages.SelectMany(page => page.Blocks))
        {
            if (block is null || block.Type is not ("heading" or "paragraph" or "bullets") || block.Ids is not { Count: > 0 })
                throw new InvalidOperationException("تنسيق إحدى الفقرات غير صالح.");
            foreach (var id in block.Ids)
                if (id < 0 || id >= passages.Count || !used.Add(id))
                    throw new InvalidOperationException("التقسيم كرر فقرة أو استخدم فقرة غير موجودة.");
            if (block.Type == "heading" && (block.Ids.Count != 1 || passages[block.Ids[0]].Length > 300))
                throw new InvalidOperationException("اختر عنوانًا قصيرًا من النص الأصلي.");
        }
        if (used.Count != passages.Count)
            throw new InvalidOperationException("الفقرات الناقصة (ids): "
                + string.Join(", ", Enumerable.Range(0, passages.Count).Where(id => !used.Contains(id)))
                + ". أدرجها داخل صفحات المحتوى؛ ظهور عنوان مشابه في الغلاف لا يغني عنها.");
    }

    internal static Preview SeparateSessions(Preview preview, List<ContentDocumentSessions.Allocation> allocations)
    {
        var documents = allocations.Select(session => new PreviewFile(session.Section, session.Title,
            new List<PreviewPage> { new(0, session.Title, string.Empty, []) }
                .Concat(preview.Pages.Skip(session.FromPage - 1).Take(session.PageCount)
                    .Select((page, index) => page with { PageIndex = index + 1 })).ToList())).ToList();
        return preview with { Pages = [], PageCount = documents.Sum(document => document.PageCount), Documents = documents };
    }

    internal static List<ContentDocumentPage> CreatePages(ContentDocument document, IReadOnlyList<PreviewPage> pages) =>
        pages.Select(page => new ContentDocumentPage
        {
            ProjectId = document.ProjectId,
            DocumentId = document.Id,
            PageIndex = page.PageIndex,
            Title = page.Title,
            Body = page.Body,
            ImagePrompt = ContentDocumentGenerationService.SourceImagePrompt(page.PageIndex)
                + (page.Blocks.Count == 0 ? string.Empty : "\nPreserve the approved reading order and grouping. "
                    + "Format the body blocks (separated by blank lines) in this order: "
                    + string.Join("; ", page.Blocks.Select(block => $"{block.Type} ({block.Items.Count} passages)"))
                    + ". Use bullets only as visual markers. Never add heading text or change the supplied words."),
            Status = ContentDocumentPageStatus.Planned
        }).ToList();

    internal sealed record Preview(string Title, int PageCount, List<PreviewPage> Pages, string Fingerprint,
        List<ContentDocumentSessions.Allocation>? Sessions = null, List<PreviewFile>? Documents = null);
    internal sealed record PreviewFile(int Section, string Title, List<PreviewPage> Pages)
    {
        public int PageCount => Pages.Count;
    }
    internal sealed record PreviewPage(int PageIndex, string Title, string Body, List<PreviewBlock> Blocks);
    internal sealed record PreviewBlock(string Type, List<string> Items);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record Outline(List<OutlinePage> Pages);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record OutlinePage(List<OutlineBlock> Blocks);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record OutlineBlock(string Type, List<int> Ids);
}
