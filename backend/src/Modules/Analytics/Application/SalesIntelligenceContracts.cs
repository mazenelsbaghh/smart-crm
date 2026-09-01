using Modules.Analytics.Domain;

namespace Modules.Analytics.Application;

public sealed record FunnelMetric(string Key, string Label, int Count, decimal RateFromPrevious);
public sealed record DailySalesMetric(
    string Date,
    int NewConversations,
    int Responded,
    int Qualified,
    int BookingIntent,
    int Booked,
    int Paid,
    int Attended);
public sealed record ReasonMetric(string Reason, string Label, int Count, decimal Percentage);
public sealed record FunnelDropOffReason(
    string Reason,
    string Label,
    int Count,
    decimal Percentage,
    int NeedsFollowUp);
public sealed record FunnelTransitionMetric(
    string Key,
    string FromLabel,
    string ToLabel,
    int FromCount,
    int ToCount,
    int DropOffCount,
    decimal ConversionRate,
    decimal DropOffRate,
    int NeedsFollowUp,
    IReadOnlyList<FunnelDropOffReason> Reasons);
public sealed record AnalysisEvidence(Guid MessageId, string Quote);
public sealed record FollowUpPlanSummary(
    int SendNow,
    int Schedule,
    int Scheduled,
    string SendNowToken,
    string ScheduleToken);
public enum FollowUpPlanAction { SendNow, Schedule }
public sealed record QueueFollowUpPlan(
    Guid ProjectId,
    DateTime FromUtc,
    DateTime ToUtc,
    FollowUpPlanAction Action,
    Guid? ConversationId = null,
    string? PlanToken = null);
public sealed record QueueFollowUpPlanResult(int Queued, bool PlanChanged = false);
public sealed record OpportunityItem(
    Guid ConversationId,
    Guid CustomerId,
    string CustomerName,
    string Channel,
    int Priority,
    string Stage,
    string Reason,
    string ReasonLabel,
    string Summary,
    string Recommendation,
    string RecommendedAction,
    string ActionToken,
    DateTime? ScheduledForUtc,
    DateTime LastMessageAtUtc);
public sealed record ConversationAnalysisItem(
    Guid ConversationId,
    Guid CustomerId,
    string CustomerName,
    string Channel,
    string Stage,
    string Outcome,
    string Reason,
    string ReasonLabel,
    string Summary,
    string Recommendation,
    decimal Confidence,
    int ReplyQualityScore,
    int FollowUpPriority,
    bool NeedsFollowUp,
    bool MissedOpportunity,
    bool ManuallyCorrected,
    IReadOnlyList<AnalysisEvidence> Evidence,
    DateTime ConversationStartedAtUtc,
    DateTime LastMessageAtUtc,
    DateTime AnalyzedAtUtc);
public sealed record AiDigestResponse(
    string ExecutiveSummary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Risks,
    DateTime GeneratedAtUtc,
    string Model);
public sealed record SalesIntelligenceDashboard(
    Guid ProjectId,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    string Timezone,
    DateTime GeneratedAtUtc,
    int TotalConversations,
    int UniqueCustomers,
    int ActiveConversations,
    int AnalyzedConversations,
    decimal AnalysisCoverage,
    decimal BookingConversionRate,
    decimal PaymentConversionRate,
    decimal MedianFirstResponseMinutes,
    IReadOnlyList<FunnelMetric> Funnel,
    IReadOnlyList<FunnelTransitionMetric> FunnelTransitions,
    IReadOnlyList<DailySalesMetric> Daily,
    IReadOnlyList<ReasonMetric> Reasons,
    FollowUpPlanSummary FollowUpPlan,
    IReadOnlyList<OpportunityItem> Opportunities,
    IReadOnlyList<ConversationAnalysisItem> Analyses,
    AiDigestResponse? AiDigest);

public sealed record AnalyzeConversationResult(
    bool Analyzed,
    ConversationAnalysisItem? Analysis,
    string? Error);
public sealed record AskSalesAnalystResult(
    string Answer,
    IReadOnlyList<Guid> ConversationIds,
    DateTime GeneratedAtUtc,
    string Model,
    int TotalConversations,
    int AnalyzedConversations,
    int DetailedAnalysesReviewed,
    decimal AnalysisCoverage);
public sealed record ScheduleDemandGroup(string Label, int PeopleCount);
public sealed record ScheduleDemandRow(
    Guid ConversationId,
    Guid CustomerId,
    string CustomerName,
    string PhoneNumber,
    string Channel,
    string RequestedScheduleText,
    string RequestedScheduleLabel,
    DateTime LastMessageAtUtc,
    decimal Confidence);
public sealed record OpenScheduleAppointment(
    Guid GroupId,
    string Name,
    string Mode,
    DateTime DateTimeUtc,
    string Days,
    string InstructorName,
    int AvailableSlots);
public sealed record ScheduleDemandOverview(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalPeople,
    int DistinctSchedules,
    int PendingLegacyExtraction,
    IReadOnlyList<ScheduleDemandGroup> Groups,
    IReadOnlyList<ScheduleDemandRow> Rows,
    IReadOnlyList<OpenScheduleAppointment> OpenAppointments);
public sealed record SendScheduleAvailabilityResult(
    int Selected,
    int Queued,
    int SkippedDuplicate,
    int SkippedNoContact,
    int SkippedNoEligibleAppointments);

public sealed class AskSalesAnalystRequest
{
    public string Question { get; set; } = string.Empty;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}

public sealed class CorrectConversationAnalysisRequest
{
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class QueueFollowUpPlanRequest
{
    public string Action { get; set; } = string.Empty;
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public Guid? ConversationId { get; set; }
    public string? PlanToken { get; set; }
}

public sealed class SendScheduleAvailabilityRequest
{
    public IReadOnlyList<Guid> CustomerIds { get; set; } = Array.Empty<Guid>();
}

public static class SalesIntelligenceLabels
{
    public static string Stage(SalesConversationStage stage) => stage switch
    {
        SalesConversationStage.New => "شات جديد",
        SalesConversationStage.Engaged => "تم التفاعل",
        SalesConversationStage.Qualified => "عميل مؤهل",
        SalesConversationStage.BookingIntent => "نية حجز",
        SalesConversationStage.Booked => "حجز",
        SalesConversationStage.Paid => "دفع",
        SalesConversationStage.Attended => "حضر",
        _ => stage.ToString()
    };

    public static string Reason(SalesLossReason reason) => reason switch
    {
        SalesLossReason.None => "لا يوجد",
        SalesLossReason.Unknown => "السبب غير معروف",
        SalesLossReason.NoReplyAfterFollowUp => "لم يرد بعد المتابعة",
        SalesLossReason.PriceObjection => "اعتراض على السعر",
        SalesLossReason.ScheduleMismatch => "المواعيد غير مناسبة",
        SalesLossReason.NoAvailability => "لا توجد سعة متاحة",
        SalesLossReason.UnclearOffer => "العرض غير واضح",
        SalesLossReason.SlowResponse => "تأخر الرد",
        SalesLossReason.MissingFollowUp => "لم تتم المتابعة",
        SalesLossReason.MissingBookingData => "بيانات الحجز ناقصة",
        SalesLossReason.BookingTechnicalFailure => "مشكلة تقنية في الحجز",
        SalesLossReason.NeedsMoreTime => "يحتاج وقتًا للتفكير",
        SalesLossReason.DecisionMakerUnavailable => "ينتظر صاحب القرار",
        SalesLossReason.ChoseCompetitor => "اختار منافسًا",
        SalesLossReason.NotQualified => "غير مؤهل",
        SalesLossReason.SpamOrSupport => "دعم أو رسالة غير بيعية",
        SalesLossReason.TrustConcern => "مشكلة ثقة",
        SalesLossReason.PaymentIssue => "مشكلة في الدفع",
        SalesLossReason.OtherExplicitReason => "سبب صريح آخر",
        _ => reason.ToString()
    };
}
