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

        var reply = PricingGuard.BuildPricingReplyFromKnowledge(approvedKnowledge);

        Assert.NotNull(reply);
        Assert.Contains("1500 جنيه مصري شهرياً", reply, StringComparison.Ordinal);
        Assert.Contains("8000 جنيه مصري", reply, StringComparison.Ordinal);
    }
}
