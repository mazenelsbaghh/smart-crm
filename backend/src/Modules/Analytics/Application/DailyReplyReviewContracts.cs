namespace Modules.Analytics.Application;

public sealed record ReplyReviewIssue(string Code, string Label, string Source, IReadOnlyList<Guid> MessageIds);
public sealed record DailyReplyReviewRow(
    Guid ConversationId, Guid CustomerId, string CustomerName, string Channel, string Status,
    bool HumanHandoffPending, int IncomingCount, int OutgoingCount, double? LongestResponseMinutes,
    DateTime? WaitingSinceUtc, DateTime LastActivityAtUtc, IReadOnlyList<ReplyReviewIssue> Issues,
    string AnalysisStatus, int? ReplyQualityScore, string? Summary, string? Recommendation,
    DateTime? AnalyzedAtUtc);
public sealed record FollowUpReviewRow(
    Guid Id, Guid CustomerId, string CustomerName, Guid? ConversationId, string? Channel,
    string Type, string Status, string Health, DateTime DueAtUtc, DateTime? SentAtUtc,
    double? DelayMinutes, Guid? SentMessageId, string Content, bool CustomerReplied);
public sealed record DailyReplyReviewSummary(
    int Conversations, int NeedsAttention, int AwaitingAnalysis, int WaitingForHuman,
    int FollowUpsDue, int FollowUpsSent, int FollowUpsOverdue, int FollowUpsUnknown);
public sealed record DailyReplyReviewPage(
    string Date, string Timezone, DateTime GeneratedAtUtc, DateTime WindowStartUtc, DateTime WindowEndUtc,
    DailyReplyReviewSummary Summary, int Page, int PageSize, int FilteredCount,
    IReadOnlyList<DailyReplyReviewRow> Conversations, IReadOnlyList<FollowUpReviewRow> FollowUps);
public sealed record DailyReplyReviewRequest(Guid ProjectId, DateOnly? Date, int Page = 1, string View = "attention");
public sealed record CorrectiveReplyDraft(string Content, Guid BasedOnMessageId, DateTime GeneratedAtUtc);
