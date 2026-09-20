using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

public sealed record ReviewScheduleUpdate(bool Enabled, bool PrepareDrafts, int IntervalMinutes, int QuietMinutes, int VerifyAfterMinutes, int BatchSize);

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/reports/daily-review/processing")]
public sealed class ReplyReviewProcessingController(AppDbContext db, ReplyReviewQueue queue, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, [FromQuery] int page = 1, [FromQuery] string view = "active", CancellationToken ct = default)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        if (page is < 1 or > 100000 || view is not ("active" or "all")) return BadRequest();
        var schedule = await db.ReplyReviewSchedules.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        if (settings == null) return NotFound();
        var source = db.ReplyReviewCases.IgnoreQueryFilters().AsNoTracking().Where(r => r.ProjectId == projectId);
        var counts = await source.GroupBy(r => r.State).Select(group => new { State = group.Key, Count = group.Count() }).ToListAsync(ct);
        var filtered = view == "all" ? source : source.Where(r => r.State != ReplyReviewState.Reviewed && r.State != ReplyReviewState.Resolved);
        var total = await filtered.CountAsync(ct);
        var cases = await filtered.OrderByDescending(r => r.UpdatedAt).ThenBy(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var ids = cases.Select(r => r.ConversationId).ToArray();
        var customers = await db.Conversations.IgnoreQueryFilters().Where(c => c.ProjectId == projectId && ids.Contains(c.Id))
            .Join(db.Customers.IgnoreQueryFilters().Where(c => c.ProjectId == projectId), c => c.CustomerId, customer => customer.Id,
                (c, customer) => new { c.Id, c.Channel, customer.Name }).ToDictionaryAsync(c => c.Id, ct);
        return Ok(new { Schedule = schedule ?? new ReplyReviewSchedule { ProjectId = projectId },
            AnalysisConfigured = !string.IsNullOrWhiteSpace(settings.GeminiApiKey), Counts = counts.ToDictionary(c => c.State.ToString(), c => c.Count),
            Page = page, PageSize = 20, Total = total, Cases = cases.Select(r => new { r.Id, r.ConversationId,
                CustomerName = customers.GetValueOrDefault(r.ConversationId)?.Name ?? "عميل", Channel = customers.GetValueOrDefault(r.ConversationId)?.Channel,
                State = r.State.ToString(), r.Phase, r.NextRunAtUtc, r.LastReviewedAtUtc, r.Attempts, r.QualityScore,
                r.Summary, r.Recommendation, r.LastError, HasDraft = r.DraftContent.Length > 0, r.NeedsResolution }) });
    }

    [HttpPut("schedule")]
    public async Task<IActionResult> Update(Guid projectId, ReviewScheduleUpdate request, CancellationToken ct)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (request.IntervalMinutes is < 5 or > 1440 || request.QuietMinutes is < 1 or > 120
            || request.VerifyAfterMinutes is < 5 or > 1440 || request.BatchSize is < 1 or > 50)
            return BadRequest(new { error = "اختر فترة مراجعة وتحقق بين ٥ و١٤٤٠ دقيقة، وانتظارًا بين دقيقة و١٢٠، ودفعة بين ١ و٥٠." });
        try
        {
            var schedule = await queue.ScheduleAsync(projectId, ct);
            schedule.Enabled = request.Enabled;
            schedule.PrepareDrafts = request.PrepareDrafts;
            schedule.IntervalMinutes = request.IntervalMinutes;
            schedule.QuietMinutes = request.QuietMinutes;
            schedule.VerifyAfterMinutes = request.VerifyAfterMinutes;
            schedule.BatchSize = request.BatchSize;
            schedule.NextScanAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok(schedule);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan(Guid projectId, CancellationToken ct)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            var schedule = await queue.ScheduleAsync(projectId, ct);
            if (!schedule.Enabled) return Conflict(new { error = "فعّل الجدولة أولًا." });
            schedule.NextScanAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Accepted(new { message = "تمت جدولة فحص الدفعة التالية خلال دقيقة." });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<IActionResult> Detail(Guid projectId, Guid conversationId, CancellationToken ct)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var review = await db.ReplyReviewCases.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.ConversationId == conversationId, ct);
        if (review == null) return NotFound();
        var latest = await queue.LatestMessageAsync(conversationId, ct);
        var followUpAt = await queue.FollowUpChangedAtAsync(projectId, conversationId, ct);
        var runs = await db.ReplyReviewRuns.IgnoreQueryFilters().AsNoTracking().Where(r => r.ProjectId == projectId && r.CaseId == review.Id)
            .OrderByDescending(r => r.FinishedAtUtc).ThenBy(r => r.Id).Take(10).ToListAsync(ct);
        return Ok(new { review.Id, review.ConversationId, State = review.State.ToString(), review.Phase, review.DraftContent,
            DraftOutdated = review.DraftBasedOnMessageId != latest?.Id || review.SourceFollowUpAtUtc != followUpAt,
            review.DraftGeneratedAtUtc, Runs = runs });
    }

    [HttpPost("conversations/{conversationId:guid}/{operation:regex(^(review|verify|retry)$)}")]
    public async Task<IActionResult> Enqueue(Guid projectId, Guid conversationId, string operation, CancellationToken ct)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            var reason = operation switch { "verify" => "Verify", "retry" => "Retry", _ => "Review" };
            var review = await queue.EnqueueAsync(projectId, conversationId, reason, ct);
            return Accepted(new { review.Id, State = review.State.ToString(), review.NextRunAtUtc });
        }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }
}
