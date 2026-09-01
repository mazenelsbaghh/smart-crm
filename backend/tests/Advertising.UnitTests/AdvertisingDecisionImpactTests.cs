using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDecisionImpactTests
{
    [Fact]
    public async Task Impact_waits_until_due_and_sparse_or_correcting_evidence_is_inconclusive()
    {
        var setup = await SetupAsync(DateTime.UtcNow.AddHours(1));
        var evidence = Evidence(100, 60, 2, 10, pendingCorrection: true);

        Assert.Null(await setup.Service.EvaluateAsync(setup.ProjectId, setup.Decision.Id, evidence));
        setup.Decision.EvaluateAfterUtc = DateTime.UtcNow.AddMinutes(-1); await setup.Db.SaveChangesAsync();
        var result = await setup.Service.EvaluateAsync(setup.ProjectId, setup.Decision.Id, evidence);

        Assert.Equal(DecisionImpactLabel.Inconclusive, result!.Label);
        Assert.Null(result.RollbackCommandId);
    }

    [Fact]
    public async Task Negative_mature_impact_queues_one_explicit_rollback_and_correction_marks_reverted()
    {
        var setup = await SetupAsync(DateTime.UtcNow.AddMinutes(-1));
        var negative = await setup.Service.EvaluateAsync(setup.ProjectId, setup.Decision.Id,
            Evidence(100, 70, 10, 10, rollback: $"{{\"adId\":\"{setup.AdId}\",\"status\":\"PAUSED\"}}"));
        var corrected = await setup.Service.EvaluateAsync(setup.ProjectId, setup.Decision.Id,
            Evidence(100, 110, 10, 10));

        Assert.Equal(DecisionImpactLabel.Negative, negative!.Label);
        Assert.NotNull(negative.RollbackCommandId);
        Assert.Equal(DecisionImpactLabel.Reverted, corrected!.Label);
        Assert.Single(await setup.Db.AdvertisingExecutionCommands.IgnoreQueryFilters()
            .Where(item => item.CommandType == "Rollback").ToListAsync());
    }

    private static DecisionImpactEvidence Evidence(decimal baseline, decimal evaluation, int baselineSample,
        int evaluationSample, bool pendingCorrection = false, string? rollback = null)
    {
        var now = DateTime.UtcNow;
        return new(baseline, evaluation, baselineSample, evaluationSample, now.AddDays(-14), now.AddDays(-7),
            now.AddDays(-7), now, "NetPaidValue", pendingCorrection, rollback);
    }

    private static async Task<SetupState> SetupAsync(DateTime evaluateAfter)
    {
        var projectId = Guid.NewGuid(); var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant,
            new ServiceCollection().BuildServiceProvider());
        var adId = Guid.NewGuid();
        var decision = new AdvertisingDecision { ProjectId = projectId, ActionType = "IncreaseBudget",
            TargetType = "Ad", TargetId = adId, EvidenceStartUtc = DateTime.UtcNow.AddDays(-14),
            EvidenceEndUtc = DateTime.UtcNow.AddDays(-7), State = DecisionState.Executed, EvaluateAfterUtc = evaluateAfter };
        var executed = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id,
            IdempotencyKey = Guid.NewGuid().ToString("N"), CommandType = "IncreaseBudget", TargetExternalId = "ad-1",
            DesiredStateJson = $"{{\"adId\":\"{adId}\",\"dailyBudget\":120}}", RequestFingerprint = "applied",
            State = CommandState.Succeeded, CompletedAtUtc = DateTime.UtcNow.AddHours(-2) };
        db.AddRange(decision, executed); await db.SaveChangesAsync();
        return new(projectId, adId, decision, db, new AdvertisingDecisionImpactService(db));
    }

    private sealed record SetupState(Guid ProjectId, Guid AdId, AdvertisingDecision Decision,
        AppDbContext Db, AdvertisingDecisionImpactService Service);
}
