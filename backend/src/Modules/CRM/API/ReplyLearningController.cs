using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

public sealed record ReplyLessonUpdate(bool Enabled);

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/reports/reply-learning")]
public sealed class ReplyLearningController(AppDbContext db, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var lessons = await db.ReplyLessons.IgnoreQueryFilters().AsNoTracking().Where(l => l.ProjectId == projectId)
            .OrderBy(l => l.Channel).ThenBy(l => l.Code)
            .Select(l => new { l.Id, l.Channel, l.Code, l.Enabled, l.UpdatedAt,
                SupportConversations = db.ReplyLessonEvidence.IgnoreQueryFilters().Count(e => e.ProjectId == projectId && e.LessonId == l.Id) })
            .ToListAsync(ct);
        return Ok(lessons.Select(l => new { l.Id, l.Channel, l.Code, l.Enabled, l.UpdatedAt, l.SupportConversations,
            Instruction = ReplyLearningService.Lessons.GetValueOrDefault(l.Code), Active = l.Enabled && l.SupportConversations >= 2 }));
    }

    [HttpPut("{lessonId:guid}")]
    public async Task<IActionResult> Update(Guid projectId, Guid lessonId, ReplyLessonUpdate request, CancellationToken ct)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var lesson = await db.ReplyLessons.IgnoreQueryFilters().SingleOrDefaultAsync(l => l.ProjectId == projectId && l.Id == lessonId, ct);
        if (lesson is null) return NotFound();
        lesson.Enabled = request.Enabled;
        lesson.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
