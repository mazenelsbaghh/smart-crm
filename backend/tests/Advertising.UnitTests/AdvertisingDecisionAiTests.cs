using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDecisionAiTests
{
    [Fact]
    public async Task Review_is_queued_without_crossing_the_ai_credential_boundary()
    {
        await using var context = Context();
        var projectId = Guid.NewGuid();

        var review = await new AdvertisingDecisionAi(context)
            .ReviewCanaryAsync(projectId, "{\"tracking\":\"healthy\"}", CancellationToken.None);

        Assert.Equal(DecisionVerdict.Wait, review.StrategistVerdict);
        Assert.Equal("AI_STRATEGIST_PENDING", review.Reason);
        var work = Assert.Single(await context.AdvertisingAiWorkItems.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(projectId, work.ProjectId);
        var outbox = Assert.Single(await context.IntegrationOutboxMessages.ToListAsync());
        Assert.DoesNotContain("apiKey", outbox.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", outbox.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repeated_identical_review_reuses_the_pending_work_item()
    {
        await using var context = Context();
        var service = new AdvertisingDecisionAi(context);
        var projectId = Guid.NewGuid();

        await service.ReviewActionAsync(projectId, "PauseAd", "{}", CancellationToken.None);
        await service.ReviewActionAsync(projectId, "PauseAd", "{}", CancellationToken.None);

        Assert.Equal(1, await context.AdvertisingAiWorkItems.IgnoreQueryFilters().CountAsync());
    }

    private static AppDbContext Context()
    {
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options, tenant, new ServiceCollection().BuildServiceProvider());
    }
}
