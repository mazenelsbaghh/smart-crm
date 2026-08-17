using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Jobs;

public sealed class QuranFacebookScheduler : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuranFacebookScheduler> _logger;

    public QuranFacebookScheduler(IServiceProvider serviceProvider, ILogger<QuranFacebookScheduler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        RecurringJob.AddOrUpdate<QuranFacebookScheduler>(
            "quran-facebook-publication",
            scheduler => scheduler.PublishDuePagesAsync(),
            Cron.Minutely);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task PublishDuePagesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var projectIds = await dbContext.QuranFacebookSettings.IgnoreQueryFilters()
            .Where(settings => settings.IsEnabled
                && settings.FacebookPageId != null
                && (settings.NextPublishAtUtc == null || settings.NextPublishAtUtc <= now))
            .Select(settings => settings.ProjectId)
            .ToListAsync();
        foreach (var projectId in projectIds) await PublishProjectAsync(projectId, null, PublicationTrigger.Scheduled);
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
        var settings = await dbContext.QuranFacebookSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        try
        {
            var publisher = scope.ServiceProvider.GetRequiredService<QuranFacebookPublisher>();
            var publication = await publisher.PublishAsync(settings, selection, CancellationToken.None);
            MarkPublished(settings, publication.VideoId, trigger);
            _logger.LogInformation("Published Quran Facebook Reel {ReelId} for project {ProjectId}",
                publication.VideoId, projectId);
        }
        catch (Exception exception)
        {
            MarkFailed(settings, exception, trigger);
            _logger.LogError(exception, "Quran Facebook Reel publication failed for project {ProjectId}", projectId);
        }
        await dbContext.SaveChangesAsync();
    }

    private static void MarkPublished(
        QuranFacebookSettings settings,
        string reelId,
        PublicationTrigger trigger)
    {
        var now = DateTime.UtcNow;
        settings.LastReelId = reelId;
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
        QuranFacebookSettings settings,
        Exception exception,
        PublicationTrigger trigger)
    {
        settings.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
        settings.UpdatedAt = DateTime.UtcNow;
        if (trigger == PublicationTrigger.Scheduled) settings.NextPublishAtUtc = DateTime.UtcNow.AddMinutes(15);
    }

    private enum PublicationTrigger { Scheduled, Manual }
}
