using System.Text.Json;
using Modules.Analytics.Application;
using Modules.Analytics.Domain;

namespace Modules.Analytics.Application.Services;

public sealed record ParsedConversationAnalysis(
    SalesConversationStage Stage,
    SalesConversationOutcome Outcome,
    SalesLossReason PrimaryReason,
    IReadOnlyList<SalesLossReason> SecondaryReasons,
    string Summary,
    string Recommendation,
    IReadOnlyList<AnalysisEvidence> Evidence,
    string LastCustomerIntent,
    string RequestedScheduleText,
    string RequestedScheduleLabel,
    decimal Confidence,
    int ReplyQualityScore,
    int FollowUpPriority,
    bool NeedsFollowUp,
    bool MissedOpportunity);

public sealed record ParsedDigest(
    string ExecutiveSummary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Recommendations,
    IReadOnlyList<string> Risks);

public sealed record ParsedAnalystAnswer(string Answer, IReadOnlyList<Guid> ConversationIds);

public sealed class AiEngineUnavailableException() : InvalidOperationException("تعذر الوصول إلى محرك AI.");

public static class SalesIntelligenceAiParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ParsedConversationAnalysis ParseConversation(string raw)
    {
        var dto = JsonSerializer.Deserialize<ConversationAnalysisPayload>(ExtractObject(raw), JsonOptions)
            ?? throw new InvalidOperationException("لم يُرجع محرك AI تحليلًا صالحًا.");
        var stage = ParseRequiredEnum<SalesConversationStage>(dto.Stage, "stage");
        if (stage > SalesConversationStage.BookingIntent) stage = SalesConversationStage.BookingIntent;
        var outcome = ParseRequiredEnum<SalesConversationOutcome>(dto.Outcome, "outcome");
        if (outcome == SalesConversationOutcome.Converted) outcome = SalesConversationOutcome.Active;
        var reason = ParseRequiredEnum<SalesLossReason>(dto.PrimaryReason, "primaryReason");
        var secondaryReasons = (dto.SecondaryReasons ?? [])
            .Select(item => ParseEnum(item, SalesLossReason.Unknown))
            .Where(item => item != SalesLossReason.None)
            .Distinct()
            .Take(4)
            .ToArray();
        var evidence = (dto.Evidence ?? [])
            .Where(item => Guid.TryParse(item.MessageId, out _) && !string.IsNullOrWhiteSpace(item.Quote))
            .Select(item => new AnalysisEvidence(Guid.Parse(item.MessageId!), Clean(item.Quote, 240)))
            .Take(5)
            .ToArray();

        var summary = RequiredText(dto.Summary, "summary", 1200);
        var recommendation = RequiredText(dto.Recommendation, "recommendation", 1200);
        return new ParsedConversationAnalysis(
            stage,
            outcome,
            reason,
            secondaryReasons,
            summary,
            recommendation,
            evidence,
            Clean(dto.LastCustomerIntent, 500),
            Clean(dto.RequestedScheduleText, 240),
            Clean(dto.RequestedScheduleLabel, 120),
            Math.Clamp(dto.Confidence, 0m, 1m),
            Math.Clamp(dto.ReplyQualityScore, 0, 100),
            Math.Clamp(dto.FollowUpPriority, 0, 100),
            dto.NeedsFollowUp,
            dto.MissedOpportunity);
    }

    public static ParsedDigest ParseDigest(string raw)
    {
        var dto = JsonSerializer.Deserialize<DigestPayload>(ExtractObject(raw), JsonOptions)
            ?? throw new InvalidOperationException("لم يُرجع محرك AI ملخصًا صالحًا.");
        return new ParsedDigest(
            RequiredText(dto.ExecutiveSummary, "executiveSummary", 2400),
            CleanList(dto.Findings, 8),
            CleanList(dto.Recommendations, 8),
            CleanList(dto.Risks, 6));
    }

    public static ParsedAnalystAnswer ParseAnswer(string raw)
    {
        var dto = JsonSerializer.Deserialize<AnswerPayload>(ExtractObject(raw), JsonOptions)
            ?? throw new InvalidOperationException("لم يُرجع محرك AI إجابة صالحة.");
        var ids = (dto.ConversationIds ?? [])
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(20)
            .ToArray();
        return new ParsedAnalystAnswer(RequiredText(dto.Answer, "answer", 4000), ids);
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static T ParseRequiredEnum<T>(string? rawValue, string field) where T : struct, Enum
    {
        if (Enum.TryParse<T>(rawValue, true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new InvalidOperationException($"استجابة AI تحتوي قيمة غير صالحة في {field}.");
    }

    private static IReadOnlyList<string> CleanList(IEnumerable<string>? values, int maximum) =>
        (values ?? [])
            .Select(value => Clean(value, 600))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(maximum)
            .ToArray();

    private static string ExtractObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("[AI_ERROR]", StringComparison.Ordinal))
            throw new AiEngineUnavailableException();
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("استجابة AI ليست JSON صالحًا.");
        return raw[start..(end + 1)];
    }

    private static string Clean(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var clean = value.Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string RequiredText(string? rawText, string field, int maximumLength)
    {
        var cleanText = Clean(rawText, maximumLength);
        if (cleanText.Length == 0) throw new InvalidOperationException($"استجابة AI لا تحتوي {field}.");
        return cleanText;
    }

    private sealed class ConversationAnalysisPayload
    {
        public string? Stage { get; set; }
        public string? Outcome { get; set; }
        public string? PrimaryReason { get; set; }
        public string[]? SecondaryReasons { get; set; }
        public string? Summary { get; set; }
        public string? Recommendation { get; set; }
        public EvidencePayload[]? Evidence { get; set; }
        public string? LastCustomerIntent { get; set; }
        public string? RequestedScheduleText { get; set; }
        public string? RequestedScheduleLabel { get; set; }
        public decimal Confidence { get; set; }
        public int ReplyQualityScore { get; set; }
        public int FollowUpPriority { get; set; }
        public bool NeedsFollowUp { get; set; }
        public bool MissedOpportunity { get; set; }
    }

    private sealed class EvidencePayload
    {
        public string? MessageId { get; set; }
        public string? Quote { get; set; }
    }

    private sealed class DigestPayload
    {
        public string? ExecutiveSummary { get; set; }
        public string[]? Findings { get; set; }
        public string[]? Recommendations { get; set; }
        public string[]? Risks { get; set; }
    }

    private sealed class AnswerPayload
    {
        public string? Answer { get; set; }
        public string[]? ConversationIds { get; set; }
    }
}
