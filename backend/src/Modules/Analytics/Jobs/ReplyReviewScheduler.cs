using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Shared.Infrastructure;

namespace Modules.Analytics.Jobs;

public sealed class ReplyReviewScheduler(AppDbContext db, ReplyReviewQueue queue, IBackgroundJobClient jobs)
{
    private const int MaxRunsPerProjectPerDay = 100;

    [DisableConcurrentExecution(60)]
    [AutomaticRetry(Attempts = 0)]
    public async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var configured = await db.ProjectSettings.IgnoreQueryFilters().Where(s => s.GeminiApiKey != null && s.GeminiApiKey != "")
            .Select(s => s.ProjectId).ToListAsync(ct);
        foreach (var projectId in configured) await queue.ScheduleAsync(projectId, ct);
        var due = await db.ReplyReviewSchedules.IgnoreQueryFilters().Where(s => s.Enabled && s.NextScanAtUtc <= now).ToListAsync(ct);
        foreach (var schedule in due) await ScanAsync(schedule, now, ct);
        await RecoverExpiredAsync(now, ct);
        await DispatchAsync(now, ct);
    }

    private async Task ScanAsync(ReplyReviewSchedule schedule, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-2);
        var quiet = now.AddMinutes(-schedule.QuietMinutes);
        var candidates = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.ProjectId == schedule.ProjectId && c.LastMessageTimestamp <= quiet
                && (c.LastMessageTimestamp >= cutoff || db.FollowUps.IgnoreQueryFilters().Any(f => f.ProjectId == schedule.ProjectId
                    && f.ConversationId == c.Id && f.DueDate >= cutoff && f.DueDate <= now)))
            .Select(c => new { c.Id, Latest = db.Messages.IgnoreQueryFilters().Where(m => m.ConversationId == c.Id && m.MessageType != "Reaction")
                .OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id).Select(m => (Guid?)m.Id).FirstOrDefault(), c.LastMessageTimestamp,
                FollowUpAt = db.FollowUps.IgnoreQueryFilters().Where(f => f.ProjectId == schedule.ProjectId && f.ConversationId == c.Id).Max(f => (DateTime?)f.UpdatedAt) })
            .Where(c => c.Latest != null && !db.ReplyReviewCases.IgnoreQueryFilters().Any(r => r.ProjectId == schedule.ProjectId
                && r.ConversationId == c.Id && ((r.SourceMessageId == c.Latest && (c.FollowUpAt == null || r.SourceFollowUpAtUtc >= c.FollowUpAt))
                    || r.State == ReplyReviewState.Reviewing)))
            .OrderBy(c => c.LastMessageTimestamp).ThenBy(c => c.Id).Take(schedule.BatchSize).ToListAsync(ct);
        foreach (var candidate in candidates) await queue.EnqueueAsync(schedule.ProjectId, candidate.Id, "Automatic", ct);
        schedule.LastScanAtUtc = now;
        schedule.NextScanAtUtc = now.AddMinutes(schedule.IntervalMinutes);
        await db.SaveChangesAsync(ct);
    }

    private Task<int> RecoverExpiredAsync(DateTime now, CancellationToken ct) => db.ReplyReviewCases.IgnoreQueryFilters()
        .Where(r => r.State == ReplyReviewState.Reviewing && r.LeaseUntilUtc < now)
        .ExecuteUpdateAsync(set => set
            .SetProperty(r => r.State, r => r.Attempts >= 3 ? ReplyReviewState.Failed : ReplyReviewState.RetryScheduled)
            .SetProperty(r => r.NextRunAtUtc, now.AddMinutes(5)).SetProperty(r => r.LeaseToken, (Guid?)null)
            .SetProperty(r => r.LeaseUntilUtc, (DateTime?)null).SetProperty(r => r.DispatchUntilUtc, (DateTime?)null)
            .SetProperty(r => r.LastError, "انقطع التنفيذ قبل تسجيل النتيجة. لم تُعتبر المعالجة ناجحة.")
            .SetProperty(r => r.UpdatedAt, now), ct);

    private async Task DispatchAsync(DateTime now, CancellationToken ct)
    {
        var inFlight = await db.ReplyReviewCases.IgnoreQueryFilters().CountAsync(r => r.DispatchUntilUtc > now || r.State == ReplyReviewState.Reviewing, ct);
        var due = await db.ReplyReviewCases.IgnoreQueryFilters().Where(r =>
                (r.State == ReplyReviewState.Queued || r.State == ReplyReviewState.RetryScheduled || r.State == ReplyReviewState.VerifyScheduled)
                && r.NextRunAtUtc <= now && (r.DispatchUntilUtc == null || r.DispatchUntilUtc <= now)
                && db.ReplyReviewSchedules.IgnoreQueryFilters().Any(s => s.ProjectId == r.ProjectId && s.Enabled))
            .OrderBy(r => r.NextRunAtUtc).ThenBy(r => r.Id).Take(Math.Max(0, 10 - inFlight))
            .Select(r => new { r.Id, r.ProjectId }).ToListAsync(ct);
        foreach (var review in due)
        {
            if (!await HasDailyCapacityAsync(review.ProjectId, now, ct)) continue;
            var claimed = await db.ReplyReviewCases.IgnoreQueryFilters().Where(r => r.Id == review.Id && (r.DispatchUntilUtc == null || r.DispatchUntilUtc <= now))
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.DispatchUntilUtc, now.AddMinutes(30)), ct);
            if (claimed == 1) jobs.Enqueue<ReplyReviewWorker>(worker => worker.ExecuteAsync(review.Id, CancellationToken.None));
        }
    }

    private async Task<bool> HasDailyCapacityAsync(Guid projectId, DateTime now, CancellationToken ct)
    {
        var completed = await db.ReplyReviewRuns.IgnoreQueryFilters()
            .CountAsync(run => run.ProjectId == projectId && run.StartedAtUtc >= now.AddDays(-1), ct);
        var reserved = await db.ReplyReviewCases.IgnoreQueryFilters().CountAsync(review => review.ProjectId == projectId
            && (review.State == ReplyReviewState.Reviewing || review.DispatchUntilUtc > now), ct);
        return completed + reserved < MaxRunsPerProjectPerDay;
    }
}
