using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Content.Domain;
using Modules.Content.Jobs;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContentVideoRecoveryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Stale_planning_recovery_executes_on_postgresql_and_claims_work()
    {
        // Regression 2026-08-30: PostgreSQL could not translate ordering after the StaleVideo projection.
        await using var context = postgres.CreateContext();
        await context.Database.MigrateAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var projectId = Guid.NewGuid();
        var video = new ContentVideo
        {
            ProjectId = projectId,
            Status = ContentVideoStatus.Planning
        };
        context.ContentVideos.Add(video);
        await context.SaveChangesAsync();
        var staleUpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await context.ContentVideos.IgnoreQueryFilters()
            .Where(candidate => candidate.Id == video.Id && candidate.ProjectId == projectId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.UpdatedAt, staleUpdatedAt));
        context.ChangeTracker.Clear();

        var backgroundJobs = new AcceptingBackgroundJobClient();
        var dispatch = new ContentVideoDispatchService(
            backgroundJobs,
            NullLogger<ContentVideoDispatchService>.Instance);
        var job = new ContentVideoJob(
            context,
            planningService: null!,
            omniClient: null!,
            mediaService: null!,
            objectStorage: null!,
            secretVault: null!,
            dispatch,
            NullLogger<ContentVideoJob>.Instance);

        await job.RecoverAsync();

        context.ChangeTracker.Clear();
        var recovered = await context.ContentVideos.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == video.Id && candidate.ProjectId == projectId);
        Assert.True(recovered.UpdatedAt > staleUpdatedAt);
    }

    private sealed class AcceptingBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => "content-video-recovery";

        public bool ChangeState(string jobId, IState state, string expectedState) =>
            throw new NotSupportedException("Recovery dispatch should only create a job.");
    }
}
