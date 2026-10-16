using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Conversations.Domain;
using Modules.CRM.API;
using Modules.CRM.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CustomerPersistenceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private IsolatedPostgresDatabase isolated = null!;

    public async Task InitializeAsync()
    {
        isolated = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = isolated.CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => isolated.DisposeAsync().AsTask();

    [Fact]
    public async Task Customer_pages_preserve_global_filter_counts_and_filter_beyond_the_first_page()
    {
        var tenant = Tenant();
        await using var db = isolated.CreateContext(tenant);
        var customer = await SeedAsync(db, tenant.ProjectId);
        var others = Enumerable.Range(1, 12).Select(index => new Customer
        {
            ProjectId = tenant.ProjectId, Name = $"Customer {index}", PhoneNumber = $"20100000{index:0000}",
            City = index == 12 ? "Alexandria" : "Cairo", Label = index == 12 ? "Interested" : null
        }).ToArray();
        var stage = new PipelineStage { ProjectId = tenant.ProjectId, Name = "Qualified", Order = 0 };
        db.AddRange(others);
        db.Add(stage);
        db.Add(new Deal { ProjectId = tenant.ProjectId, CustomerId = others[^1].Id, Title = "Latest deal", PipelineStageId = stage.Id });
        await db.SaveChangesAsync();
        var controller = new CustomerReadController(db, new ProjectAuthorizationService()) { ControllerContext = Controller(db, tenant).ControllerContext };

        var page = Body(await controller.CustomersPage(tenant.ProjectId, new CustomerPageQuery { Page = 999, PageSize = 5 }, default));
        Assert.Equal(3, page.GetProperty("page").GetInt32());
        Assert.Equal(13, page.GetProperty("totalFiltered").GetInt32());
        Assert.Equal(3, page.GetProperty("items").GetArrayLength());
        Assert.Equal(13, page.GetProperty("labels").EnumerateArray().Sum(label => label.GetProperty("count").GetInt32()));
        var filtered = Body(await controller.CustomersPage(tenant.ProjectId,
            new CustomerPageQuery { Search = "CUSTOMER 12", Stage = "Qualified", City = "Alexandria", Label = "Interested" }, default));
        Assert.Equal(1, filtered.GetProperty("totalFiltered").GetInt32());
        Assert.Equal(13, filtered.GetProperty("totalCustomers").GetInt32());
        Assert.Equal(others[^1].Id, filtered.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Dashboard_counts_all_conversations_and_returns_only_ten_ranked_customers_in_the_authorized_project()
    {
        var tenant = Tenant();
        await using var db = isolated.CreateContext(tenant);
        var customer = await SeedAsync(db, tenant.ProjectId);
        db.Customers.AddRange(Enumerable.Range(1, 12).Select(index => new Customer
        { ProjectId = tenant.ProjectId, Name = $"Rank {index}", PhoneNumber = $"rank-{index}", City = "Cairo", LeadScore = index }));
        db.Conversations.AddRange(Enumerable.Range(1, 105).Select(_ => new Conversation
        { ProjectId = tenant.ProjectId, CustomerId = customer.Id, Channel = "WhatsApp", Status = "Open" }));
        var otherProject = new Project { Name = "Other tenant" };
        db.AddRange(otherProject, new Customer { ProjectId = otherProject.Id, Name = "Must not leak", PhoneNumber = "other", City = "", LeadScore = 100 });
        await db.SaveChangesAsync();
        var controller = new CustomerReadController(db, new ProjectAuthorizationService()) { ControllerContext = Controller(db, tenant).ControllerContext };

        var summary = Body(await controller.DashboardSummary(tenant.ProjectId, null, default));

        Assert.Equal(13, summary.GetProperty("customers").GetInt32());
        Assert.Equal(105, summary.GetProperty("chats").GetInt32());
        Assert.Equal(10, summary.GetProperty("topLeads").GetArrayLength());
        Assert.Equal("Rank 12", summary.GetProperty("topLeads")[0].GetProperty("name").GetString());
        Assert.DoesNotContain(summary.GetProperty("topLeads").EnumerateArray(), lead => lead.GetProperty("projectId").GetGuid() != tenant.ProjectId);
        Assert.IsType<ForbidResult>(await controller.DashboardSummary(otherProject.Id, null, default));
    }

    [Theory]
    [InlineData("classification")]
    [InlineData("consent")]
    [InlineData("legal-basis")]
    public async Task Rejected_customer_update_does_not_commit_profile_or_new_pipeline_stage(string invalidField)
    {
        var tenant = Tenant();
        await using var db = isolated.CreateContext(tenant);
        var customer = await SeedAsync(db, tenant.ProjectId);
        var request = new UpdateCustomerRequest { Name = "Changed", PipelineStage = "New custom stage" };
        if (invalidField == "classification") request.SalesClassification = "invalid";
        if (invalidField == "consent") request.AdvertisingConsentState = "invalid";
        if (invalidField == "legal-basis") request.AdvertisingConsentState = "NotRequired";

        Assert.IsType<UnprocessableEntityObjectResult>(await Controller(db, tenant).UpdateCustomer(customer.Id, request));

        await using var verification = isolated.CreateContext(tenant);
        Assert.Equal("Original", (await verification.Customers.SingleAsync()).Name);
        Assert.Empty(await verification.PipelineStages.ToListAsync());
        Assert.Empty(await verification.Deals.ToListAsync());
        Assert.Empty(await verification.IntegrationOutboxMessages.Where(message => message.PayloadJson.Contains(tenant.ProjectId.ToString())).ToListAsync());
        Assert.Single(await verification.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Successful_customer_update_commits_profile_deal_stage_audit_and_outcome_together()
    {
        var tenant = Tenant();
        await using var db = isolated.CreateContext(tenant);
        var customer = await SeedAsync(db, tenant.ProjectId);

        Assert.IsType<OkObjectResult>(await Controller(db, tenant).UpdateCustomer(customer.Id,
            new UpdateCustomerRequest { Name = "Changed", PipelineStage = "Won", Budget = 123.45m }));

        await using var verification = isolated.CreateContext(tenant);
        Assert.Equal("Changed", (await verification.Customers.SingleAsync()).Name);
        var stage = await verification.PipelineStages.SingleAsync();
        var deal = await verification.Deals.SingleAsync();
        Assert.Equal("Won", stage.Name);
        Assert.Equal(0, stage.Order);
        Assert.Equal(stage.Id, deal.PipelineStageId);
        Assert.Equal(123.45m, deal.Amount);
        Assert.Equal(Modules.CRM.Domain.DealStatus.Won, deal.Status);
        Assert.NotNull(deal.ClosedAt);
        Assert.Contains(await verification.AuditLogs.ToListAsync(), log => log.Action == "ModifiedCustomer");
        Assert.Single(await verification.IntegrationOutboxMessages
            .Where(message => message.EventType == "DealOutcomeChanged.v2" && message.PayloadJson.Contains(tenant.ProjectId.ToString())).ToListAsync());
    }

    [Fact]
    public async Task Database_rejection_rolls_back_customer_stage_deal_and_audit()
    {
        var tenant = Tenant();
        await using var db = isolated.CreateContext(tenant);
        var customer = await SeedAsync(db, tenant.ProjectId);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Deals" ADD CONSTRAINT "RejectTestAmount"
            CHECK ("Amount" <> 123.45) NOT VALID
            """);

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => Controller(db, tenant).UpdateCustomer(customer.Id,
                new UpdateCustomerRequest { Name = "Changed", PipelineStage = "Won", Budget = 123.45m }));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Deals\" DROP CONSTRAINT \"RejectTestAmount\"");
        }

        await using var verification = isolated.CreateContext(tenant);
        Assert.Equal("Original", (await verification.Customers.SingleAsync()).Name);
        Assert.Empty(await verification.PipelineStages.ToListAsync());
        Assert.Empty(await verification.Deals.ToListAsync());
        Assert.Empty(await verification.IntegrationOutboxMessages.Where(message => message.PayloadJson.Contains(tenant.ProjectId.ToString())).ToListAsync());
        Assert.Single(await verification.AuditLogs.ToListAsync());
    }

    private static TenantContext Tenant()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        return tenant;
    }

    private static JsonElement Body(IActionResult response) => JsonSerializer.SerializeToElement(
        Assert.IsType<OkObjectResult>(response).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<Customer> SeedAsync(AppDbContext db, Guid projectId)
    {
        var customer = new Customer { ProjectId = projectId, Name = "Original", PhoneNumber = "201012345678", City = "Cairo" };
        db.AddRange(new Project { Id = projectId, Name = "Customer persistence regression" }, customer);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return customer;
    }

    private static CRMController Controller(AppDbContext db, TenantContext tenant) =>
        new(db, null!, null!, new ConfigurationBuilder().Build(), null!, tenant, null!, new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("ProjectId", tenant.ProjectId.ToString()), new Claim(ClaimTypes.Role, "Owner")], "test"))
            } }
        };
}
