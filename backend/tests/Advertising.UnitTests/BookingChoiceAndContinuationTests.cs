using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.TalkTips.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class BookingChoiceAndContinuationTests
{
    [Theory]
    [InlineData("الساعه ٨", 11, true)]
    [InlineData("٨", 11, true)]
    [InlineData("فهكون موجودة ٨ إن شاء الله", 11, true)]
    [InlineData("الساعة 8", 17, false)]
    [InlineData("عايزه المعاد ٨ بدلا من ٢، من فضلك", 17, false)]
    [InlineData("مشيرة أحمد، اسكندرية، الساعة ٨", 17, false)]
    [InlineData("مشغول الساعة ٨", 17, true)]
    [InlineData("الساعه ٨:٣٠", 17, true)]
    [InlineData("الساعة 20:00", 17, false)]
    [InlineData("الساعة ٨ صباحا", 17, true)]
    [InlineData("الساعة ٨ مساء", 17, false)]
    [InlineData("8 pm", 17, false)]
    [InlineData("مش الساعة ٨", 17, true)]
    [InlineData("الساعة ٨ او الساعة ٢", 11, true)]
    [InlineData("فين المكان", 11, false)]
    public void Production_Oct17_explicit_time_never_silently_becomes_another_slot(string choice, int utcHour, bool conflict)
    {
        var group = new GroupAppointment { FreeSessionDateTime = new DateTime(2026, 10, 19, utcHour, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(conflict, GroupBookingTimeChoicePolicy.Conflicts(group, "Africa/Cairo", [choice, "المكان فين"]));
    }

    [Fact]
    public void Latest_explicit_choice_replaces_earlier_choice_without_reading_assistant_times()
    {
        var group = new GroupAppointment { FreeSessionDateTime = new DateTime(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc) };
        Assert.False(GroupBookingTimeChoicePolicy.Conflicts(group, "Africa/Cairo", ["الساعة ٢", "الساعة ٨", "فين المكان"]));
    }

    [Fact]
    public void Collecting_booking_details_does_not_erase_the_students_eight_pm_choice()
    {
        var group = new GroupAppointment { FreeSessionDateTime = new DateTime(2026, 10, 19, 11, 0, 0, DateTimeKind.Utc) };
        string[] messages = ["الساعة ٨", "إسراء", "الإسكندرية", "01012345678", "في السنتر", "فين المكان", "تمام"];
        Assert.True(GroupBookingTimeChoicePolicy.Conflicts(group, "Africa/Cairo", messages));
    }

    [Fact]
    public void Successful_booking_replaces_hallucinated_confirmation_with_committed_schedule()
    {
        var result = new AiGroupBookingResult(true, 3) { BookedPeople = ["إسراء"],
            ConfirmedAppointmentDetails = "الحضور: في السنتر في الإسكندرية\nميعاد الحصة المجانية: السبت الساعة 8:00 م\nيوسف سعد" };
        var reply = AiGroupBookingReplyPolicy.Apply("تم الحجز الساعة ٢ مع احمد قاسم", false, result);
        Assert.Contains("8:00", reply);
        Assert.Contains("إسراء", reply);
        Assert.Contains("يوسف سعد", reply);
        Assert.DoesNotContain("احمد قاسم", reply);
        Assert.DoesNotContain("الساعة ٢", reply);
    }

    [Fact]
    public void Production_Oct17_customer_never_receives_internal_group_ids_in_any_customer_field()
    {
        const string id = "0bd7f9fd-dce9-4aff-a2da-5dc56adf23c7";
        var analysis = new MarketingAnalysisResult { ReplyContent = $"أونلاين الساعة ٤ مع يوسف سعد (معرف المجموعة: {id}). المجموعة-{id}",
            PublicCommentReply = $"مجموعة {id}", SuggestedButtons = [$"احجز {id}"], SuggestedGroupBookingId = id };
        CustomerReplyInstructionPolicy.RemoveGroupIdentifiers(analysis);
        Assert.DoesNotContain(id, analysis.ReplyContent);
        Assert.DoesNotContain("معرف المجموعة", analysis.ReplyContent);
        Assert.Contains("يوسف سعد", analysis.ReplyContent);
        Assert.DoesNotContain(id, analysis.PublicCommentReply!);
        Assert.All(analysis.SuggestedButtons, button => Assert.DoesNotContain(id, button));
        Assert.Equal(id, analysis.SuggestedGroupBookingId);
    }

    [Fact]
    public void Group_redaction_preserves_unrelated_order_references()
    {
        var groupId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var analysis = new MarketingAnalysisResult { ReplyContent = $"المجموعة: {groupId}، رقم الطلب: {orderId}",
            PublicCommentReply = orderId.ToString(), SuggestedButtons = [orderId.ToString()] };
        CustomerReplyInstructionPolicy.RemoveGroupIdentifiers(analysis, [groupId]);
        Assert.DoesNotContain(groupId.ToString(), analysis.ReplyContent);
        Assert.Contains(orderId.ToString(), analysis.ReplyContent);
        Assert.Equal(orderId.ToString(), analysis.PublicCommentReply);
        Assert.Equal(orderId.ToString(), Assert.Single(analysis.SuggestedButtons));
    }

    [Theory]
    [InlineData("https://academy.example/enroll/0bd7f9fd-dce9-4aff-a2da-5dc56adf23c7")]
    [InlineData("https://academy.example/enroll?token=0bd7f9fd-dce9-4aff-a2da-5dc56adf23c7")]
    public void Hiding_group_identifiers_preserves_customer_business_links(string link)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = "رابط التقديم: " + link,
            PublicCommentReply = link, SuggestedButtons = [link] };
        CustomerReplyInstructionPolicy.RemoveGroupIdentifiers(analysis);
        Assert.Contains(link, analysis.ReplyContent);
        Assert.Equal(link, analysis.PublicCommentReply);
        Assert.Equal(link, Assert.Single(analysis.SuggestedButtons));
    }

    [Theory]
    [InlineData("تمام", true)]
    [InlineData("👍", true)]
    [InlineData("ماشي", true)]
    [InlineData("لا تمام", false)]
    [InlineData("شكرا", false)]
    [InlineData("مش مهتم", false)]
    public void Production_Oct17_acceptance_continues_last_invitation_and_declines_do_not_restart_sales(string customerMessage, bool accepted)
    {
        Message[] history = [new() { Direction = "Outgoing", MessageType = "Text", Content = "حضرتك حابب تعرف تفاصيل الكورس ولا السعر ولا المكان" },
            new() { Direction = "Incoming", MessageType = "Text", Content = customerMessage }];
        Assert.Equal(accepted, TalkTipsFollowUpAcceptancePolicy.AcceptsCourseInvitation(customerMessage, history));
        if (!accepted) return;
        var analysis = new MarketingAnalysisResult { ReplyContent = "تحت أمرك هل فيه استفسار تاني؟", SuggestedGroupBookingId = Guid.NewGuid().ToString(),
            SuggestedFollowUp = new() { Needed = true, Type = "Nurturing", AppointmentTime = "2026-10-17T11:00:00Z", Notes = "تأكيد الحجز الساعة ٢" } };
        TalkTipsFollowUpAcceptancePolicy.ContinueWithDetails(analysis, history[0].Content);
        Assert.Contains("٤ شهور", analysis.ReplyContent);
        Assert.Contains("١٨", analysis.ReplyContent);
        Assert.Contains("تجربة مجانية", analysis.ReplyContent);
        Assert.DoesNotContain("استفسار تاني", analysis.ReplyContent);
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.Null(analysis.SuggestedFollowUp!.AppointmentTime);
        Assert.DoesNotContain("٢", analysis.SuggestedFollowUp.Notes!);
    }

    [Fact]
    public void Thanks_after_booking_confirmation_does_not_reopen_an_older_invitation()
    {
        Message[] history = [new() { Direction = "Outgoing", Content = "تحب تعرف تفاصيل الكورس؟" },
            new() { Direction = "Incoming", Content = "احجز الساعة ٨" },
            new() { Direction = "Outgoing", Content = "تم تسجيل الحجز بنجاح الساعة ٨" }];
        Assert.False(TalkTipsFollowUpAcceptancePolicy.AcceptsCourseInvitation("تمام", history));
    }

    [Theory]
    [InlineData("AppointmentReminder")]
    [InlineData("appointmentreminder")]
    public void Acceptance_of_course_details_never_schedules_an_unconfirmed_appointment(string followUpType)
    {
        var analysis = new MarketingAnalysisResult { SuggestedGroupBookingId = Guid.NewGuid().ToString(),
            SuggestedFollowUp = new() { Needed = true, Type = followUpType,
                AppointmentTime = "2026-10-17T11:00:00Z", Notes = "تأكيد الساعة ٢" } };
        TalkTipsFollowUpAcceptancePolicy.ContinueWithDetails(analysis, "تحب تعرف تفاصيل الكورس؟");
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.False(analysis.SuggestedFollowUp.Needed);
        Assert.Null(analysis.SuggestedFollowUp.AppointmentTime);
        Assert.DoesNotContain("الساعة ٢", analysis.ReplyContent);
    }

    [Fact]
    public void Acceptance_of_a_specific_slot_keeps_the_booking_flow_instead_of_restarting_details()
    {
        Message[] history = [new() { Direction = "Outgoing", Content = "تحب أحجز لحضرتك الحصة المجانية الساعة ٨ مساء؟" }];
        Assert.False(TalkTipsFollowUpAcceptancePolicy.AcceptsCourseInvitation("تمام", history));
    }

    [Theory]
    [InlineData("كام واحد في المحاضره ؟")]
    [InlineData("المجموعة فيها كام طالب")]
    public void Production_Oct17_study_group_size_is_not_the_fifty_trial_registrations(string question)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = "الأونلاين 50 طالب كحد أقصى" };
        TalkTipsCourseGroupSizePolicy.Apply(question, analysis);
        Assert.Contains("١٥", analysis.ReplyContent);
        Assert.Contains("١٨", analysis.ReplyContent);
        Assert.DoesNotContain("50", analysis.ReplyContent);
        Assert.Contains("حجوزات الحصة المجانية منفصل", analysis.ReplyContent);
    }
}
