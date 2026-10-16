using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.CRM.API;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class CustomerReadController(AppDbContext db, IProjectAuthorizationService authorization) : ControllerBase
{
    [HttpGet("customers/page")]
    public async Task<IActionResult> CustomersPage(Guid projectId, [FromQuery] CustomerPageQuery request, CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var customers = CustomerReadQueries.Customers(db, projectId);
        var filtered = CustomerReadQueries.Filter(customers, request);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var totalFiltered = await filtered.CountAsync(cancellationToken);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, (int)Math.Ceiling(totalFiltered / (double)pageSize)));
        var labels = await db.Customers.Where(customer => customer.ProjectId == projectId)
            .GroupBy(customer => customer.Label == null || customer.Label == "" ? "بدون تصنيف" : customer.Label)
            .Select(group => new { name = group.Key, count = group.Count() }).ToListAsync(cancellationToken);
        return Ok(new
        {
            items = await filtered.OrderBy(customer => customer.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken),
            totalFiltered, totalCustomers = labels.Sum(label => label.count), page, pageSize, labels,
            stages = await customers.GroupBy(customer => customer.PipelineStage)
                .Select(group => new { name = group.Key, count = group.Count() }).ToListAsync(cancellationToken),
            cities = await customers.Where(customer => customer.City != "").Select(customer => customer.City)
                .Distinct().OrderBy(city => city).ToListAsync(cancellationToken)
        });
    }

    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> DashboardSummary(Guid projectId, [FromQuery] string? stage, CancellationToken cancellationToken)
    {
        if (!authorization.CanRead(User, projectId)) return Forbid();
        var totals = await db.Projects.Where(project => project.Id == projectId).Select(project => new
        {
            customers = db.Customers.Count(customer => customer.ProjectId == projectId),
            chats = db.Conversations.Count(conversation => conversation.ProjectId == projectId),
            totalDeals = db.Deals.Count(deal => deal.ProjectId == projectId),
            wonDeals = db.Deals.Count(deal => deal.ProjectId == projectId && deal.Status == DealStatus.Won)
        }).SingleOrDefaultAsync(cancellationToken);
        if (totals is null) return NotFound();
        var customers = CustomerReadQueries.Customers(db, projectId);
        var stages = await customers.Select(customer => customer.PipelineStage).Distinct().ToListAsync(cancellationToken);
        if (!string.IsNullOrEmpty(stage) && stage != "All") customers = customers.Where(customer => customer.PipelineStage == stage);
        return Ok(new
        {
            totals.customers, totals.chats, totals.totalDeals, totals.wonDeals, stages,
            topLeads = await customers.OrderByDescending(customer => customer.LeadScore).ThenBy(customer => customer.Id)
                .Take(10).ToListAsync(cancellationToken)
        });
    }
}

public sealed class CustomerPageQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public string? Stage { get; set; }
    public string? City { get; set; }
    public string? Label { get; set; }
}
