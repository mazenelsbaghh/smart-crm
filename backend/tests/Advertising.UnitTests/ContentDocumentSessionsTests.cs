using System.Text.Json;
using Modules.Content.Domain;
using Modules.Content.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentDocumentSessionsTests
{
    [Fact]
    public void Session_titles_must_fit_the_saved_file_before_planning()
    {
        var source = $"Session 11: {new string('x', 300)}\nOriginal details";
        var suggestion = ContentDocumentSessions.Suggest(source, ContentDocumentKind.Presentation);
        Assert.Throws<ArgumentException>(() => ContentDocumentSessions.UniformRanges(suggestion, 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(101)]
    [InlineData(200)]
    public void Shared_count_reserves_every_cover_and_stays_inside_the_batch_limit(int pageCount)
    {
        var source = $"Session 11\n{string.Join(' ', Enumerable.Repeat("word", 199))}\nسيشن ١٢\n{string.Join(' ', Enumerable.Repeat("word", 199))}";
        var suggestion = ContentDocumentSessions.Suggest(source, ContentDocumentKind.Presentation);
        Assert.Throws<ArgumentException>(() => ContentDocumentSessions.UniformRanges(suggestion, pageCount));
        Assert.Equal(new[] { 99, 99 }, ContentDocumentSessions.UniformRanges(suggestion, 100).Select(range => range.MinPageCount));
    }

    [Theory]
    [InlineData("✅ Slide 1: Title: Welcome 👩🏽‍💻", "Welcome ")]
    [InlineData("**Slide ٢:** عنوان: أهلاً 🇪🇬", "أهلاً ")]
    [InlineData("Slider 10 — مرحبًا\nSlides #12: التفاصيل", "مرحبًا\nالتفاصيل")]
    [InlineData("قبل😀بعد\n👨‍👩‍👧‍👦 👍🏿 🏳️‍🌈 1️⃣ ❤️", "قبل بعد\n    ")]
    [InlineData("رقم 123 و١٢٣، C# و* و© 2026 و® و™ و± و∑ و𝔸 والسِّعر", "رقم 123 و١٢٣، C# و* و© 2026 و® و™ و± و∑ و𝔸 والسِّعر")]
    [InlineData("https://example.com/slide1 waterslide2 slides are useful", "https://example.com/slide1 waterslide2 slides are useful")]
    [InlineData("راجع Slide 3 بعد الشرح", "راجع بعد الشرح")]
    [InlineData("Slide 1️⃣: Title: البداية", "البداية")]
    public void Display_copy_removes_only_emoji_and_numbered_slide_labels(string source, string expected)
    {
        var cleaned = ContentDocumentText.ForDisplay(source);
        Assert.Equal(expected, cleaned);
        Assert.Equal(cleaned, ContentDocumentText.ForDisplay(cleaned));
    }

    [Theory]
    [InlineData("Session 1: Listening", true)]
    [InlineData("### SESSION ٢", true)]
    [InlineData("**سيشن ٣: الكلام**", true)]
    [InlineData("السيشن الأول: البداية", true)]
    [InlineData("الجلسة الثانية", true)]
    [InlineData("Session one", true)]
    [InlineData("We have 2 sessions", false)]
    [InlineData("Session 1\nDetails", false)]
    public void Only_explicit_session_headings_start_sections(string source, bool expected)
    {
        Assert.Equal(expected, ContentDocumentSessions.IsHeading(source));
    }

    [Fact]
    public void Count_suggestion_reserves_pages_for_each_session_and_ignores_removed_labels()
    {
        const string source = "✅ Session 1\nSlide 1: Title: First topic\nFirst explanation.\nSession 2\nSlide 2: Second topic\nSecond explanation.";
        var suggestion = ContentDocumentSessions.Suggest(source, ContentDocumentKind.Presentation);
        Assert.Equal(3, suggestion.PageCount);
        Assert.Equal(3, suggestion.MinPageCount);
        Assert.Equal(2, suggestion.SessionCount);
        var cleaned = "Session 1\nFirst topic\nFirst explanation.\nSession 2\nSecond topic\nSecond explanation.";
        Assert.Equal(JsonSerializer.Serialize(suggestion), JsonSerializer.Serialize(ContentDocumentSessions.Suggest(cleaned, ContentDocumentKind.Presentation)));
        var passages = ContentDocumentPreview.SourcePassages(source, suggestion.MaxPageCount);
        Assert.Equal(2, passages.Count(ContentDocumentSessions.IsHeading));
        Assert.Equal(suggestion.MaxPageCount - 1, passages.Count);
        Assert.Throws<ArgumentException>(() => ContentDocumentSessions.Suggest("✅\nSlide 1:\nTitle:", ContentDocumentKind.Presentation));
    }

    [Theory]
    [InlineData("[[0,1,3],[2,4,5]]")]
    [InlineData("[[3,4,5],[0,1,2]]")]
    [InlineData("[[1,0,2],[3,4,5]]")]
    public void Preview_rejects_mixed_reversed_or_detached_session_headings(string pageIds)
    {
        var ids = JsonSerializer.Deserialize<int[][]>(pageIds)!;
        var outline = new ContentDocumentPreview.Outline(ids.Select(page => new ContentDocumentPreview.OutlinePage(
            [new ContentDocumentPreview.OutlineBlock("paragraph", page.ToList())])).ToList());
        Assert.Throws<InvalidOperationException>(() => ContentDocumentPreview.Create("Training", Source(), outline, 3));
    }

    [Fact]
    public void Sessions_can_span_pages_and_reorder_their_own_passages_without_losing_words()
    {
        var source = Source();
        var outline = new ContentDocumentPreview.Outline([
            new([new("heading", [0]), new("paragraph", [2])]),
            new([new("paragraph", [1])]),
            new([new("heading", [3]), new("paragraph", [5,4])])]);
        var preview = ContentDocumentPreview.Create("Training", source, outline, 4);
        Assert.Equal("Session 1\n\nFirst explanation.", preview.Pages[1].Body);
        Assert.Equal("First topic", preview.Pages[2].Body);
        Assert.Equal("سيشن ٢\n\nSecond explanation.\nSecond topic", preview.Pages[3].Body);
        Assert.Equal(source.Order(), preview.Pages.SelectMany(page => page.Blocks).SelectMany(block => block.Items).Order());
    }

    private static string[] Source() => ["Session 1", "First topic", "First explanation.", "سيشن ٢", "Second topic", "Second explanation."];

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"Section\":0,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    [InlineData("[{\"Section\":0,\"MinPageCount\":1,\"MaxPageCount\":2},{\"Section\":0,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    [InlineData("[{\"Section\":0,\"MinPageCount\":3,\"MaxPageCount\":2},{\"Section\":1,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    [InlineData("[{\"Section\":0,\"MinPageCount\":0,\"MaxPageCount\":2},{\"Section\":1,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    [InlineData("[{\"Section\":0,\"MinPageCount\":1,\"MaxPageCount\":99},{\"Section\":1,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    [InlineData("[null,{\"Section\":1,\"MinPageCount\":1,\"MaxPageCount\":2}]")]
    public void Session_ranges_must_cover_each_section_once_with_feasible_ordered_bounds(string json)
    {
        var suggestion = ContentDocumentSessions.Suggest(string.Join('\n', Source()), ContentDocumentKind.Presentation);
        var ranges = JsonSerializer.Deserialize<List<ContentDocumentSessionRange>>(json)!;
        Assert.Throws<ArgumentException>(() => ContentDocumentSessions.ValidateRanges(suggestion, ranges));
    }

    [Fact]
    public void Each_session_is_split_enough_for_its_own_maximum_and_preamble_is_kept()
    {
        const string source = "مقدمة كاملة\nSession 1\nواحد اثنان ثلاثة أربعة خمسة ستة\nSession 2\nشرح مفصل\nمثال واضح\nنقطة إضافية\nخاتمة طويلة";
        var suggestion = ContentDocumentSessions.Suggest(source, ContentDocumentKind.Presentation);
        List<ContentDocumentSessionRange> ranges = [new(0, 1, 1), new(1, 4, 5), new(2, 1, 2)];
        ContentDocumentSessions.ValidateRanges(suggestion, ranges);
        var passages = ContentDocumentSessions.SourcePassages(source, ranges);
        Assert.Equal("المقدمة قبل السيشنات", suggestion.Sections[0].Title);
        Assert.Equal(5, ContentDocumentSessions.SectionIds(passages).Count(section => section == 1));
        Assert.Equal(source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), string.Join(' ', passages).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var large = ContentDocumentSessions.Suggest($"Session 1\n{string.Join(' ', Enumerable.Repeat("word", 110))}\nSession 2\n{string.Join(' ', Enumerable.Repeat("word", 110))}", ContentDocumentKind.Presentation);
        Assert.Throws<ArgumentException>(() => ContentDocumentSessions.ValidateRanges(large, [new(0, 1, 100), new(1, 1, 100)]));
    }

    [Fact]
    public void Splitting_prose_does_not_create_a_session_from_an_inline_reference()
    {
        const string source = "Session 1\nReview Session 2 together";
        var passages = ContentDocumentSessions.SourcePassages(source, [new(0, 1, 5)]);
        Assert.Equal(5, passages.Count);
        Assert.All(ContentDocumentSessions.SectionIds(passages), section => Assert.Equal(0, section));
        Assert.Equal(source.Replace('\n', ' '), string.Join(' ', passages));
    }
}
