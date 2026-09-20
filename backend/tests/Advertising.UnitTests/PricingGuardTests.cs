using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class PricingGuardTests
{
    [Theory]
    [InlineData("انا عندي مشكله اني خايف و مش ضامن ادفع فلوسي متجيش بفايده")]
    [InlineData("أنا خايف أدفع فلوسي وفي الآخر ماستفدش")]
    [InlineData("لو دفعت ومجاش فايدة أعمل إيه؟")]
    [InlineData("عايز أدفع بس مش ضامن أستفيد")]
    [InlineData("ممكن أدفع بس خايف الفلوس تضيع")]
    [InlineData("هأدفع بالكاش")]
    [InlineData("I want to pay by card")]
    public void Production_2026_08_28_trust_objections_and_payment_statements_are_not_pricing_questions(string message)
    {
        Assert.False(PricingGuard.IsPricingQuestion(message));
    }

    [Theory]
    [InlineData("السعر كام؟")]
    [InlineData("الاشتراك بكام؟")]
    [InlineData("هل فيه تقسيط؟")]
    [InlineData("ممكن أدفع على أقساط؟")]
    [InlineData("إيه طرق الدفع المتاحة؟")]
    [InlineData("هل ينفع أدفع بالكاش؟")]
    [InlineData("تكلفة الكورس كام وهل فيه مصاريف إضافية؟")]
    [InlineData("How much does it cost?")]
    [InlineData("What payment plans do you offer?")]
    [InlineData("Can I pay by card?")]
    public void Explicit_price_or_payment_plan_questions_are_pricing_questions(string message)
    {
        Assert.True(PricingGuard.IsPricingQuestion(message));
    }

    [Fact]
    public void Trusted_pricing_knowledge_is_rendered_with_its_configured_prices()
    {
        const string approvedKnowledge = """
            معلومات البيع المعتمدة:
            الاشتراك الشهري: 1500 جنيه مصري شهرياً
            عرض الكاش للكورس كامل: 8000 جنيه مصري
            """;

        var reply = PricingGuard.BuildPricingReplyFromKnowledge("السعر كام؟", approvedKnowledge);

        Assert.NotNull(reply);
        Assert.Contains("1500 جنيه مصري شهرياً", reply, StringComparison.Ordinal);
        Assert.Contains("8000 جنيه مصري", reply, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("هل فيه أي مصاريف إضافية تانية هدفعها")]
    [InlineData("الكتب ليها مصاريف؟")]
    [InlineData("إيه طرق الدفع المتاحة؟")]
    [InlineData("هل ينفع أدفع بالكاش؟")]
    public void Production_2026_09_05_contextual_payment_questions_are_left_to_the_ai(string message)
    {
        Assert.False(PricingGuard.RequiresExactPriceAnswer(message));
    }

    [Theory]
    [InlineData("السيشن المجانية بقى امتى آخر سؤال")]
    [InlineData("حضرتك بتقولي فيه سيشن مجانية تعريفية عن الكورس بسألك امتى هتكون معادها")]
    [InlineData("السعر عرفته خلاص، معاد السيشن امتى؟")]
    [InlineData("السعر كام والمواعيد امتى؟")]
    [InlineData("عرفت السعر خلاص")]
    [InlineData("What does it cost and when is the free session?")]
    [InlineData("لو سمحت عاوزه اعرف تفاصيل الكورس و بكام")]
    [InlineData("ممكن معلومات عن الكورس والسعر")]
    [InlineData("محتوى الكورس إيه وتكلفته كام؟")]
    [InlineData("What are the course details and how much does it cost?")]
    public void Production_2026_09_08_session_questions_are_not_replaced_with_monthly_price(string message)
    {
        Assert.False(PricingGuard.RequiresExactPriceAnswer(message));
    }

    [Theory]
    [InlineData("السعر كام؟")]
    [InlineData("الاشتراك بكام؟")]
    [InlineData("تكلفة الأربع شهور كام؟")]
    [InlineData("How much does it cost?")]
    public void Direct_price_questions_use_trusted_knowledge(string message)
    {
        Assert.True(PricingGuard.RequiresExactPriceAnswer(message));
    }

    [Fact]
    public void Full_course_question_returns_the_full_course_price_without_an_unrelated_cta()
    {
        const string approvedKnowledge = """
            الاشتراك الشهري: 1500 جنيه مصري شهرياً
            عرض الكاش للكورس كامل: 4500 جنيه مصري
            """;

        var reply = PricingGuard.BuildPricingReplyFromKnowledge(
            "تكلفة الأربع شهور كام؟",
            approvedKnowledge);

        Assert.Equal("سعر الكورس بالكامل كاش هو 4500 جنيه مصري.", reply);
    }
}
