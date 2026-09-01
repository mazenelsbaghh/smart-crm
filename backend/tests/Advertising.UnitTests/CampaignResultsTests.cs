using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Campaigns.API;
using Modules.Campaigns.Application.Services;
using Modules.Campaigns.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CampaignResultsTests
{
    [Fact]
    public async Task Results_count_only_confirmed_delivery_lifecycle_statuses_as_sent()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var campaign = new Campaign
        {
            ProjectId = projectId,
            Name = "Status accounting",
            SegmentId = Guid.NewGuid(),
            MessageTemplateA = "Template",
            MessageTemplateB = "Template",
            Status = CampaignStatus.Running
        };
        var statuses = Enum.GetValues<RecipientStatus>();
        db.Campaigns.Add(campaign);
        db.CampaignRecipients.AddRange(statuses.Select(status => new CampaignRecipient
        {
            ProjectId = projectId,
            CampaignId = campaign.Id,
            CustomerId = Guid.NewGuid(),
            Variant = "A",
            Status = status
        }));
        await db.SaveChangesAsync();
        var controller = new CampaignsController(
            db,
            new StubCampaignAiService(),
            new WhatsAppAccountService(db),
            new ProjectAuthorizationService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = User(projectId) }
            }
        };

        var response = Assert.IsType<OkObjectResult>(await controller.GetCampaignResults(campaign.Id));
        var body = JsonSerializer.SerializeToElement(response.Value);
        var variantA = body.GetProperty("variants").GetProperty("A");

        Assert.Equal(4, variantA.GetProperty("sent").GetInt32());
        Assert.Equal(3, variantA.GetProperty("delivered").GetInt32());
        Assert.Equal(1, variantA.GetProperty("responded").GetInt32());
    }

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private static ClaimsPrincipal User(Guid projectId) => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Owner"),
            new Claim("ProjectId", projectId.ToString())
        ],
        "Test"));

    private sealed class StubCampaignAiService : ICampaignAIService
    {
        public Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext) =>
            Task.FromResult(baseTemplate);

        public Task<string> GenerateProjectCampaignCopyAsync(
            Guid projectId,
            string prompt,
            string baseTemplate,
            string targetContext) => Task.FromResult(baseTemplate);
    }
}
