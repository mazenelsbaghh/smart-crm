using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Shared.Infrastructure;

namespace Modules.Analytics.Application.Services;

public sealed class DailyReplyReviewService(AppDbContext db)
{
    private const int PageSize = 30;

    public async Task<DailyReplyReviewPage> GetAsync(DailyReplyReviewRequest request, CancellationToken cancellationToken)
    {
        var window = await WindowAsync(request, cancellationToken);
        var followUps = await db.FollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(followUp => followUp.ProjectId == request.ProjectId
                && ((followUp.DueDate >= window.StartUtc && followUp.DueDate < window.EndUtc)
                    || (followUp.SentAtUtc >= window.StartUtc && followUp.SentAtUtc < window.EndUtc)))
            .ToListAsync(cancellationToken);
        var source = await LoadConversationsAsync(request.ProjectId, window, followUps, cancellationToken);
        var followUpRows = await FollowUpRowsAsync(followUps, request.ProjectId, cancellationToken);
        var rows = source.Conversations.Select(conversation => BuildRow(conversation, source, window, followUpRows)).ToArray();
        var filtered = request.View switch {
            "all" => rows,
            "unanalyzed" => rows.Where(row => row.AnalysisStatus != "Current").ToArray(),
            _ => rows.Where(row => row.Issues.Count > 0).ToArray()
        };
        var page = Math.Max(1, request.Page);
        return new(window.Date.ToString("yyyy-MM-dd"), window.Timezone, DateTime.UtcNow, window.StartUtc, window.EndUtc,
            Summary(rows, followUpRows), page, PageSize, filtered.Length,
            filtered.OrderByDescending(row => row.Issues.Count).ThenByDescending(row => row.LastActivityAtUtc)
                .ThenBy(row => row.ConversationId).Skip((page - 1) * PageSize).Take(PageSize).ToArray(), followUpRows);
    }

    private async Task<ReviewWindow> WindowAsync(DailyReplyReviewRequest request, CancellationToken cancellationToken)
    {
        var timezoneId = await db.ProjectSettings.IgnoreQueryFilters().Where(settings => settings.ProjectId == request.ProjectId)
            .Select(settings => settings.Timezone).SingleOrDefaultAsync(cancellationToken);
        var timezone = TimezoneHelper.GetTimeZone(timezoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
        var date = request.Date ?? today;
        if (date > today || date.Year < 2000) throw new ArgumentException("اختر اليوم أو تاريخًا سابقًا صالحًا.");
        return new(date, timezone.Id, LocalMidnightUtc(date, timezone), LocalMidnightUtc(date.AddDays(1), timezone));
    }

    internal static DateTime LocalMidnightUtc(DateOnly date, TimeZoneInfo timezone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some timezones advance at midnight; the day begins at its first valid local minute.
        while (timezone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }

    private async Task<ReviewSource> LoadConversationsAsync(
        Guid projectId, ReviewWindow window, IReadOnlyCollection<FollowUp> followUps, CancellationToken cancellationToken)
    {
        var followUpConversations = followUps.Where(f => f.ConversationId.HasValue).Select(f => f.ConversationId!.Value).ToArray();
        var conversations = await db.Conversations.IgnoreQueryFilters().AsNoTracking().Where(conversation =>
            conversation.ProjectId == projectId && (followUpConversations.Contains(conversation.Id)
                || db.Messages.IgnoreQueryFilters().Any(message => message.ConversationId == conversation.Id
                    && message.MessageType != "Reaction" && message.Timestamp >= window.StartUtc && message.Timestamp < window.EndUtc)))
            .ToListAsync(cancellationToken);
        var ids = conversations.Select(conversation => conversation.Id).ToArray();
        var messages = await db.Messages.IgnoreQueryFilters().AsNoTracking()
            .Where(message => ids.Contains(message.ConversationId) && message.MessageType != "Reaction"
                && message.Timestamp >= window.StartUtc && message.Timestamp < window.EndUtc)
            .ToListAsync(cancellationToken);
        var analyses = await db.ConversationSalesAnalyses.IgnoreQueryFilters().AsNoTracking()
            .Where(analysis => analysis.ProjectId == projectId && ids.Contains(analysis.ConversationId))
            .ToDictionaryAsync(analysis => analysis.ConversationId, cancellationToken);
        var customerIds = conversations.Select(conversation => conversation.CustomerId).ToArray();
        var names = await db.Customers.IgnoreQueryFilters().Where(customer => customer.ProjectId == projectId && customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => customer.Name, cancellationToken);
        return new(conversations, messages.ToLookup(message => message.ConversationId), analyses, names);
    }

    private static DailyReplyReviewRow BuildRow(
        Conversation conversation, ReviewSource source, ReviewWindow window, IReadOnlyList<FollowUpReviewRow> followUps)
    {
        var messages = source.Messages[conversation.Id].ToArray();
        var timing = DailyReplyReviewPolicy.Evaluate(messages, DateTime.UtcNow < window.EndUtc ? DateTime.UtcNow : window.EndUtc);
        var issues = timing.Issues.ToList();
        if (conversation.HumanHandoffReplyId.HasValue)
            issues.Add(new("HumanHandoff", "ينتظر موظف حاليًا", "System", []));
        if (followUps.Any(f => f.ConversationId == conversation.Id && f.Health is "Overdue" or "Failed" or "Unknown"))
            issues.Add(new("FollowUpProblem", "متابعة تحتاج تدخل", "FollowUps", []));
        source.Analyses.TryGetValue(conversation.Id, out var analysis);
        var analysisStatus = AnalysisStatus(analysis, messages, window);
        if (analysisStatus == "Current" && analysis!.ReplyQualityScore < 60)
            issues.Add(new("LowReplyQuality", "جودة رد منخفضة حسب التحليل", "AI", EvidenceIds(analysis)));
        return new(conversation.Id, conversation.CustomerId, source.CustomerNames.GetValueOrDefault(conversation.CustomerId, "عميل"),
            conversation.Channel, conversation.Status, conversation.HumanHandoffReplyId.HasValue,
            timing.IncomingCount, timing.OutgoingCount, timing.LongestResponseMinutes, timing.WaitingSinceUtc,
            messages.Length > 0 ? messages.Max(message => message.Timestamp) : conversation.LastMessageTimestamp, issues,
            analysisStatus, analysisStatus == "Current" ? analysis?.ReplyQualityScore : null,
            analysis?.Summary, analysis?.Recommendation, analysis?.AnalyzedAtUtc);
    }

    private static string AnalysisStatus(ConversationSalesAnalysis? analysis, IReadOnlyCollection<Message> messages, ReviewWindow window)
    {
        if (analysis == null) return "Missing";
        if (analysis.AnalyzedThroughMessageAtUtc >= window.EndUtc) return "Later";
        var latestAt = messages.Count > 0 ? messages.Max(message => message.Timestamp) : window.StartUtc;
        return analysis.AnalyzedThroughMessageAtUtc >= latestAt && analysis.AnalysisVersion >= ConversationSalesAnalyzer.CurrentAnalysisVersion
            ? "Current" : "Stale";
    }

    private static IReadOnlyList<Guid> EvidenceIds(ConversationSalesAnalysis analysis)
    {
        try { return (JsonSerializer.Deserialize<AnalysisEvidence[]>(analysis.EvidenceJson) ?? []).Select(evidence => evidence.MessageId).ToArray(); }
        catch (JsonException) { return []; }
    }

    private async Task<IReadOnlyList<FollowUpReviewRow>> FollowUpRowsAsync(IReadOnlyCollection<FollowUp> followUps, Guid projectId, CancellationToken cancellationToken)
    {
        var customerIds = followUps.Select(followUp => followUp.CustomerId).ToArray();
        var names = await db.Customers.IgnoreQueryFilters().Where(customer => customer.ProjectId == projectId && customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => customer.Name, cancellationToken);
        var sentIds = followUps.Where(followUp => followUp.SentMessageId.HasValue).Select(followUp => followUp.SentMessageId!.Value).ToArray();
        var sentMessages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(message => sentIds.Contains(message.Id)
            && db.Conversations.IgnoreQueryFilters().Any(conversation => conversation.Id == message.ConversationId && conversation.ProjectId == projectId))
            .ToDictionaryAsync(message => message.Id, cancellationToken);
        var replied = await db.FollowUps.IgnoreQueryFilters().Where(followUp => followUp.ProjectId == projectId
            && followUp.SentAtUtc.HasValue && followUp.ConversationId.HasValue && customerIds.Contains(followUp.CustomerId)
            && db.Conversations.IgnoreQueryFilters().Any(conversation => conversation.Id == followUp.ConversationId && conversation.ProjectId == projectId)
            && db.Messages.IgnoreQueryFilters().Any(message => message.ConversationId == followUp.ConversationId
                && message.Direction == "Incoming" && message.MessageType != "Reaction" && message.Timestamp > followUp.SentAtUtc))
            .Select(followUp => followUp.Id).ToListAsync(cancellationToken);
        return followUps.OrderBy(followUp => followUp.DueDate).Select(followUp => new FollowUpReviewRow(
            followUp.Id, followUp.CustomerId, names.GetValueOrDefault(followUp.CustomerId, "عميل"), followUp.ConversationId,
            followUp.Channel, followUp.Type, followUp.Status, DailyReplyReviewPolicy.FollowUpHealth(followUp, DateTime.UtcNow),
            followUp.SentForDueAtUtc ?? followUp.DueDate, followUp.SentAtUtc,
            followUp.SentAtUtc.HasValue ? Math.Round((followUp.SentAtUtc.Value - (followUp.SentForDueAtUtc ?? followUp.DueDate)).TotalMinutes, 1) : null,
            followUp.SentMessageId, followUp.SentMessageId.HasValue && sentMessages.TryGetValue(followUp.SentMessageId.Value, out var message)
                ? message.Content : followUp.Notes, replied.Contains(followUp.Id))).ToArray();
    }

    private static DailyReplyReviewSummary Summary(IReadOnlyCollection<DailyReplyReviewRow> rows, IReadOnlyCollection<FollowUpReviewRow> followUps) =>
        new(rows.Count, rows.Count(row => row.Issues.Count > 0), rows.Count(row => row.AnalysisStatus != "Current"),
            rows.Count(row => row.HumanHandoffPending), followUps.Count, followUps.Count(f => f.Health == "Sent"),
            followUps.Count(f => f.Health == "Overdue"), followUps.Count(f => f.Health is "Unknown" or "Unverified"));

    private sealed record ReviewWindow(DateOnly Date, string Timezone, DateTime StartUtc, DateTime EndUtc);
    private sealed record ReviewSource(IReadOnlyList<Conversation> Conversations, ILookup<Guid, Message> Messages,
        IReadOnlyDictionary<Guid, ConversationSalesAnalysis> Analyses, IReadOnlyDictionary<Guid, string> CustomerNames);
}
