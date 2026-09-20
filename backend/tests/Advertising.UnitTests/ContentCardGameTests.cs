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
        Assert.Contains("Do not offer generic games", prompt);
        Assert.Contains("clear, natural English", prompt);
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

        Assert.Contains($"Required card count: {cardCount}", prompt);
        Assert.Contains($"Return exactly {cardCount} cards", prompt);
        Assert.Contains("Write every player-facing value in clear, natural English", prompt);
        Assert.Contains("\"cards\"", prompt);
        Assert.DoesNotContain("{{", prompt);
    }

    [Fact]
    public void Json_parser_accepts_an_object_wrapped_in_model_formatting()
    {
        var parsed = ContentCardGameService.ParseJson<IdeaResponse>("""
            هنا الأفكار المطلوبة:
            ```json
            {"title":"اختبار"}
            ```
            """);

        Assert.Equal("اختبار", parsed.Title);
    }

    [Fact]
    public void Image_prompts_require_text_free_art_and_preserve_the_real_logo_in_the_application()
    {
        var game = new ContentCardGame
        {
            Title = "Quick Response",
            BrandColorsJson = "[\"#140D2E\",\"#22E9D4\"]",
            BrandStylePrompt = "Bold editorial identity"
        };
        var card = new ContentGameCard { Category = "Role play", Title = "Difficult opener", Prompt = "Respond in 30 seconds." };

        var face = ContentCardGameService.BuildCardFaceImagePrompt(game, card);
        var back = ContentCardGameService.BuildCardBackImagePrompt(game);

        Assert.Contains("no text, no glyphs, no numbers", face);
        Assert.Contains("application will place the exact logo", face);
        Assert.Contains("no logo recreation", back);
        Assert.Contains("Quick Response", back);
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

    private sealed record IdeaResponse(string Title);
}
