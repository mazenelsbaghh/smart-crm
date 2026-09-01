using Shared.Domain;

namespace Modules.Analytics.Domain;

public enum SalesConversationStage
{
    New = 0,
    Engaged = 1,
    Qualified = 2,
    BookingIntent = 3,
    Booked = 4,
    Paid = 5,
    Attended = 6
}

public enum SalesConversationOutcome
{
    Active = 0,
    Dormant = 1,
    Lost = 2,
    Converted = 3,
    NotApplicable = 4
}

public enum SalesLossReason
{
    None = 0,
    Unknown = 1,
    NoReplyAfterFollowUp = 2,
    PriceObjection = 3,
    ScheduleMismatch = 4,
    NoAvailability = 5,
    UnclearOffer = 6,
    SlowResponse = 7,
    MissingFollowUp = 8,
    MissingBookingData = 9,
    BookingTechnicalFailure = 10,
    NeedsMoreTime = 11,
    DecisionMakerUnavailable = 12,
    ChoseCompetitor = 13,
    NotQualified = 14,
    SpamOrSupport = 15,
    TrustConcern = 16,
    PaymentIssue = 17,
    OtherExplicitReason = 18
}

public sealed class ConversationSalesAnalysis : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime ConversationStartedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public DateTime AnalyzedThroughMessageAtUtc { get; set; }
    public DateTime AnalyzedAtUtc { get; set; }
    public SalesConversationStage AiStage { get; set; }
    public SalesConversationStage VerifiedStage { get; set; }
    public SalesConversationOutcome Outcome { get; set; }
    public SalesLossReason AiPrimaryReason { get; set; }
    public SalesLossReason? ManualPrimaryReason { get; set; }
    public string SecondaryReasonsJson { get; set; } = "[]";
    public string Summary { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "[]";
    public string LastCustomerIntent { get; set; } = string.Empty;
    public string RequestedScheduleText { get; set; } = string.Empty;
    public string RequestedScheduleLabel { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public int ReplyQualityScore { get; set; }
    public int FollowUpPriority { get; set; }
    public bool NeedsFollowUp { get; set; }
    public bool MissedOpportunity { get; set; }
    public string? ManualNotes { get; set; }
    public Guid? CorrectedByUserId { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }
    public string Model { get; set; } = string.Empty;
    public int AnalysisVersion { get; set; } = 2;

    public SalesLossReason EffectivePrimaryReason => ManualPrimaryReason ?? AiPrimaryReason;
}

public sealed class SalesIntelligenceDigest : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public string WindowTimezone { get; set; } = "Africa/Cairo";
    public string DataFingerprint { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public string FindingsJson { get; set; } = "[]";
    public string RecommendationsJson { get; set; } = "[]";
    public string RisksJson { get; set; } = "[]";
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
}
