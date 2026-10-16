using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Approvals.API;
using Modules.Approvals.Domain;
using Modules.Approvals.Services;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ApprovalExecutionTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("DeleteCustomer")]
    [InlineData("SendDiscount")]
    [InlineData("UnknownAction")]
    public async Task Unsupported_actions_are_rejected_and_existing_requests_are_never_marked_executed(string action)
    {
        await using var isolated = await postgres.CreateIsolatedDatabaseAsync();
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = isolated.CreateContext(tenant);
        await db.Database.MigrateAsync();
        var controller = new ApprovalsController(db, new RiskAnalyzer())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))
            } }
        };
        Assert.IsType<UnprocessableEntityObjectResult>(await controller.ExecuteAction(projectId,
            new ExecuteActionRequest { ActionType = action, PayloadJson = "{}" }));
        Assert.Empty(await db.ApprovalRequests.ToListAsync());
        var legacy = new ApprovalRequest { ProjectId = projectId, ActionType = action, RiskLevel = "High" };
        db.ApprovalRequests.Add(legacy);
        await db.SaveChangesAsync();

        Assert.IsType<UnprocessableEntityObjectResult>(await controller.ApproveRequest(legacy.Id));

        db.ChangeTracker.Clear();
        var saved = await db.ApprovalRequests.SingleAsync();
        Assert.Equal("Pending", saved.Status);
        Assert.Null(saved.ExecutedAt);
        Assert.Null(saved.ApprovedBy);
    }
}
