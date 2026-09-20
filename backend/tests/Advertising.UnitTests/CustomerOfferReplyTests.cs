using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.WhatsApp.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CustomerOfferReplyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("cached-before-format-fix")]
    public async Task Details_and_price_preserve_three_generated_messages_with_or_without_cache(string? cacheId)
    {
        const string question = "لو سمحت عاوزه اعرف تفاصيل الكورس و بكام";
        string[] paragraphs = [
            "أهلاً بحضرتك، الكورس بيجمع تدريب اللغة مع ممارسة المحادثة والاستعداد للمقابلات. " +
            "بنراجع التطبيقات مع حضرتك ونوضح الأخطاء اللي محتاجة تدريب. " +
            "المحتوى والتدريبات بيتقدموا حسب خطة الدراسة المعتمدة. " +
            "وكل جزء مرتبط بتطبيق عملي يساعد حضرتك تتابع التقدم في المهارات.",
            "الدراسة سيشنين في الأسبوع، والاشتراك الشهري 1500 جنيه حسب بيانات الأكاديمية.",
            "تحبي نعرض لحضرتك مواعيد السيشن المجانية المناسبة للأونلاين؟\n- فريق الأكاديمية"
        ];
        var provider = new OfferReplyGemini(JsonSerializer.Serialize(new { replyContent = string.Join("\n\n", paragraphs) }));
        var brain = new AIMarketingBrain(provider, null!, null!, new AIBehaviorSettingsService());
        var result = await brain.AnalyzeAndGenerateReplyAsync(question, "test-key",
            customerReply: new("Gemini", "test-model", cacheId),
            aiBehaviorSettings: new() { Cta = new() { Enabled = true, Topics = ["حضور السيشن المجانية"] } });

        Assert.False(PricingGuard.RequiresExactPriceAnswer(question));
        Assert.False(result.IsFallbackResponse);
        using var services = new ServiceCollection().BuildServiceProvider();
        var engine = new HumanMessagingEngine(new ConfigurationBuilder().Build(), services);
        Assert.Equal(paragraphs, engine.SplitIntoChunks(result.ReplyContent).ToArray());
    }

    [Fact]
    public void Standalone_signature_is_attached_to_the_previous_message()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var engine = new HumanMessagingEngine(new ConfigurationBuilder().Build(), services);
        Assert.Equal(["تفاصيل الكورس", "تحب تعرف المواعيد؟\n- فريق الأكاديمية"],
            engine.SplitIntoChunks("تفاصيل الكورس\n\nتحب تعرف المواعيد؟\n\n- فريق الأكاديمية").ToArray());
    }

    private sealed class OfferReplyGemini(string reply) : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            return Task.FromResult(reply);
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string text, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string systemPrompt, string modelOverride, int ttlSeconds = 3600, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
