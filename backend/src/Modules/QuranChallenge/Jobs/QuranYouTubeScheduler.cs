using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Jobs;

public sealed class QuranYouTubeScheduler : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuranYouTubeScheduler> _logger;

    public QuranYouTubeScheduler(IServiceProvider serviceProvider, ILogger<QuranYouTubeScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RecurringJob.AddOrUpdate<QuranYouTubeScheduler>(
            "quran-youtube-publication",
            scheduler => scheduler.PublishDueChannelsAsync(),
            Cron.Minutely);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task PublishDueChannelsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var projectIds = await dbContext.QuranYouTubeSettings.IgnoreQueryFilters()
            .Where(settings => settings.IsEnabled
                && settings.ProtectedRefreshToken != null
                && (settings.NextPublishAtUtc == null || settings.NextPublishAtUtc <= now))
            .Select(settings => settings.ProjectId)
            .ToListAsync();
        foreach (var projectId in projectIds) await PublishScheduledProjectAsync(projectId);
    }

    public async Task PublishScheduledProjectAsync(Guid projectId)
    {
        await PublishProjectAsync(projectId, null, PublicationTrigger.Scheduled);
    }

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task PublishCurrentVerseAsync(Guid projectId, QuranVerseSelection selection)
    {
        await PublishProjectAsync(projectId, selection, PublicationTrigger.Manual);
    }

    private async Task PublishProjectAsync(Guid projectId, QuranVerseSelection? selection, PublicationTrigger trigger)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await dbContext.QuranYouTubeSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        try
        {
            var publisher = scope.ServiceProvider.GetRequiredService<QuranVideoPublisher>();
            var publication = await publisher.PublishAsync(settings, selection, CancellationToken.None);
            MarkPublished(settings, publication.VideoId, trigger);
            _logger.LogInformation("Published Quran video {VideoId} for project {ProjectId}", publication.VideoId, projectId);
        }
        catch (YouTubeReauthenticationRequiredException exception)
        {
            MarkConnectionExpired(settings, exception);
            _logger.LogWarning(exception, "YouTube connection expired for project {ProjectId}", projectId);
        }
        catch (Exception exception)
        {
            MarkFailed(settings, exception, trigger);
            _logger.LogError(exception, "Quran video publication failed for project {ProjectId}", projectId);
        }
        await dbContext.SaveChangesAsync();
    }

    private static void MarkPublished(QuranYouTubeSettings settings, string videoId, PublicationTrigger trigger)
    {
        var now = DateTime.UtcNow;
        settings.LastVideoId = videoId;
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

    private static void MarkFailed(QuranYouTubeSettings settings, Exception exception, PublicationTrigger trigger)
    {
        settings.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
        settings.UpdatedAt = DateTime.UtcNow;
        if (trigger == PublicationTrigger.Scheduled) settings.NextPublishAtUtc = DateTime.UtcNow.AddMinutes(15);
    }

    private static void MarkConnectionExpired(QuranYouTubeSettings settings, Exception exception)
    {
        settings.ProtectedRefreshToken = null;
        settings.ChannelId = null;
        settings.ChannelTitle = null;
        settings.IsEnabled = false;
        settings.NextPublishAtUtc = null;
        settings.LastError = exception.Message;
        settings.UpdatedAt = DateTime.UtcNow;
    }

    private enum PublicationTrigger { Scheduled, Manual }
}
