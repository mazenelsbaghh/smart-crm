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
            Status = CampaignStatus.Running,
            SentCount = 4,
            DeliveredCount = 99,
            ReadCount = 88,
            ResponseCount = 77
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
        Assert.Equal(4, body.GetProperty("sentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("deliveredCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("readCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("responseCount").ValueKind);
        foreach (var variant in body.GetProperty("variants").EnumerateObject())
        {
            Assert.Equal(JsonValueKind.Null, variant.Value.GetProperty("delivered").ValueKind);
            Assert.Equal(JsonValueKind.Null, variant.Value.GetProperty("responded").ValueKind);
        }
    }

    [Theory]
    [InlineData("list")]
    [InlineData("detail")]
    public async Task Campaign_reads_do_not_present_legacy_send_acceptance_as_delivery_receipts(string view)
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var campaign = new Campaign
        {
            ProjectId = projectId,
            Name = "Historical campaign",
            SegmentId = Guid.NewGuid(),
            MessageTemplateA = "Template",
            MessageTemplateB = "Template B",
            Status = CampaignStatus.Completed,
            SentCount = 12,
            DeliveredCount = 12,
            ReadCount = 8,
            ResponseCount = 4
        };
        db.Campaigns.Add(campaign);
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

        var response = Assert.IsType<OkObjectResult>(view == "list"
            ? await controller.GetCampaigns(projectId)
            : await controller.GetCampaign(campaign.Id));
        var body = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var displayedCampaign = view == "list" ? body[0] : body;

        Assert.Equal(campaign.Id, displayedCampaign.GetProperty("id").GetGuid());
        Assert.Equal("Historical campaign", displayedCampaign.GetProperty("name").GetString());
        Assert.Equal(12, displayedCampaign.GetProperty("sentCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, displayedCampaign.GetProperty("deliveredCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, displayedCampaign.GetProperty("readCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, displayedCampaign.GetProperty("responseCount").ValueKind);
        Assert.Equal(12, campaign.DeliveredCount);
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
