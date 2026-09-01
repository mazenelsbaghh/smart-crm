using Modules.AI.Services;
using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AiGroupBookingReplyPolicyTests
{
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
