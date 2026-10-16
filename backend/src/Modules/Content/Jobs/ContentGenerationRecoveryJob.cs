using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Shared.Infrastructure;

namespace Modules.Content.Jobs;

public sealed class ContentGenerationRecoveryJob(AppDbContext dbContext, IBackgroundJobClient jobs)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RecoverAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var sampleProjects = await dbContext.ContentPosts.IgnoreQueryFilters().AsNoTracking()
            .Where(post => post.IsStyleSample && post.Status == ContentPostStatus.Generating && post.UpdatedAt <= cutoff)
            .Select(post => post.ProjectId).Distinct().Take(100).ToListAsync();
        foreach (var projectId in sampleProjects)
            jobs.Enqueue<ContentAutomationJob>(job => job.GenerateSampleAsync(projectId));

        var planProjects = await RecoverablePlanProjectsAsync(cutoff);
        foreach (var projectId in planProjects)
            jobs.Enqueue<ContentAutomationJob>(job => job.GenerateWeeklyPlanAsync(projectId));

        var games = await dbContext.ContentCardGames.IgnoreQueryFilters().AsNoTracking()
            .Where(game => game.UpdatedAt <= cutoff && (game.DesignStatus == ContentCardGameDesignStatus.Queued
                || game.DesignStatus == ContentCardGameDesignStatus.Generating || game.DesignStatus == ContentCardGameDesignStatus.Stopping))
            .OrderBy(game => game.UpdatedAt).Take(100).Select(game => new { game.ProjectId, game.Id }).ToListAsync();
        foreach (var game in games)
            jobs.Enqueue<ContentCardGameDesignJob>(job => job.GenerateAsync(game.ProjectId, game.Id, CancellationToken.None));
    }

    private Task<List<Guid>> RecoverablePlanProjectsAsync(DateTime cutoff) =>
        dbContext.ContentWeekPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(plan => plan.UpdatedAt <= cutoff && (plan.Status == ContentWeekPlanStatus.Generating
                || plan.Status == ContentWeekPlanStatus.AwaitingApproval
                    && (!dbContext.ContentWeekPlanItems.IgnoreQueryFilters().Any(planItem => planItem.PlanId == plan.Id && planItem.ProjectId == plan.ProjectId)
                        || dbContext.ContentWeekPlanItems.IgnoreQueryFilters().Any(planItem => planItem.PlanId == plan.Id
                            && planItem.ProjectId == plan.ProjectId && planItem.ContentPostId == null))))
            .Select(plan => plan.ProjectId).Distinct().Take(100).ToListAsync();
}
