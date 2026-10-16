using Shared.Infrastructure;

namespace Modules.CRM.API;

internal static class CustomerReadQueries
{
    public static IQueryable<CustomerListItem> Customers(AppDbContext db, Guid projectId) =>
        db.Customers.Where(customer => customer.ProjectId == projectId).Select(customer => new CustomerListItem
        {
            Id = customer.Id, ProjectId = customer.ProjectId, Name = customer.Name, PhoneNumber = customer.PhoneNumber,
            City = customer.City, LeadScore = customer.LeadScore, Tags = customer.Tags, Budget = customer.Budget,
            Label = customer.Label, IsBlacklisted = customer.IsBlacklisted,
            Notes = customer.Notes, Interests = customer.Interests, PurchaseProbability = customer.PurchaseProbability,
            AIInsights = customer.AIInsights, AutomationRules = customer.AutomationRules,
            PipelineStage = db.PipelineStages.Where(stage => stage.ProjectId == projectId && stage.Id ==
                    db.Deals.Where(deal => deal.ProjectId == projectId && deal.CustomerId == customer.Id)
                        .OrderByDescending(deal => deal.ClosedAt ?? deal.CreatedAt).ThenByDescending(deal => deal.Id)
                        .Select(deal => (Guid?)deal.PipelineStageId).FirstOrDefault())
                .Select(stage => stage.Name).FirstOrDefault() ?? "New"
        });

    public static IQueryable<CustomerListItem> Filter(IQueryable<CustomerListItem> customers, CustomerPageQuery request)
    {
        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLowerInvariant();
            customers = customers.Where(customer => customer.Name.ToLower().Contains(search) || customer.PhoneNumber.Contains(request.Search));
        }
        if (!string.IsNullOrEmpty(request.Stage) && request.Stage != "All")
            customers = customers.Where(customer => customer.PipelineStage == request.Stage);
        if (!string.IsNullOrEmpty(request.City) && request.City != "All")
            customers = customers.Where(customer => customer.City == request.City);
        if (!string.IsNullOrEmpty(request.Label) && request.Label != "All")
            customers = customers.Where(customer => (customer.Label == null || customer.Label == "" ? "بدون تصنيف" : customer.Label) == request.Label);
        return customers;
    }
}

public sealed class CustomerListItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int LeadScore { get; set; }
    public string[] Tags { get; set; } = [];
    public decimal? Budget { get; set; }
    public string? Label { get; set; }
    public bool IsBlacklisted { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string[] Interests { get; set; } = [];
    public int PurchaseProbability { get; set; }
    public string? AIInsights { get; set; }
    public string? AutomationRules { get; set; }
    public string PipelineStage { get; set; } = "New";
}
