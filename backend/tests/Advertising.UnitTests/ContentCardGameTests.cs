using Modules.Content.Domain;
using Modules.Content.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentCardGameTests
{
    [Fact]
    public void Idea_prompt_substitutes_brand_and_workshop_context_into_json_contract()
    {
        var context = Context();

        var prompt = ContentCardGameService.BuildIdeasPrompt(context, "ورشة تواصل لفريق خدمة العملاء");

        Assert.Contains("TalkTips", prompt);
        Assert.Contains("#22E9D4", prompt);
        Assert.Contains("ورشة تواصل لفريق خدمة العملاء", prompt);
        Assert.Contains("دليل خدمة العملاء المنشور", prompt);
        Assert.Contains("لا تقترح أفكارًا عامة", prompt);
        Assert.Contains("\"ideas\"", prompt);
        Assert.DoesNotContain("{{", prompt);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(60)]
    public void Deck_prompt_requires_the_selected_number_of_cards(int cardCount)
    {
        var input = new CreateCardGameInput("رد بسرعة", "تدريب الفريق على الردود العملية", "إجابة خلال 30 ثانية", cardCount);

        var prompt = ContentCardGameService.BuildDeckPrompt(Context(), input);

        Assert.Contains($"عدد الكروت الإلزامي: {cardCount}", prompt);
        Assert.Contains($"أعد {cardCount} عنصرًا بالضبط", prompt);
        Assert.Contains("\"cards\"", prompt);
        Assert.DoesNotContain("{{", prompt);
    }

    private static ContentCardGameService.GenerationContext Context() => new(
        "TalkTips",
        new ContentAutomationSettings
        {
            BrandColorsJson = "[\"#140D2E\",\"#22E9D4\"]",
            StylePrompt = "هوية تحريرية جريئة"
        },
        "not-used-in-prompt-test",
        "gemini-test",
        "دليل خدمة العملاء المنشور");
}
