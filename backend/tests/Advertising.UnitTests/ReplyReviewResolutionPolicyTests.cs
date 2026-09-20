using System.Text.Json;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ReplyReviewResolutionPolicyTests
{
    [Theory]
    [InlineData("confirmed", true)]
    [InlineData("low-confidence", false)]
    [InlineData("still-unresolved", false)]
    [InlineData("missing-verdict", false)]
    [InlineData("invented-quote", false)]
    [InlineData("old-reply", false)]
    [InlineData("customer-message", false)]
    public void Self_review_requires_a_reliable_verdict_and_a_real_new_outgoing_reply(string scenario, bool resolved)
    {
        var reviewedAt = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var reply = new Message { Direction = scenario == "customer-message" ? "Incoming" : "Outgoing",
            Content = "السيشن الساعة الثامنة مساءً", MessageType = "Text",
            Timestamp = scenario == "old-reply" ? reviewedAt : reviewedAt.AddMinutes(1) };
        var analysis = new ConversationSalesAnalysis { Confidence = scenario == "low-confidence" ? 0.4m : 0.9m,
            HasUnresolvedReplyIssue = scenario == "missing-verdict" ? null : scenario == "still-unresolved",
            EvidenceJson = JsonSerializer.Serialize(new[] { new AnalysisEvidence(reply.Id, scenario == "invented-quote" ? "تم حل كل حاجة" : reply.Content) }) };

        Assert.Equal(resolved, ReplyReviewResolutionPolicy.HasResolutionEvidence(analysis, [reply], reviewedAt));
    }
}
