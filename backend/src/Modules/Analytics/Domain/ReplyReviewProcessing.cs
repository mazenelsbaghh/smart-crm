using Shared.Domain;

namespace Modules.Analytics.Domain;

public sealed class ReplyReviewSchedule : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool PrepareDrafts { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int QuietMinutes { get; set; } = 5;
    public int VerifyAfterMinutes { get; set; } = 30;
    public int BatchSize { get; set; } = 20;
    public DateTime NextScanAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastScanAtUtc { get; set; }
}

public enum ReplyReviewState { Queued, Reviewing, RetryScheduled, DraftReady, NeedsHuman, VerifyScheduled, Reviewed, Resolved, Failed }

public sealed class ReplyReviewCase : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public ReplyReviewState State { get; set; } = ReplyReviewState.Queued;
    public string Phase { get; set; } = "Review";
    public Guid SourceMessageId { get; set; }
    public DateTime SourceMessageAtUtc { get; set; }
    public DateTime? SourceFollowUpAtUtc { get; set; }
    public DateTime? VerifyAfterMessageAtUtc { get; set; }
    public DateTime NextRunAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastReviewedAtUtc { get; set; }
    public DateTime? DispatchUntilUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public int Attempts { get; set; }
    public bool NeedsResolution { get; set; }
    public int? QualityScore { get; set; }
    public string Summary { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string DraftContent { get; set; } = "";
    public Guid? DraftBasedOnMessageId { get; set; }
    public DateTime? DraftGeneratedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class ReplyReviewRun : Entity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CaseId { get; set; }
    public Guid SourceMessageId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public string Outcome { get; set; } = "";
    public int Attempt { get; set; }
    public int? QualityScore { get; set; }
    public string Summary { get; set; } = "";
    public string? Error { get; set; }
}
