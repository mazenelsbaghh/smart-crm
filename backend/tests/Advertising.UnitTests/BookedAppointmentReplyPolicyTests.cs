using Modules.AI.Services;
using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class BookedAppointmentReplyPolicyTests
{
    [Theory]
    [InlineData("المعاد امتى", false)]
    [InlineData("عايز احجز لصاحبي", false)]
    [InlineData("في مواعيد تانية؟", true)]
    [InlineData("عايز أغير ميعاد الحجز", true)]
    [InlineData("مش عايز أغير ميعاد الحجز", false)]
    [InlineData("another time please", true)]
    [InlineData("عايزه المعاد 6 بدلا من 10", true)]
    [InlineData("عايزه المعاد ٦ بدل ١٠", true)]
    public void Other_times_require_a_customer_request(string request, bool expected) =>
        Assert.Equal(expected, BookedAppointmentReplyPolicy.AllowsOtherTimes(request, []));

    [Fact]
    public void A_recent_request_can_authorize_a_selection_but_a_current_refusal_overrides_it()
    {
        Assert.True(BookedAppointmentReplyPolicy.AllowsOtherTimes("احجز الخميس", ["في مواعيد تانية؟"]));
        Assert.False(BookedAppointmentReplyPolicy.AllowsOtherTimes("مش عايز أغير ميعاد الحجز", ["في مواعيد تانية؟"]));
        Assert.False(BookedAppointmentReplyPolicy.AllowsTransfer("في مواعيد تانية؟", []));
        Assert.True(BookedAppointmentReplyPolicy.AllowsTransfer("احجز الخميس", ["في مواعيد تانية؟"]));
    }

    [Theory]
    [InlineData("عايزه المعاد 6 بدلا من 10", true)]
    [InlineData("عاوزه الساعة ٦ بدل ١٠", true)]
    [InlineData("هو المعاد 6 بدلا من 10؟", false)]
    [InlineData("عايز أعرف هو المعاد 6 بدلا من 10؟", false)]
    [InlineData("المعاد امتى", false)]
    [InlineData("مش عايز أغير المعاد", false)]
    public void October_4_explicit_replacement_time_is_authorized_but_questions_are_not(string message, bool expected) =>
        Assert.Equal(expected, BookedAppointmentReplyPolicy.AllowsTransfer(message, []));

    [Fact]
    public void Unsolicited_changes_are_removed_without_losing_the_current_date_or_final_trial_message()
    {
        var reply = BookedAppointmentReplyPolicy.PreserveBooking(
            "حجزك يوم السبت. تحب معاد تاني؟\n\nهيوصلك لينك الجروب.\n\nجرب الويبسايت من هنا:\n" + TalkTipsTrialCtaInstructions.TrialUrl);
        Assert.Contains("حجزك يوم السبت", reply);
        Assert.DoesNotContain("معاد تاني", reply);
        Assert.Contains("\n\nجرب الويبسايت", reply);
        Assert.EndsWith(TalkTipsTrialCtaInstructions.TrialUrl, reply);
    }

    [Fact]
    public void Work_salary_is_preserved_during_discovery_while_course_fees_are_removed()
    {
        var reply = CourseDiscoveryPricingPolicy.Apply("القبض في الشركات دي بيبدأ من ١٧٬٨٠٠ جنيه.\n\nسعر الكورس ٢٠٠٠ جنيه.", "عايز أحسن الإنجليزي للشغل", 0);
        Assert.Contains("١٧٬٨٠٠ جنيه", reply);
        Assert.DoesNotContain("٢٠٠٠", reply);
    }

    [Theory]
    [InlineData("المعاد امتى", false)]
    [InlineData("ألغي حجزى", true)]
    [InlineData("مش عايز ألغي الحجز", false)]
    [InlineData("خلاص الغي الحجز", true)]
    [InlineData("متلغيش الحجز", false)]
    [InlineData("لا الغي الحجز", false)]
    public void Cancellation_requires_a_current_explicit_request(string message, bool expected) =>
        Assert.Equal(expected, BookedAppointmentReplyPolicy.RequestsCancellation(message));

    [Theory]
    [InlineData("تمام", false)]
    [InlineData("عايز أغير ميعاد الحجز", true)]
    public void A_later_refusal_revokes_old_change_permission_until_another_explicit_request(string current, bool expected)
    {
        Assert.Equal(expected, BookedAppointmentReplyPolicy.AllowsOtherTimes(current,
            ["فيه معاد تاني؟", "مش عايز أغير معادي"]));
        if (!expected)
            Assert.False(BookedAppointmentReplyPolicy.AllowsTransfer(current,
                ["فيه معاد تاني؟", "مش عايز أغير معادي"]));
    }
}
