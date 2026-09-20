using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SchedulePreferenceReplyPolicyTests
{
    [Fact]
    public void Rejected_schedule_without_alternative_asks_for_fixed_time_windows_only()
    {
        var analysis = new MarketingAnalysisResult
        {
            ReplyContent = "دي المواعيد المتاحة.",
            SuggestedGroupBookingId = Guid.NewGuid().ToString(),
            SuggestedGroupBookingPeople = [new() { IsRequester = true }],
            SuggestedFollowUp = new() { Needed = true }
        };

        SchedulePreferenceReplyPolicy.Apply(
            "المواعيد دي مش مناسبة ليا",
            analysis,
            "WhatsApp",
            "سارة");

        Assert.Contains("من ١٢ ظهرًا لـ٤ عصرًا", analysis.ReplyContent);
        Assert.Contains("من ٤ عصرًا لـ٨ مساءً", analysis.ReplyContent);
        Assert.Contains("من ٨ مساءً لـ١٢ منتصف الليل", analysis.ReplyContent);
        Assert.Contains("الأسبوع الجاي", analysis.ReplyContent);
        Assert.Contains("الشهر الجاي", analysis.ReplyContent);
        Assert.Contains("تحب الحضور أونلاين ولا أوفلاين في السنتر؟", analysis.ReplyContent);
        Assert.DoesNotContain("الأيام", analysis.ReplyContent);
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.Empty(analysis.SuggestedGroupBookingPeople);
        Assert.False(analysis.SuggestedFollowUp.Needed);
    }

    [Theory]
    [InlineData("Online", "أونلاين")]
    [InlineData("Offline", "أوفلاين في السنتر")]
    [InlineData("Either", "أونلاين أو أوفلاين في السنتر")]
    public void Known_attendance_mode_is_confirmed_without_asking_again(string mode, string label)
    {
        var analysis = new MarketingAnalysisResult { AttendanceMode = mode };

        SchedulePreferenceReplyPolicy.Apply("المواعيد مش مناسبة", analysis, "WhatsApp", "سارة");

        Assert.Contains(label, analysis.ReplyContent);
        Assert.DoesNotContain("تحب الحضور", analysis.ReplyContent);
    }

    [Theory]
    [InlineData("WhatsApp", "Unknown", true)]
    [InlineData("Messenger", "Unknown", true)]
    [InlineData("WhatsApp", "Online", false)]
    [InlineData("Facebook Comment", "Unknown", false)]
    public void Schedule_inquiry_asks_for_missing_mode_only_in_private(string channel, string mode, bool asks)
    {
        const string answer = "المواعيد المتاحة السبت والأحد.";
        var analysis = new MarketingAnalysisResult { ReplyContent = answer, AttendanceMode = mode };

        SchedulePreferenceReplyPolicy.Apply("مواعيدكم إيه؟", analysis, channel, "سارة");

        Assert.StartsWith(answer, analysis.ReplyContent);
        Assert.Equal(asks, analysis.ReplyContent.Contains("تحب الحضور"));
        Assert.Null(analysis.SuggestedGroupBookingId);
    }

    [Fact]
    public void Rejected_schedule_with_explicit_alternative_keeps_contextual_ai_reply()
    {
        const string aiReply = "تمام، هسجل إن السبت الساعة ٦ أنسب لحضرتك.";
        var analysis = new MarketingAnalysisResult { ReplyContent = aiReply };

        SchedulePreferenceReplyPolicy.Apply(
            "المعاد مش مناسب بس السبت الساعة ٦ ينفع",
            analysis,
            "WhatsApp",
            "سارة");

        Assert.Equal(aiReply, analysis.ReplyContent);
    }

    [Theory]
    [InlineData("أونلاين السبت وأوفلاين الأحد.")]
    [InlineData("تحب الحضور أونلاين ولا أوفلاين في السنتر؟")]
    public void Mentioning_both_modes_still_requires_a_question_but_existing_question_is_not_repeated(string reply)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = reply };

        SchedulePreferenceReplyPolicy.Apply("إيه المواعيد؟", analysis, "WhatsApp", "سارة");

        Assert.Equal(1, analysis.ReplyContent.Split("تحب الحضور").Length - 1);
    }
}
