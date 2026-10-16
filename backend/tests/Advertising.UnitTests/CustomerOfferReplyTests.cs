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
    [InlineData("current-static-instructions-cache")]
    public async Task Current_business_and_cta_facts_are_supplied_once_with_or_without_context_cache(string? cacheId)
    {
        const string currentFacts = "القبض في الشركات المذكورة بيبدأ من ١٧٬٨٠٠ جنيه.";
        var provider = new OfferReplyGemini("{\"replyContent\":\"حجزك الحالي ثابت\"}");
        var brain = new AIMarketingBrain(provider, null!, null!, new AIBehaviorSettingsService());
        var settings = new AIBehaviorSettings { SubscribersOnlyAutoStop = true,
            Tone = new() { BusinessInstructions = currentFacts },
            Cta = new() { Enabled = true, Instructions = "اسأل عن الهدف المهني الواضح", Topics = ["المحادثة المهنية"] } };
        var cachedPrompt = cacheId is null ? "" : brain.BuildStaticPrompt("خدمة العملاء", "عامية", "الجمهور", "", aiBehaviorSettings: settings);
        await brain.AnalyzeAndGenerateReplyAsync("المعاد امتى", "test-key",
            brainContext: BookedAppointmentReplyPolicy.ContextMarker,
            customerReply: new("Gemini", "test-model", cacheId, "اقترح تغيير الموعد ومرتب قديم"),
            aiBehaviorSettings: settings);
        var completeContext = cachedPrompt + provider.LastPrompt;
        Assert.Equal(1, completeContext.Split(currentFacts).Length - 1);
        Assert.Equal(1, completeContext.Split(settings.Cta.Instructions).Length - 1);
        Assert.Equal(1, completeContext.Split(settings.Cta.Topics[0]).Length - 1);
        Assert.Contains(BookedAppointmentReplyPolicy.ContextMarker, provider.LastPrompt);
    }

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
            brainContext: "تفاصيل الكورس العادي والكورس المكثف",
            customerReply: new("Gemini", "test-model", cacheId, "اعرض السعر دائماً مع التفاصيل"),
            aiBehaviorSettings: new() { Cta = new() { Enabled = true, Topics = ["حضور السيشن المجانية"] } });

        Assert.True(provider.LastPrompt.IndexOf(CourseDiscoveryPricingPolicy.Instructions, StringComparison.Ordinal)
            > provider.LastPrompt.IndexOf("اعرض السعر دائماً مع التفاصيل", StringComparison.Ordinal));
        Assert.False(PricingGuard.RequiresExactPriceAnswer(question));
        Assert.False(result.IsFallbackResponse);
        using var services = new ServiceCollection().BuildServiceProvider();
        var engine = new HumanMessagingEngine(new ConfigurationBuilder().Build(), services);
        Assert.Equal(paragraphs, engine.SplitIntoChunks(result.ReplyContent).ToArray());
    }

    [Fact]
    public async Task Other_project_types_keep_their_existing_discovery_flow()
    {
        var provider = new OfferReplyGemini("{\"replyContent\":\"تفضل تفاصيل المنتج\"}");
        var brain = new AIMarketingBrain(provider, null!, null!, new AIBehaviorSettingsService());

        await brain.AnalyzeAndGenerateReplyAsync("ممكن تفاصيل", "test-key", brainContext: "منتجات الأثاث المتاحة");

        Assert.DoesNotContain(CourseDiscoveryPricingPolicy.Instructions, provider.LastPrompt);
    }

    [Fact]
    public void Standalone_signature_is_attached_to_the_previous_message()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var engine = new HumanMessagingEngine(new ConfigurationBuilder().Build(), services);
        Assert.Equal(["تفاصيل الكورس", "تحب تعرف المواعيد؟\n- فريق الأكاديمية"],
            engine.SplitIntoChunks("تفاصيل الكورس\n\nتحب تعرف المواعيد؟\n\n- فريق الأكاديمية").ToArray());
    }

    [Theory]
    [InlineData("WhatsApp", false)]
    [InlineData("Messenger", false)]
    [InlineData("WhatsApp", true)]
    public async Task October_4_booking_instruction_leak_is_deferred_before_any_customer_reply(string channel, bool plainText)
    {
        const string leaked = "يا فندم، العميل الذي لديه حجز لا نعرض عليه تغيير الموعد ولا نسأله إن كان يريد تغييره. وبما أن عندك حجز بالفعل في مجموعة الساعة 10، فالمسؤول هيراجع طلبك.";
        var payload = plainText ? leaked : JsonSerializer.Serialize(new {
            replyContent = leaked, suggestedGroupBookingId = "chosen-group", cancelGroupBooking = true,
        });
        var brain = new AIMarketingBrain(new OfferReplyGemini(payload), null!, null!, new AIBehaviorSettingsService());

        var reply = await brain.AnalyzeAndGenerateReplyAsync("عايزه المعاد 6 بدلا من 10", "test-key", channel: channel);

        Assert.True(reply.ShouldDeferReply);
        Assert.Empty(reply.ReplyContent);
        Assert.Null(reply.SuggestedGroupBookingId);
        Assert.False(reply.CancelGroupBooking);
    }

    [Theory]
    [InlineData("حجز حضرتك الحالي الساعة 10 ثابت زي ما هو.")]
    [InlineData("القبض في الشركات دي بيبدأ من ١٧٬٨٠٠ جنيه.")]
    [InlineData("أنا مساعد آلي وبساعد حضرتك في استفسارات الحجز والمواعيد.")]
    public async Task Verified_booking_salary_and_honest_automation_disclosure_remain_customer_replies(string customerText)
    {
        var brain = new AIMarketingBrain(new OfferReplyGemini(JsonSerializer.Serialize(new { replyContent = customerText })),
            null!, null!, new AIBehaviorSettingsService());

        var reply = await brain.AnalyzeAndGenerateReplyAsync("ممكن توضيح", "test-key", aiBehaviorSettings: new() {
            Tone = new() { BusinessInstructions = "القبض في الشركات دي بيبدأ من ١٧٬٨٠٠ جنيه. اذكر الرقم الصحيح دون ضمان راتب شخصي." },
        });

        Assert.False(reply.ShouldDeferReply);
        Assert.Equal(customerText, reply.ReplyContent);
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("publicComment")]
    [InlineData("button")]
    public void Copied_private_pricing_instructions_are_blocked_in_every_customer_output(string field)
    {
        const string directive = "لا ترسل سعر الكورس في أول خمس رسائل إلا إذا طلب العميل السعر صراحة";
        var reply = new MarketingAnalysisResult { ReplyContent = "الكورس مناسب لهدف حضرتك." };
        if (field == "reply") reply.ReplyContent = directive;
        if (field == "publicComment") reply.PublicCommentReply = directive;
        if (field == "button") reply.SuggestedButtons = [directive];

        CustomerReplyInstructionPolicy.Apply(reply, directive);

        Assert.True(reply.ShouldDeferReply);
        Assert.Empty(reply.ReplyContent);
        Assert.Null(reply.PublicCommentReply);
        Assert.Empty(reply.SuggestedButtons);
    }

    [Theory]
    [InlineData("CURRENT_BOOKING_MUST_BE_PRESERVED")]
    [InlineData("INTERNAL INSTRUCTIONS / CUSTOMER OUTPUT BOUNDARY")]
    [InlineData("Additional project-specific behavior instructions")]
    [InlineData("Never volunteer another appointment, offer to change/reschedule.")]
    public void Internal_control_tokens_and_english_booking_rules_are_not_customer_output(string leaked)
    {
        var reply = new MarketingAnalysisResult { ReplyContent = leaked };

        CustomerReplyInstructionPolicy.Apply(reply, null);

        Assert.True(reply.ShouldDeferReply);
        Assert.Empty(reply.ReplyContent);
    }

    private sealed class OfferReplyGemini(string reply) : IGeminiClient
    {
        public string LastPrompt { get; private set; } = "";
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            LastPrompt = messageContent;
            return Task.FromResult(reply);
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string text, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string systemPrompt, string modelOverride, int ttlSeconds = 3600, string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
