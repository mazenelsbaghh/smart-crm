using System.Globalization;
using System.Text.Json;
using Modules.Content.Domain;
using Modules.Content.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentDocumentPaginationTests
{
    [Theory]
    [InlineData("Title: Why Call Center? (ليه كول سنتر؟)", "Why Call Center? (ليه كول سنتر؟)")]
    [InlineData("  TITLE : Why?", "Why?")]
    [InlineData("العنوان: مهارات التواصل\r\nعنوان فرعي: الاستماع", "مهارات التواصل\r\nالاستماع")]
    [InlineData("Subtitle — Listening\nHeading: Details", "Listening\nDetails")]
    [InlineData("**Title:** Welcome\n**Title**: Next\n__عنوان:__ أهلاً", "Welcome\nNext\nأهلاً")]
    [InlineData("### Title: Welcome\nتايتل: أهلاً", "Welcome\nأهلاً")]
    [InlineData("Title:\r\nWelcome", "\r\nWelcome")]
    [InlineData("Title: عنوان: Welcome", "Welcome")]
    [InlineData("Title insurance\nعنوان العميل مهم\nExplain the title: exactly.\nhttps://example.com/title:test", "Title insurance\nعنوان العميل مهم\nExplain the title: exactly.\nhttps://example.com/title:test")]
    [InlineData("", "")]
    public void Authoring_title_labels_are_hidden_without_changing_meaningful_copy(string source, string expected)
    {
        var cleaned = ContentDocumentText.WithoutTitleLabels(source);
        Assert.Equal(expected, cleaned);
        Assert.Equal(cleaned, ContentDocumentText.WithoutTitleLabels(cleaned));
    }

    [Fact]
    public void Cover_uses_actual_heading_after_a_standalone_title_label()
    {
        Assert.Equal("Why Call Center?", ContentDocumentGenerationService.SourceTitle("Title:\nTitle: Why Call Center?\nOriginal details."));
        Assert.Equal(["Why Call Center?", "Original details."],
            ContentDocumentPreview.SourcePassages("Title:\nTitle: Why Call Center?\nOriginal details.", 2));
        Assert.Throws<ArgumentException>(() => ContentDocumentPreview.SourcePassages("Title:\nالعنوان:", 2));
    }

    [Fact]
    public void Previously_saved_title_labels_are_not_sent_as_visible_text_on_regeneration()
    {
        var prompt = ContentDocumentGenerationService.BuildImagePrompt(new ContentDocument(), new ContentDocumentPage
        {
            Title = "Title: Why Call Center?", Body = "عنوان فرعي: الاستماع\nThe title stays inside this sentence."
        });
        Assert.Contains("<exact_title>Why Call Center?</exact_title>", prompt);
        Assert.Contains("<exact_body>الاستماع\nThe title stays inside this sentence.</exact_body>", prompt);
    }

    [Theory]
    [InlineData(ContentDocumentKind.Presentation, 1)]
    [InlineData(ContentDocumentKind.Presentation, 300)]
    [InlineData(ContentDocumentKind.A4, 1)]
    [InlineData(ContentDocumentKind.A4, 300)]
    public void Cover_is_followed_by_the_complete_original_text_in_order(ContentDocumentKind kind, int repetitions)
    {
        var source = "  \r\n" + string.Concat(Enumerable.Repeat(
            "التفصيل الأول: السِّعر ١٢٣٫٤٥ جنيه، بلا تغيير.\r\n\r\nالتفصيل الثاني: مثال English 👩🏽‍💻 مع رابط https://example.com/a?b=1.\n", repetitions)) + "  ";

        var pages = ContentDocumentPagination.CreatePageBodies(source, kind);

        Assert.Empty(pages[0]);
        Assert.Equal(source, string.Concat(pages.Skip(1)));
        Assert.All(pages.Skip(1), page => Assert.InRange(page.Length, 1, 1_800));
        if (repetitions == 1) Assert.Equal(2, pages.Count);
        else Assert.True(pages.Count > (kind == ContentDocumentKind.Presentation ? 30 : 20));
    }

    [Theory]
    [InlineData("أ", 700)]
    [InlineData("أ", 701)]
    [InlineData("👩🏽‍💻", 250)]
    [InlineData("بِ", 500)]
    public void Long_unbroken_text_keeps_unicode_characters_intact(string element, int repetitions)
    {
        var source = string.Concat(Enumerable.Repeat(element, repetitions));
        var boundaries = StringInfo.ParseCombiningCharacters(source).ToHashSet();

        var pages = ContentDocumentPagination.CreatePageBodies(source, ContentDocumentKind.Presentation);

        Assert.Equal(source, string.Concat(pages));
        var offset = 0;
        foreach (var page in pages.Skip(1))
        {
            Assert.Contains(offset, boundaries);
            Assert.InRange(page.Length, 1, 700);
            offset += page.Length;
        }
    }

    [Theory]
    [InlineData(ContentDocumentKind.Presentation)]
    [InlineData(ContentDocumentKind.A4)]
    public void Every_selectable_count_preserves_original_text_and_unicode(ContentDocumentKind kind)
    {
        var source = "  \r\n" + string.Concat(Enumerable.Repeat("السِّعر ١٢٣٫٤٥ جنيه. English 👩🏽‍💻 example.\n", 45)) + "  ";
        var suggestion = ContentDocumentPagination.Suggest(source, kind);
        var boundaries = StringInfo.ParseCombiningCharacters(source).ToHashSet();
        Assert.True(suggestion.MinPageCount < suggestion.PageCount);
        Assert.True(suggestion.MaxPageCount > suggestion.PageCount);
        for (var count = suggestion.MinPageCount; count <= suggestion.MaxPageCount; count++)
        {
            var pages = ContentDocumentPagination.CreatePageBodies(source, kind, count);
            Assert.Equal(count, pages.Count);
            Assert.Empty(pages[0]);
            Assert.Equal(source, string.Concat(pages));
            var offset = 0;
            foreach (var page in pages.Skip(1))
            {
                Assert.False(string.IsNullOrWhiteSpace(page));
                Assert.InRange(page.Length, 1, 4_000);
                Assert.Contains(offset, boundaries);
                offset += page.Length;
            }
        }
        Assert.Throws<ArgumentException>(() => ContentDocumentPagination.CreatePageBodies(source, kind, suggestion.MinPageCount - 1));
        Assert.Throws<ArgumentException>(() => ContentDocumentPagination.CreatePageBodies(source, kind, suggestion.MaxPageCount + 1));
    }

    [Theory]
    [InlineData("{\"pages\":[{\"blocks\":[{\"type\":\"paragraph\",\"ids\":[0,0,1]}]}]}")]
    [InlineData("{\"pages\":[{\"blocks\":[{\"type\":\"paragraph\",\"ids\":[0]}]}]}")]
    [InlineData("{\"pages\":[{\"blocks\":[{\"type\":\"paragraph\",\"ids\":[0,1,2]}]}]}")]
    [InlineData("{\"pages\":[{\"blocks\":[{\"type\":\"summary\",\"ids\":[0,1]}]}]}")]
    [InlineData("{\"pages\":[{\"blocks\":[]} ]}")]
    public void Ai_outline_must_use_every_source_passage_once_without_inventing_or_summarizing(string json)
    {
        var outline = JsonSerializer.Deserialize<ContentDocumentPreview.Outline>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Throws<InvalidOperationException>(() => ContentDocumentPreview.Create("الغلاف", ["تفصيل أول", "تفصيل ثانٍ"], outline, 2));
    }

    [Fact]
    public void Source_passages_keep_all_words_when_long_paragraphs_need_more_slides()
    {
        var source = "  \r\n" + new string('أ', 1_200) + " " + string.Concat(Enumerable.Repeat("السِّعر ١٢٣٫٤٥ جنيه. English 👩🏽‍💻 https://example.com.\n", 40));
        var passages = ContentDocumentPreview.SourcePassages(source, 70);
        Assert.True(passages.Count >= 69);
        var words = System.Text.RegularExpressions.Regex.Split(source.Replace("👩🏽‍💻 ", string.Empty).Trim(), @"\s+");
        Assert.Equal(words, passages.SelectMany(passage => System.Text.RegularExpressions.Regex.Split(passage, @"\s+")));
    }

    [Fact]
    public void Planner_cannot_replace_source_text_with_a_summary_or_put_it_on_the_cover()
    {
        var source = string.Concat(Enumerable.Repeat("  هذا تفصيل أصلي يجب الاحتفاظ به، حتى لو حاول النموذج تلخيصه.\n", 30));
        var sourcePages = ContentDocumentPagination.CreatePageBodies(source, ContentDocumentKind.Presentation);
        var response = JsonSerializer.Serialize(new
        {
            title = "دليل التفاصيل",
            pages = sourcePages.Select(_ => new { title = "عنوان", body = "ملخص بديل", imagePrompt = "Editorial layout" })
        });

        var plan = ContentDocumentGenerationService.ParsePlan(response, sourcePages);

        Assert.Empty(plan.Pages[0].Body);
        Assert.Equal("دليل التفاصيل", plan.Pages[0].Title);
        Assert.Equal(source, string.Concat(plan.Pages.Skip(1).Select(page => page.Body)));
        Assert.DoesNotContain(plan.Pages, page => page.Body == "ملخص بديل");
    }
}
