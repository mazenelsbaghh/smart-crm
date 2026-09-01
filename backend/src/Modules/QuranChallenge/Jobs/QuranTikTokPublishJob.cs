using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Jobs;

public sealed class QuranTikTokPublishJob : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuranTikTokPublishJob> _logger;

    public QuranTikTokPublishJob(IServiceProvider serviceProvider, ILogger<QuranTikTokPublishJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RecurringJob.AddOrUpdate<QuranTikTokPublishJob>(
            "quran-tiktok-publication",
            scheduler => scheduler.PublishDueAccountsAsync(),
            Cron.Minutely);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task PublishDueAccountsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var projectIds = await dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .Where(settings => settings.IsEnabled
                && (settings.NextPublishAtUtc == null || settings.NextPublishAtUtc <= now))
            .Select(settings => settings.ProjectId)
            .ToListAsync();
        foreach (var projectId in projectIds)
        {
            await PublishProjectAsync(projectId, null, PublicationTrigger.Scheduled);
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public Task PublishCurrentVerseAsync(Guid projectId, QuranVerseSelection selection)
    {
        return PublishProjectAsync(projectId, selection, PublicationTrigger.Manual);
    }

    private async Task PublishProjectAsync(
        Guid projectId,
        QuranVerseSelection? selection,
        PublicationTrigger trigger)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        try
        {
            settings.LastPublishStatus = "GENERATING";
            settings.LastError = null;
            await dbContext.SaveChangesAsync();
            var publisher = scope.ServiceProvider.GetRequiredService<QuranTikTokPublisher>();
            var postId = await publisher.PublishAsync(settings, selection, CancellationToken.None);
            MarkPublished(settings, postId, trigger);
            _logger.LogInformation("Submitted Quran TikTok post {PostId} for project {ProjectId}", postId, projectId);
        }
        catch (Exception exception)
        {
            MarkFailed(settings, exception);
            _logger.LogError(exception, "Quran TikTok publication failed for project {ProjectId}", projectId);
        }
        await dbContext.SaveChangesAsync();
    }

    private static void MarkPublished(
        QuranTikTokSettings settings,
        string postId,
        PublicationTrigger trigger)
    {
        var now = DateTime.UtcNow;
        settings.LastPublishId = postId;
        settings.LastPublishStatus = "PROCESSING";
        settings.LastPublishedAtUtc = now;
        settings.LastError = null;
        settings.UpdatedAt = now;
        if (trigger == PublicationTrigger.Scheduled)
        {
            settings.NextPublishAtUtc = PublicationSchedule.NextSlot(
                settings.NextPublishAtUtc,
                settings.IntervalHours,
                now);
        }
    }

    private static void MarkFailed(
        QuranTikTokSettings settings,
        Exception exception)
    {
        settings.LastPublishStatus = "FAILED";
        settings.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
        settings.UpdatedAt = DateTime.UtcNow;
        settings.NextPublishAtUtc = DateTime.UtcNow.AddMinutes(10);
    }

    private enum PublicationTrigger { Scheduled, Manual }
}
