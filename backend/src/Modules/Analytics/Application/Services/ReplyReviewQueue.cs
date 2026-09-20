using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Npgsql;
using Shared.Infrastructure;

namespace Modules.Analytics.Application.Services;

public sealed class ReplyReviewQueue(AppDbContext db)
{
    public async Task<ReplyReviewSchedule> ScheduleAsync(Guid projectId, CancellationToken ct)
    {
        var schedule = await db.ReplyReviewSchedules.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (schedule != null) return schedule;
        if (!await db.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == projectId, ct))
            throw new KeyNotFoundException("إعدادات المشروع غير موجودة.");
        schedule = new() { ProjectId = projectId };
        db.ReplyReviewSchedules.Add(schedule);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_ReplyReviewSchedules_ProjectId" })
        {
            db.Entry(schedule).State = EntityState.Detached;
            return await db.ReplyReviewSchedules.IgnoreQueryFilters().SingleAsync(s => s.ProjectId == projectId, ct);
        }
        return schedule;
    }

    public async Task<ReplyReviewCase> EnqueueAsync(Guid projectId, Guid conversationId, string reason, CancellationToken ct)
    {
        if (!await db.Conversations.IgnoreQueryFilters().AnyAsync(c => c.Id == conversationId && c.ProjectId == projectId, ct))
            throw new KeyNotFoundException("المحادثة غير موجودة في هذا المشروع.");
        var latest = await LatestMessageAsync(conversationId, ct)
            ?? throw new InvalidOperationException("لا توجد رسائل لمراجعتها.");
        var schedule = await ScheduleAsync(projectId, ct);
        var followUpAt = await FollowUpChangedAtAsync(projectId, conversationId, ct);
        var review = await db.ReplyReviewCases.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.ProjectId == projectId && c.ConversationId == conversationId, ct);
        if (review?.State == ReplyReviewState.Reviewing && review.LeaseUntilUtc > DateTime.UtcNow) return review;
        if (review != null && reason == "Automatic" && review.SourceMessageId == latest.Id && review.SourceFollowUpAtUtc == followUpAt) return review;
        if (reason == "Verify" && review?.NeedsResolution != true)
            throw new InvalidOperationException("يلزم وجود مشكلة قيد المعالجة قبل جدولة التحقق.");
        if (review == null)
        {
            review = new() { ProjectId = projectId, ConversationId = conversationId };
            db.ReplyReviewCases.Add(review);
        }
        Prepare(review, latest, reason == "Verify" ? DateTime.UtcNow.AddMinutes(schedule.VerifyAfterMinutes) : DateTime.UtcNow);
        review.SourceFollowUpAtUtc = followUpAt;
        if (reason == "Retry") review.Phase = "Review";
        if (reason == "Verify") review.State = ReplyReviewState.VerifyScheduled;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            await db.Entry(review).ReloadAsync(ct);
            return review;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505", ConstraintName: "IX_ReplyReviewCases_ProjectId_ConversationId" })
        {
            db.Entry(review).State = EntityState.Detached;
            return await db.ReplyReviewCases.IgnoreQueryFilters().SingleAsync(c => c.ProjectId == projectId && c.ConversationId == conversationId, ct);
        }
        return review;
    }

    internal Task<Message?> LatestMessageAsync(Guid conversationId, CancellationToken ct) => db.Messages.IgnoreQueryFilters().AsNoTracking()
        .Where(m => m.ConversationId == conversationId && m.MessageType != "Reaction")
        .OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);

    internal Task<DateTime?> FollowUpChangedAtAsync(Guid projectId, Guid conversationId, CancellationToken ct) =>
        db.FollowUps.IgnoreQueryFilters().Where(f => f.ProjectId == projectId && f.ConversationId == conversationId)
            .MaxAsync(f => (DateTime?)f.UpdatedAt, ct);

    internal static void Prepare(ReplyReviewCase review, Message latest, DateTime due)
    {
        review.SourceMessageId = latest.Id;
        review.SourceMessageAtUtc = latest.Timestamp;
        review.State = ReplyReviewState.Queued;
        review.Phase = review.NeedsResolution ? "Verify" : "Review";
        review.NextRunAtUtc = due;
        review.DispatchUntilUtc = null;
        review.LeaseToken = null;
        review.LeaseUntilUtc = null;
        review.Attempts = 0;
        review.LastError = null;
        review.QualityScore = null;
        review.Summary = "";
        review.Recommendation = "";
        review.DraftContent = "";
        review.DraftBasedOnMessageId = null;
        review.DraftGeneratedAtUtc = null;
        review.UpdatedAt = DateTime.UtcNow;
    }
}
