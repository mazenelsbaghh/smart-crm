using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class TalkTipsTrialCtaInstructionsTests
{
    [Fact]
    public void Instructions_GateTrialUrlUntilBookingSucceeds()
    {
        var instructions = TalkTipsTrialCtaInstructions.ForCustomerWhoHasNotTried();

        Assert.DoesNotContain(TalkTipsTrialCtaInstructions.TrialUrl, instructions, StringComparison.Ordinal);
        Assert.Contains("BOOKING-GATED", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureCta_AppendsTrialLinkOnlyWhenMissing()
    {
        var withCta = TalkTipsTrialCtaInstructions.EnsureCta("رد طبيعي للعميل");
        var unchanged = TalkTipsTrialCtaInstructions.EnsureCta($"جرّب هنا {TalkTipsTrialCtaInstructions.TrialUrl}");

        Assert.Contains(TalkTipsTrialCtaInstructions.TrialUrl, withCta, StringComparison.Ordinal);
        Assert.Equal(1, withCta.Split(TalkTipsTrialCtaInstructions.TrialUrl).Length - 1);
        Assert.Equal($"جرّب هنا {TalkTipsTrialCtaInstructions.TrialUrl}", unchanged);
    }

    [Theory]
    [InlineData(1, "٥")]
    [InlineData(4, "٥")]
    [InlineData(5, "٥")]
    [InlineData(6, "٦")]
    [InlineData(7, "٧")]
    [InlineData(8, "٧")]
    [InlineData(19, "٧")]
    // Production incident 2026-08-28: actual availability 36 must be presented as 7.
    [InlineData(36, "٧")]
    public void Production_2026_08_28_customer_facing_availability_is_bounded_between_five_and_seven(
        int actualRemainingPlaces,
        string expectedDisplayedPlaces)
    {
        var reply = TalkTipsTrialCtaInstructions.AfterSuccessfulBooking(
            "تم حجزك",
            actualRemainingPlaces);

        Assert.Contains($"فاضل {expectedDisplayedPlaces} أماكن", reply, StringComparison.Ordinal);
        Assert.Contains("أصحابك أو قرايبك", reply, StringComparison.Ordinal);
        Assert.Contains("اسمه ورقم موبايله", reply, StringComparison.Ordinal);
        Assert.Contains(TalkTipsTrialCtaInstructions.TrialUrl, reply, StringComparison.Ordinal);
    }

    [Fact]
    public void FullGroup_OmitsCompanionInvitationButStillUnlocksBookedCustomersTrial()
    {
        var reply = TalkTipsTrialCtaInstructions.AfterSuccessfulBooking("تم حجز آخر مكان", 0);

        Assert.DoesNotContain("فاضل", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("أصحابك أو قرايبك", reply, StringComparison.Ordinal);
        Assert.Contains(TalkTipsTrialCtaInstructions.TrialUrl, reply, StringComparison.Ordinal);
    }
}
