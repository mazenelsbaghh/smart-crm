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
        Assert.Contains("never a deck of questions", prompt);
        Assert.Contains("simple Egyptian Arabic", prompt);
        Assert.Contains("ENGLISH CLUB MODE", prompt);
        Assert.Contains("not in a sales or employee-training session", prompt);
        Assert.Contains("Both summary and mechanic MUST contain Arabic text", ContentCardGameService.BuildIdeasRetryPrompt(context, "ورشة تواصل لفريق خدمة العملاء"));
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
        Assert.Contains("Build a REAL, replayable card game", prompt);
        Assert.Contains("never a question deck", prompt);
        Assert.Contains("simple Egyptian Arabic", prompt);
        Assert.Contains("EVERY card category, title, prompt, and instruction in clear natural English", prompt);
        Assert.Contains("عدد اللاعبين", prompt);
        Assert.Contains("Use 3 to 6 distinct English card categories", prompt);
        Assert.Contains("This deck is for a live English Club", prompt);
        Assert.Contains("do NOT make a sales-training, call-center", prompt);
        Assert.Contains("STRICT RETRY", ContentCardGameService.BuildDeckRetryPrompt(Context(), input));
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
    public void Complete_image_prompts_include_every_printed_field_and_brand_reference()
    {
        var game = new ContentCardGame
        {
            Title = "Quick Response",
            BrandColorsJson = "[\"#140D2E\",\"#22E9D4\"]",
            BrandStylePrompt = "Bold editorial identity"
        };
        var card = new ContentGameCard { CardIndex = 3, Category = "Role play", Title = "Difficult opener", Prompt = "Respond in 30 seconds.", Instruction = "Tell a story." };

        var face = ContentCardGameService.BuildCardFaceImagePrompt(game, card);
        var back = ContentCardGameService.BuildCardBackImagePrompt(game);

        foreach (var copy in new[] { card.Category, card.Title, card.Prompt, card.Instruction, game.BrandColorsJson, game.BrandStylePrompt })
            Assert.Contains(copy, face);
        Assert.Contains("\"number\":4", face);
        Assert.DoesNotContain("{{", face);
        Assert.Contains("Quick Response", back);
        Assert.Contains(game.BrandColorsJson, back);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("content/project/card-games/game/back-old.png", false)]
    [InlineData("content/project/card-games/game/full-card-v1/back-new.png", true)]
    public void Legacy_backgrounds_are_not_exposed_as_complete_card_images(string? key, bool expected)
    {
        Assert.Equal(expected, ContentCardArtwork.IsCompleteImage(key));
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
