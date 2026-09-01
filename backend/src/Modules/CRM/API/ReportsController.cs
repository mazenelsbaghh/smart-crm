using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.CRM.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/reports")]
public sealed class ReportsController(
    AppDbContext db,
    SalesIntelligenceService intelligence,
    ConversationSalesAnalyzer analyzer,
    IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet("sales-intelligence")]
    public async Task<IActionResult> GetSalesIntelligence(
        Guid projectId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var window = ResolveWindow(fromUtc, toUtc);
        if (window.Error is not null) return BadRequest(new { error = window.Error });
        return Ok(await intelligence.GetDashboardAsync(projectId, window.FromUtc, window.ToUtc, cancellationToken));
    }

    [HttpGet("sales-intelligence/schedule-demand")]
    public async Task<IActionResult> GetScheduleDemand(
        Guid projectId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] bool all,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var window = all
            ? new ReportWindow(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddMinutes(1), null)
            : ResolveWindow(fromUtc, toUtc);
        if (window.Error is not null) return BadRequest(new { error = window.Error });
        return Ok(await intelligence.GetScheduleDemandAsync(
            projectId, window.FromUtc, window.ToUtc, cancellationToken));
    }

    [HttpPost("sales-intelligence/schedule-demand/send-available")]
    public async Task<IActionResult> SendAvailableSchedules(
        Guid projectId,
        [FromBody] SendScheduleAvailabilityRequest? request,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (request?.CustomerIds is null || request.CustomerIds.Count == 0)
            return BadRequest(new { error = "اختر عميلًا واحدًا على الأقل." });
        if (request.CustomerIds.Count > 500)
            return BadRequest(new { error = "يمكن إرسال المواعيد إلى 500 عميل كحد أقصى في المرة الواحدة." });

        var result = await intelligence.QueueScheduleAvailabilityAsync(
            projectId, request.CustomerIds, cancellationToken);
        if (result.Queued > 0)
            BackgroundJob.Enqueue<FollowUpScheduler>(scheduler => scheduler.CheckOverdueFollowUpsJobAsync());
        return Ok(result);
    }

    [HttpPost("sales-intelligence/refresh")]
    public IActionResult RefreshSalesIntelligence(Guid projectId)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        return HistoricalAnalysisDisabled();
    }

    [HttpPost("sales-intelligence/analyze-all")]
    public IActionResult AnalyzeAllSalesConversations(Guid projectId)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        return HistoricalAnalysisDisabled();
    }

    [HttpPost("sales-intelligence/follow-ups")]
    public async Task<IActionResult> QueueFollowUpPlan(
        Guid projectId,
        [FromBody] QueueFollowUpPlanRequest? request,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var window = ResolveWindow(request?.FromUtc, request?.ToUtc);
        if (window.Error is not null) return BadRequest(new { error = window.Error });
        if (!Enum.TryParse<FollowUpPlanAction>(request?.Action, true, out var action) || !Enum.IsDefined(action))
            return BadRequest(new { error = "إجراء المتابعة غير مدعوم." });
        var queued = await intelligence.QueueFollowUpPlanAsync(
            new(projectId, window.FromUtc, window.ToUtc, action, request?.ConversationId, request?.PlanToken),
            cancellationToken);
        if (queued.PlanChanged)
            return Conflict(new { error = "تغيّرت قائمة العملاء منذ عرض التقرير. راجع الأرقام وحدّث التقرير قبل التأكيد." });
        if (action == FollowUpPlanAction.SendNow && queued.Queued > 0)
            BackgroundJob.Enqueue<FollowUpScheduler>(scheduler => scheduler.CheckOverdueFollowUpsJobAsync());
        return Ok(queued);
    }

    [HttpPost("conversations/{conversationId:guid}/analyze")]
    public async Task<IActionResult> AnalyzeConversation(
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        var lastMessageAtUtc = await db.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.ProjectId == projectId && conversation.Id == conversationId)
            .Select(conversation => (DateTime?)conversation.LastMessageTimestamp)
            .SingleOrDefaultAsync(cancellationToken);
        if (!lastMessageAtUtc.HasValue) return NotFound(new { error = "المحادثة غير موجودة في هذا المشروع." });
        if (!SalesAnalysisRecencyPolicy.Allows(lastMessageAtUtc.Value, DateTime.UtcNow))
            return HistoricalAnalysisDisabled();
        try
        {
            var analysis = await analyzer.ReanalyzeAsync(projectId, conversationId, cancellationToken);
            var context = await ConversationContextAsync(projectId, conversationId, cancellationToken);
            return Ok(SalesIntelligenceService.MapAnalysis(analysis, context.CustomerName, context.Channel));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    [HttpPatch("conversations/{conversationId:guid}/analysis")]
    public async Task<IActionResult> CorrectConversationAnalysis(
        Guid projectId,
        Guid conversationId,
        [FromBody] CorrectConversationAnalysisRequest? request,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanManageProject(User, projectId)) return Forbid();
        if (!Enum.TryParse<SalesLossReason>(request?.Reason, true, out var reason) || !Enum.IsDefined(reason))
            return BadRequest(new { error = "سبب التصحيح غير مدعوم." });
        if (request?.Notes?.Length > 1000)
            return BadRequest(new { error = "ملاحظات التصحيح يجب ألا تتجاوز 1000 حرف." });
        var userId = authorization.GetUserId(User);
        if (!userId.HasValue) return Forbid();
        try
        {
            await analyzer.CorrectAsync(
                new(projectId, conversationId, reason, request?.Notes, userId.Value),
                cancellationToken);
            var analysis = await analyzer.GetAsync(projectId, conversationId, cancellationToken);
            var context = await ConversationContextAsync(projectId, conversationId, cancellationToken);
            return Ok(SalesIntelligenceService.MapAnalysis(analysis!, context.CustomerName, context.Channel));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost("sales-intelligence/ask")]
    public async Task<IActionResult> AskSalesAnalyst(
        Guid projectId,
        [FromBody] AskSalesAnalystRequest? request,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var window = ResolveWindow(request?.FromUtc, request?.ToUtc);
        if (window.Error is not null) return BadRequest(new { error = window.Error });
        try
        {
            return Ok(await intelligence.AskAsync(
                new(projectId, window.FromUtc, window.ToUtc, request?.Question ?? string.Empty),
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }

    [HttpGet("daily-operations")]
    public async Task<IActionResult> GetDailyOperationsReport(Guid projectId, CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var conversations = db.Conversations.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CreatedAt >= today && item.CreatedAt < tomorrow);
        return Ok(new
        {
            projectId,
            date = today.ToString("yyyy-MM-dd"),
            totalConversations = await conversations.CountAsync(cancellationToken),
            activeConversations = await conversations.CountAsync(item => item.Status == "Open" || item.Status == "Pending", cancellationToken),
            completedConversations = await conversations.CountAsync(item => item.Status == "Resolved" || item.Status == "Closed", cancellationToken),
            missedFollowUps = await db.FollowUps.IgnoreQueryFilters().CountAsync(
                item => item.ProjectId == projectId && item.Status == "Missed" && item.UpdatedAt >= today && item.UpdatedAt < tomorrow,
                cancellationToken),
            outgoingMessages = await (from message in db.Messages.IgnoreQueryFilters()
                join conversation in db.Conversations.IgnoreQueryFilters() on message.ConversationId equals conversation.Id
                where conversation.ProjectId == projectId
                    && message.Direction == "Outgoing"
                    && message.Timestamp >= today
                    && message.Timestamp < tomorrow
                select message.Id).CountAsync(cancellationToken)
        });
    }

    [HttpGet("follow-ups")]
    public async Task<IActionResult> GetFollowUpsReport(Guid projectId, CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var followUps = db.FollowUps.IgnoreQueryFilters().Where(item => item.ProjectId == projectId);
        return Ok(new
        {
            projectId,
            pendingCount = await followUps.CountAsync(item => item.Status == "Pending", cancellationToken),
            missedCount = await followUps.CountAsync(item => item.Status == "Missed", cancellationToken),
            completedCount = await followUps.CountAsync(
                item => item.Status == "Completed" || item.Status == "Resolved" || item.Status == "Bypassed",
                cancellationToken)
        });
    }

    private async Task<(string CustomerName, string Channel)> ConversationContextAsync(
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var context = await (from conversation in db.Conversations.IgnoreQueryFilters()
            join customer in db.Customers.IgnoreQueryFilters() on conversation.CustomerId equals customer.Id
            where conversation.ProjectId == projectId && conversation.Id == conversationId
            select new { customer.Name, conversation.Channel }).SingleOrDefaultAsync(cancellationToken);
        return context is null ? ("عميل بدون اسم", "Unknown") : (context.Name, context.Channel);
    }

    private static ReportWindow ResolveWindow(DateTime? fromUtc, DateTime? toUtc)
    {
        var end = AsUtc(toUtc ?? DateTime.UtcNow);
        var start = AsUtc(fromUtc ?? end.AddDays(-7));
        if (start >= end) return new(start, end, "بداية الفترة يجب أن تسبق نهايتها.");
        if ((end - start).TotalDays > 90) return new(start, end, "أقصى فترة للتحليل هي 90 يومًا.");
        if (end > DateTime.UtcNow.AddMinutes(5)) return new(start, end, "نهاية الفترة لا يمكن أن تكون في المستقبل.");
        return new(start, end, null);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private ObjectResult HistoricalAnalysisDisabled() => StatusCode(
        StatusCodes.Status410Gone,
        new
        {
            code = "SALES_HISTORICAL_ANALYSIS_DISABLED",
            error = "تم إيقاف تحليل المحادثات القديمة. سيُحلل النظام النشاط الجديد فقط تلقائيًا."
        });

    private sealed record ReportWindow(DateTime FromUtc, DateTime ToUtc, string? Error);
}
