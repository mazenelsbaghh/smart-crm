using Modules.AI.Services;
using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AiGroupBookingReplyPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preparation_confirmation_explains_joining_the_group_and_ends_with_one_immediate_trial_link(bool shouldOfferTrial)
    {
        var reply = AiGroupBookingReplyPolicy.Apply(
            "تم حجز السيشن يوم السبت\n\nجرّب السايت دلوقتي\n" + TalkTipsTrialCtaInstructions.TrialUrl,
            shouldOfferTrial, new(true, 6), shouldPrepareBeforeSession: true);

        Assert.Contains("يوم السبت", reply);
        Assert.Contains("أونلاين أو في السنتر", reply);
        Assert.Contains("تحديد المستوى", reply);
        Assert.Contains("قبل السيشن بيومين", reply);
        Assert.Contains("عشان يدخل", reply);
        Assert.Contains("تذكير السايت قبلها بيوم", reply);
        Assert.EndsWith("جرب الويبسايت من هنا:\n" + TalkTipsTrialCtaInstructions.TrialUrl, reply);
        Assert.Equal(1, reply.Split(TalkTipsTrialCtaInstructions.TrialUrl).Length - 1);
        Assert.DoesNotContain("جرّب السايت دلوقتي", reply);
    }

    [Fact]
    public void Trial_link_on_the_same_line_as_confirmation_does_not_remove_the_booking_details()
    {
        var reply = AiGroupBookingReplyPolicy.Apply("تم حجز السيشن يوم السبت " + TalkTipsTrialCtaInstructions.TrialUrl,
            false, new(true, 6), true);
        Assert.Contains("يوم السبت", reply);
        Assert.EndsWith(TalkTipsTrialCtaInstructions.TrialUrl, reply);
        Assert.Equal(1, reply.Split(TalkTipsTrialCtaInstructions.TrialUrl).Length - 1);
    }

    [Fact]
    public void Partial_booking_uses_truthful_override_without_trial_cta()
    {
        var result = new AiGroupBookingResult(
            true,
            0,
            AiGroupBookingFailure.GroupFull)
        {
            BookedPeople = ["الشخص الأول"],
            UnbookedPeople = ["الشخص الثاني"]
        };

        var reply = AiGroupBookingReplyPolicy.Apply(
            "تم حجز الجميع",
            shouldOfferTrial: true,
            result);

        Assert.Equal(result.CustomerReplyOverride, reply);
        Assert.Contains("الشخص الأول", reply, StringComparison.Ordinal);
        Assert.Contains("الشخص الثاني", reply, StringComparison.Ordinal);
        Assert.DoesNotContain(TalkTipsTrialCtaInstructions.TrialUrl, reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_booking_can_unlock_trial_cta()
    {
        var result = new AiGroupBookingResult(
            true,
            2,
            AiGroupBookingFailure.None);

        var reply = AiGroupBookingReplyPolicy.Apply(
            "تم الحجز",
            shouldOfferTrial: true,
            result);

        Assert.Contains("تم الحجز", reply, StringComparison.Ordinal);
        Assert.Contains(TalkTipsTrialCtaInstructions.TrialUrl, reply, StringComparison.Ordinal);
    }
}
