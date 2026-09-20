using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Shared.Security;

namespace Modules.CRM.API;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/reports/daily-review")]
public sealed class DailyReplyReviewController(DailyReplyReviewService review, ConversationSalesAnalyzer analyzer,
    CorrectiveReplyDraftService drafts, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, [FromQuery] DateOnly? date, [FromQuery] int page = 1,
        [FromQuery] string view = "attention", CancellationToken cancellationToken = default)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        if (page is < 1 or > 100000 || view is not ("attention" or "all" or "unanalyzed"))
            return BadRequest(new { error = "فلتر أو رقم صفحة غير صالح." });
        try { return Ok(await review.GetAsync(new(projectId, date, page, view), cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("conversations/{conversationId:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid projectId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        try
        {
            var result = await analyzer.ReanalyzeAsync(projectId, conversationId, cancellationToken);
            return Ok(new { result.AnalyzedAtUtc });
        }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }

    [HttpPost("conversations/{conversationId:guid}/draft-reply")]
    public async Task<IActionResult> Draft(Guid projectId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        try { return Ok(await drafts.GenerateAsync(projectId, conversationId, cancellationToken)); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (StaleCorrectiveDraftException exception) { return Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return StatusCode(502, new { error = exception.Message }); }
    }
}
