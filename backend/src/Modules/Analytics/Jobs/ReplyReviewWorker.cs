using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Shared.Infrastructure;

namespace Modules.Analytics.Jobs;

public sealed class ReplyReviewWorker(AppDbContext db, ReplyReviewQueue queue, ConversationSalesAnalyzer analyzer,
    CorrectiveReplyDraftService drafts, ILogger<ReplyReviewWorker> logger)
{
    [Queue("reply-review")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(Guid caseId, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var token = Guid.NewGuid();
        var claimed = await db.ReplyReviewCases.IgnoreQueryFilters().Where(r => r.Id == caseId && r.NextRunAtUtc <= started
                && (r.State == ReplyReviewState.Queued || r.State == ReplyReviewState.RetryScheduled
                    || r.State == ReplyReviewState.VerifyScheduled)
                && db.ReplyReviewSchedules.IgnoreQueryFilters().Any(s => s.ProjectId == r.ProjectId && s.Enabled))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.State, ReplyReviewState.Reviewing)
                .SetProperty(r => r.LeaseToken, token).SetProperty(r => r.LeaseUntilUtc, started.AddMinutes(10))
                .SetProperty(r => r.Attempts, r => r.Attempts + 1), ct);
        if (claimed == 0) return;
        var review = await db.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(r => r.Id == caseId, ct);
        await db.Entry(review).ReloadAsync(ct);
        var run = new ReplyReviewRun { ProjectId = review.ProjectId, CaseId = review.Id, SourceMessageId = review.SourceMessageId,
            StartedAtUtc = started, Attempt = review.Attempts };
        try { await ReviewAsync(review, ct); }
        catch (StaleCorrectiveDraftException) { await RescheduleChangedAsync(review, ct); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or HttpRequestException
            || exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Reply review failed for project {ProjectId}, case {CaseId}, attempt {Attempt}.", review.ProjectId, review.Id, review.Attempts);
            review.State = review.Attempts >= 3 ? ReplyReviewState.Failed : ReplyReviewState.RetryScheduled;
            review.NextRunAtUtc = DateTime.UtcNow.AddMinutes(review.Attempts * 5);
            review.LastError = "تعذر إكمال التحليل أو تجهيز المسودة. راجع إعدادات مزود الذكاء الاصطناعي؛ ستظهر نتيجة كل محاولة هنا.";
        }
        await SaveOutcomeAsync(review, run, ct);
    }

    private async Task ReviewAsync(ReplyReviewCase review, CancellationToken ct)
    {
        var latest = await queue.LatestMessageAsync(review.ConversationId, ct);
        if (latest == null) throw new InvalidOperationException("Conversation has no messages.");
        if (latest.Id != review.SourceMessageId) { await RescheduleChangedAsync(review, ct); return; }
        if (review.Phase == "Verify" && review.VerifyAfterMessageAtUtc.HasValue && !await db.Messages.IgnoreQueryFilters().AnyAsync(m =>
                m.ConversationId == review.ConversationId && m.Direction == "Outgoing" && m.MessageType != "Reaction"
                && m.Timestamp > review.VerifyAfterMessageAtUtc.Value, ct))
        {
            review.State = ReplyReviewState.NeedsHuman;
            review.LastError = "لم تُرصد رسالة معالجة جديدة بعد المراجعة. تجهيز المسودة وحده لا يثبت حل المشكلة.";
            return;
        }
        var analysis = await analyzer.AnalyzeAsync(review.ProjectId, review.ConversationId, ct);
        review.QualityScore = analysis.ReplyQualityScore;
        review.Summary = analysis.Summary;
        review.Recommendation = analysis.Recommendation;
        review.LastError = null;
        var conversation = await db.Conversations.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.ProjectId == review.ProjectId && c.Id == review.ConversationId, ct);
        var messages = await db.Messages.IgnoreQueryFilters().AsNoTracking().Where(m => m.ConversationId == review.ConversationId && m.MessageType != "Reaction")
            .OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id).Take(80).ToArrayAsync(ct);
        var hasFollowUpProblem = await HasFollowUpProblemAsync(review, ct);
        if (hasFollowUpProblem && analysis.HasUnresolvedReplyIssue == false)
        {
            review.NeedsResolution = true;
            review.State = ReplyReviewState.NeedsHuman;
            review.LastError = "توجد متابعة متأخرة أو فاشلة أو إرسال غير محسوم. يلزم مراجعتها قبل تأكيد اكتمال المعالجة.";
        }
        else if (!conversation.HumanHandoffReplyId.HasValue && ReplyReviewResolutionPolicy.HasResolutionEvidence(analysis, messages, review.VerifyAfterMessageAtUtc))
        {
            review.State = review.NeedsResolution ? ReplyReviewState.Resolved : ReplyReviewState.Reviewed;
            review.NeedsResolution = false;
            review.VerifyAfterMessageAtUtc = null;
        }
        else await PrepareTreatmentAsync(review, conversation.HumanHandoffReplyId, ct);
        if ((await queue.LatestMessageAsync(review.ConversationId, ct))?.Id != review.SourceMessageId
            || await queue.FollowUpChangedAtAsync(review.ProjectId, review.ConversationId, ct) != review.SourceFollowUpAtUtc)
            await RescheduleChangedAsync(review, ct);
    }

    private async Task PrepareTreatmentAsync(ReplyReviewCase review, Guid? humanHandoff, CancellationToken ct)
    {
        review.NeedsResolution = true;
        review.VerifyAfterMessageAtUtc ??= review.SourceMessageAtUtc;
        review.State = ReplyReviewState.NeedsHuman;
        var schedule = await db.ReplyReviewSchedules.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.ProjectId == review.ProjectId, ct);
        if (!schedule.Enabled) { review.State = ReplyReviewState.Queued; return; }
        if (!schedule.PrepareDrafts) return;
        var draft = await drafts.GenerateAsync(review.ProjectId, review.ConversationId, ct);
        review.DraftContent = draft.Content;
        review.DraftBasedOnMessageId = draft.BasedOnMessageId;
        review.DraftGeneratedAtUtc = draft.GeneratedAtUtc;
        review.VerifyAfterMessageAtUtc = draft.GeneratedAtUtc;
        review.State = humanHandoff.HasValue ? ReplyReviewState.NeedsHuman : ReplyReviewState.DraftReady;
        review.Phase = "Verify";
        review.NextRunAtUtc = DateTime.UtcNow.AddMinutes(schedule.VerifyAfterMinutes);
    }

    private async Task RescheduleChangedAsync(ReplyReviewCase review, CancellationToken ct)
    {
        var latest = await queue.LatestMessageAsync(review.ConversationId, ct)
            ?? throw new InvalidOperationException("Conversation has no messages.");
        var schedule = await queue.ScheduleAsync(review.ProjectId, ct);
        ReplyReviewQueue.Prepare(review, latest, DateTime.UtcNow.AddMinutes(schedule.QuietMinutes));
        review.SourceFollowUpAtUtc = await queue.FollowUpChangedAtAsync(review.ProjectId, review.ConversationId, ct);
        review.LastError = "تغيرت المحادثة أثناء المعالجة؛ جرى جدولة مراجعة جديدة بدل اعتماد نتيجة قديمة.";
    }

    private async Task<bool> HasFollowUpProblemAsync(ReplyReviewCase review, CancellationToken ct)
    {
        var followUps = await db.FollowUps.IgnoreQueryFilters().AsNoTracking().Where(f => f.ProjectId == review.ProjectId
            && f.ConversationId == review.ConversationId && f.DueDate <= DateTime.UtcNow).ToListAsync(ct);
        return followUps.Any(f => DailyReplyReviewPolicy.FollowUpHealth(f, DateTime.UtcNow) is "Overdue" or "Failed" or "Unknown" or "Unverified");
    }

    private async Task SaveOutcomeAsync(ReplyReviewCase review, ReplyReviewRun run, CancellationToken ct)
    {
        review.LeaseToken = null;
        review.LeaseUntilUtc = null;
        review.DispatchUntilUtc = null;
        if (review.State is ReplyReviewState.Reviewed or ReplyReviewState.Resolved or ReplyReviewState.DraftReady or ReplyReviewState.NeedsHuman)
            review.LastReviewedAtUtc = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        run.FinishedAtUtc = DateTime.UtcNow;
        run.Outcome = review.State.ToString();
        run.QualityScore = review.QualityScore;
        run.Summary = review.Summary;
        run.Error = review.LastError;
        db.ReplyReviewRuns.Add(run);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(review).State = EntityState.Detached;
            db.Entry(run).State = EntityState.Detached;
            logger.LogInformation("Discarded superseded reply review completion for case {CaseId}.", review.Id);
        }
    }
}
