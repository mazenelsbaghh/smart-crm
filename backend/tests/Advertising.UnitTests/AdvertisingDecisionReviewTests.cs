using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDecisionReviewTests
{
    [Fact]
    public async Task Strategist_then_auditor_then_required_judge_run_sequentially_and_cannot_be_bypassed()
    {
        await using var db = Context();
        var projectId = Guid.NewGuid();
        var service = new AdvertisingDecisionAi(db);

        var first = await service.ReviewActionAsync(projectId, "IncreaseBudget", "{\"valueProducing\":true}");
        Assert.Equal(DecisionVerdict.Wait, first.StrategistVerdict);
        Assert.Equal("Strategist", Assert.Single(await Work(db)).Purpose);

        await CompleteCurrent(db, "Strategist", "APPROVE", "ADS_STRATEGY_SUPPORTED");
        var second = await service.ReviewActionAsync(projectId, "IncreaseBudget", "{\"valueProducing\":true}");
        Assert.Equal(DecisionVerdict.Approve, second.StrategistVerdict);
        Assert.Equal(DecisionVerdict.Wait, second.AuditorVerdict);
        Assert.Equal(2, (await Work(db)).Count);

        await CompleteCurrent(db, "Auditor", "APPROVE", "ADS_AUDIT_CLEAR");
        var third = await service.ReviewActionAsync(projectId, "IncreaseBudget", "{\"valueProducing\":true}");
        Assert.Equal(DecisionVerdict.Wait, third.JudgeVerdict);
        Assert.Equal(3, (await Work(db)).Count);

        await CompleteCurrent(db, "Judge", "REJECT", "ADS_VALUE_RISK");
        var final = await service.ReviewActionAsync(projectId, "IncreaseBudget", "{\"valueProducing\":true}");
        Assert.Equal(DecisionVerdict.Reject, final.JudgeVerdict);
        Assert.Equal("ADS_VALUE_RISK", final.Reason);
    }

    private static async Task<List<AdvertisingAiWorkItem>> Work(AppDbContext db) =>
        await db.AdvertisingAiWorkItems.IgnoreQueryFilters().OrderBy(item => item.CreatedAt).ToListAsync();

    private static async Task CompleteCurrent(AppDbContext db, string purpose, string verdict, string reason)
    {
        var work = await db.AdvertisingAiWorkItems.IgnoreQueryFilters().SingleAsync(item => item.Purpose == purpose);
        work.State = AiWorkState.Completed;
        work.ResultJson = $"{{\"verdict\":\"{verdict}\",\"reasons\":[\"{reason}\"]}}";
        work.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new TenantContext(), new ServiceCollection().BuildServiceProvider());
}
