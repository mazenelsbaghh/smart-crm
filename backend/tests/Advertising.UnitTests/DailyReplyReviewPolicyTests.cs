using Modules.Analytics.Application.Services;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class DailyReplyReviewPolicyTests
{
    private static readonly DateTime Start = new(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Repeated_price_reply_and_waiting_customer_are_reported_with_message_evidence()
    {
        var first = Message(1, "Outgoing", "الاشتراك الشهري هو 1500 جنيه مصري شهريًا.");
        var duplicate = Message(4, "Outgoing", first.Content);
        var latest = Message(5, "Incoming", "هو إيه علاقة ده بسؤالي؟");
        var review = DailyReplyReviewPolicy.Evaluate([Message(0, "Incoming", "السيشن امتى؟"), first, duplicate, latest], Start.AddMinutes(15));
        Assert.Equal(new[] { first.Id, duplicate.Id }, review.Issues.Single(i => i.Code == "RepeatedReply").MessageIds);
        Assert.Equal(latest.Timestamp, review.WaitingSinceUtc);
        Assert.Contains(review.Issues, i => i.Code == "Unanswered");
    }

    [Fact]
    public void Reaction_does_not_answer_a_burst_of_customer_messages_or_reset_waiting_time()
    {
        var first = Message(0, "Incoming", "ممكن التفاصيل؟");
        var reaction = Message(3, "Outgoing", "❤️");
        reaction.MessageType = "Reaction";
        var reply = Message(8, "Outgoing", "متاح أونلاين الساعة ٦.");
        var review = DailyReplyReviewPolicy.Evaluate([first, Message(2, "Incoming", "الكورس أونلاين؟"), reaction, reply], Start.AddMinutes(12));
        Assert.Equal(8, review.LongestResponseMinutes);
        Assert.Equal(1, review.OutgoingCount);
        Assert.Equal(new[] { first.Id, reply.Id }, review.Issues.Single(i => i.Code == "SlowReply").MessageIds);
        Assert.Null(review.WaitingSinceUtc);
    }

    [Theory]
    [InlineData(300, false)]
    [InlineData(301, true)]
    public void Five_minute_threshold_uses_elapsed_time_before_display_rounding(int seconds, bool expected)
    {
        var reply = Message(0, "Outgoing", "رد");
        reply.Timestamp = Start.AddSeconds(seconds);
        var review = DailyReplyReviewPolicy.Evaluate([Message(0, "Incoming", "سؤال"), reply], Start.AddHours(1));
        Assert.Equal(expected, review.Issues.Any(i => i.Code == "SlowReply"));
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Human")]
    public void Repeated_staff_text_is_not_reported_as_an_automated_loop(string sender)
    {
        var messages = new[] { Message(1, "Outgoing", "حضرتك متاح أونلاين"), Message(2, "Outgoing", "حضرتك متاح أونلاين") };
        foreach (var message in messages) message.SenderType = sender;
        Assert.Empty(DailyReplyReviewPolicy.Evaluate(messages, Start.AddHours(1)).Issues);
    }

    [Theory]
    [InlineData("Completed", false, "Unverified")]
    [InlineData("Completed", true, "Sent")]
    [InlineData("DeliveryUnknown", false, "Unknown")]
    [InlineData("Pending", false, "Overdue")]
    [InlineData("Processing", false, "Overdue")]
    [InlineData("Cancelled", false, "Stopped")]
    public void Follow_up_health_does_not_confuse_completion_with_confirmed_sending(string status, bool evidence, string expected)
    {
        var followUp = new FollowUp { Status = status, DueDate = Start, Notes = "متابعة",
            SentAtUtc = evidence ? Start.AddMinutes(1) : null, SentMessageId = evidence ? Guid.NewGuid() : null };
        Assert.Equal(expected, DailyReplyReviewPolicy.FollowUpHealth(followUp, Start.AddMinutes(10)));
    }

    private static Message Message(int minute, string direction, string content) => new()
    { Id = Guid.NewGuid(), Direction = direction, SenderType = direction == "Incoming" ? "Customer" : "AI",
        Content = content, Timestamp = Start.AddMinutes(minute), MessageType = "Text" };
}
