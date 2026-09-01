using Modules.AI.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SchedulePreferenceReplyPolicyTests
{
    [Fact]
    public void Rejected_schedule_without_alternative_asks_for_suitable_days_and_times()
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

        Assert.Contains("قولي إيه المواعيد المناسبة مع حضرتك", analysis.ReplyContent);
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.Empty(analysis.SuggestedGroupBookingPeople);
        Assert.False(analysis.SuggestedFollowUp.Needed);
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
}
