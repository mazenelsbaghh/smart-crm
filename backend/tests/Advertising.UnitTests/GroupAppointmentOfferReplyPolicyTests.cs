using Modules.AI.Services;
using Modules.GroupAppointments.Domain;
using Shared.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GroupAppointmentOfferReplyPolicyTests
{
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.CreateCustomTimeZone("Cairo test", TimeSpan.FromHours(3), "Cairo", "Cairo");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void October_2026_online_student_never_receives_the_centers_two_pm_session(bool hasOfferId)
    {
        var center = Group("offline", 11, "احمد قاسم");
        var online = Group("online", 13, "احمد قاسم");
        var analysis = new MarketingAnalysisResult
        {
            AttendanceMode = AttendanceModes.Online,
            ReplyContent = "بما إن حضرتك اخترتي الحضور أونلاين، تحبي الحصة المجانية الساعة ٢:٠٠ مساءً مع الدكتور احمد قاسم؟",
            SuggestedGroupOfferIds = hasOfferId ? [center.Id.ToString()] : [],
            SuggestedFollowUp = new() { Needed = true, Type = "AppointmentReminder" }
        };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([center, online], Cairo, "اسكندرية"));

        Assert.Contains("أونلاين", analysis.ReplyContent);
        Assert.Contains("4:00", analysis.ReplyContent);
        Assert.Contains("احمد قاسم", analysis.ReplyContent);
        Assert.DoesNotContain("2:00", analysis.ReplyContent);
        Assert.DoesNotContain("٢:٠٠", analysis.ReplyContent);
        Assert.DoesNotContain("السنتر", analysis.ReplyContent);
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.False(analysis.SuggestedFollowUp.Needed);
    }

    [Theory]
    [InlineData("Online", "online", "أونلاين")]
    [InlineData("Offline", "offline", "أوفلاين (في السنتر في الإسكندرية)")]
    public void Offered_group_supplies_its_own_mode_free_session_and_instructor(string preference, string groupMode, string expectedMode)
    {
        var group = Group(groupMode, 11, "يوسف سعد");
        group.DateTime = group.DateTime.AddDays(3);
        var analysis = new MarketingAnalysisResult
        {
            AttendanceMode = preference,
            ReplyContent = "موعد مختلف مع دكتور غير صحيح",
            SuggestedGroupOfferIds = [group.Id.ToString()]
        };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([group], Cairo, "الإسكندرية"));

        Assert.Contains(expectedMode, analysis.ReplyContent);
        Assert.Contains(FreeSessionBookingPolicy.FormatDate(group.FreeSessionDateTime!.Value), analysis.ReplyContent);
        Assert.Contains("2:00", analysis.ReplyContent);
        Assert.Contains("يوسف سعد", analysis.ReplyContent);
        Assert.DoesNotContain("دكتور غير صحيح", analysis.ReplyContent);
    }

    [Theory]
    [InlineData("Unknown", "اسكندرية", "تحب الحضور أونلاين ولا أوفلاين")]
    [InlineData("Offline", "", "هل حضرتك مقيم في الإسكندرية؟")]
    [InlineData("Offline", "القاهرة", "تحب نعرض لحضرتك مواعيد الأونلاين؟")]
    public void Missing_preference_or_ineligible_city_requires_clarification_before_offering(string preference, string city, string question)
    {
        var group = Group("offline", 11, "أحمد");
        var analysis = new MarketingAnalysisResult { AttendanceMode = preference, SuggestedGroupOfferIds = [group.Id.ToString()] };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([group], Cairo, city));

        Assert.Contains(question, analysis.ReplyContent);
        Assert.DoesNotContain("2:00", analysis.ReplyContent);
    }

    [Theory]
    [InlineData("full")]
    [InlineData("inactive")]
    [InlineData("past")]
    [InlineData("wrong mode")]
    [InlineData("unknown mode")]
    public void Unavailable_or_incompatible_offer_is_not_presented_as_bookable(string unavailableReason)
    {
        var group = Group("online", 11, "أحمد");
        if (unavailableReason == "full") group.Capacity = 0;
        if (unavailableReason == "inactive") group.IsActive = false;
        if (unavailableReason == "past") group.FreeSessionDateTime = DateTime.UtcNow.AddHours(-1);
        if (unavailableReason == "wrong mode") group.Mode = "offline";
        if (unavailableReason == "unknown mode") group.Mode = "hybrid";
        var analysis = new MarketingAnalysisResult { AttendanceMode = "Online", SuggestedGroupOfferIds = [group.Id.ToString()] };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([group], Cairo, "اسكندرية"));

        Assert.Contains("لا توجد حصة مجانية متاحة", analysis.ReplyContent);
        Assert.DoesNotContain("2:00", analysis.ReplyContent);
    }

    [Fact]
    public void Nurturing_remains_scheduled_without_repeating_an_unverified_appointment()
    {
        var group = Group("online", 13, "أحمد");
        var analysis = new MarketingAnalysisResult
        {
            AttendanceMode = "Online", SuggestedGroupOfferIds = [group.Id.ToString()],
            SuggestedFollowUp = new() { Needed = true, Type = "Nurturing", DueDate = "2026-10-18T10:00:00Z", Notes = "الحصة أونلاين الساعة ٢ مع دكتور غير صحيح" }
        };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([group], Cairo, "اسكندرية"));

        Assert.True(analysis.SuggestedFollowUp.Needed);
        Assert.Equal("2026-10-18T10:00:00Z", analysis.SuggestedFollowUp.DueDate);
        Assert.DoesNotContain("٢", analysis.SuggestedFollowUp.Notes);
        Assert.DoesNotContain("دكتور غير صحيح", analysis.SuggestedFollowUp.Notes);
    }

    [Fact]
    public void Invalid_intensive_offer_does_not_fall_back_to_regular_online_course()
    {
        var intensive = Group("offline", 11, "هشام");
        intensive.CourseType = CourseTypes.Intensive;
        var regular = Group("online", 13, "أحمد");
        var analysis = new MarketingAnalysisResult { AttendanceMode = "Online", SuggestedGroupOfferIds = [intensive.Id.ToString()] };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([intensive, regular], Cairo, "اسكندرية"));

        Assert.Contains("لا توجد حصة مجانية متاحة", analysis.ReplyContent);
        Assert.DoesNotContain("أحمد", analysis.ReplyContent);
    }

    [Fact]
    public void Unidentified_offer_cannot_mix_course_types()
    {
        var regular = Group("online", 13, "أحمد");
        var intensive = Group("online", 15, "هشام");
        intensive.CourseType = CourseTypes.Intensive;
        var analysis = new MarketingAnalysisResult { AttendanceMode = "Online", ReplyContent = "الحصة المجانية الساعة ٢ مساءً" };

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([regular, intensive], Cairo, "اسكندرية"));

        Assert.Contains("العادي ولا المكثف", analysis.ReplyContent);
        Assert.DoesNotContain("الساعة", analysis.ReplyContent);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("booking")]
    [InlineData("human")]
    [InlineData("fallback")]
    public void Non_offer_flows_preserve_the_existing_reply(string flow)
    {
        var group = Group("online", 13, "أحمد");
        var analysis = new MarketingAnalysisResult { ReplyContent = "تحت أمرك يا فندم" };
        if (flow != "support") analysis.SuggestedGroupOfferIds = [group.Id.ToString()];
        if (flow == "booking") analysis.SuggestedGroupBookingId = group.Id.ToString();
        if (flow == "human") analysis.RequestHuman = true;
        if (flow == "fallback") analysis.IsFallbackResponse = true;

        GroupAppointmentOfferReplyPolicy.Apply(analysis, new([group], Cairo, "اسكندرية"));

        Assert.Equal("تحت أمرك يا فندم", analysis.ReplyContent);
    }

    private static GroupAppointment Group(string mode, int utcHour, string instructor) => new()
    {
        Id = Guid.NewGuid(),
        Mode = mode,
        Capacity = 10,
        InstructorName = instructor,
        FreeSessionDateTime = DateTime.UtcNow.Date.AddDays(7).AddHours(utcHour),
        DateTime = DateTime.UtcNow.Date.AddDays(7).AddHours(utcHour)
    };
}
