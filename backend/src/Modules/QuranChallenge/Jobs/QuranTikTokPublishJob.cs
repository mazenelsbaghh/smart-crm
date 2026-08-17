using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Services;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Jobs;

public sealed class QuranTikTokPublishJob
{
    private readonly AppDbContext _dbContext;
    private readonly QuranTikTokPublisher _publisher;
    private readonly ILogger<QuranTikTokPublishJob> _logger;

    public QuranTikTokPublishJob(
        AppDbContext dbContext,
        QuranTikTokPublisher publisher,
        ILogger<QuranTikTokPublishJob> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    public async Task PublishAsync(
        Guid projectId,
        QuranVerseSelection selection,
        TikTokPostRequest post)
    {
        var settings = await _dbContext.QuranTikTokSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        try
        {
            settings.LastPublishStatus = "GENERATING";
            settings.LastError = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var publishId = await _publisher.PublishAsync(
                settings,
                selection,
                post,
                CancellationToken.None);
            settings.LastPublishId = publishId;
            settings.LastPublishStatus = "PROCESSING";
            settings.LastPublishedAtUtc = DateTime.UtcNow;
            settings.LastError = null;
            settings.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "Submitted Quran TikTok post {PublishId} for project {ProjectId}",
                publishId,
                projectId);
        }
        catch (Exception exception)
        {
            settings.LastPublishStatus = "FAILED";
            settings.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
            settings.UpdatedAt = DateTime.UtcNow;
            _logger.LogError(exception, "Quran TikTok publication failed for project {ProjectId}", projectId);
        }
        await _dbContext.SaveChangesAsync();
    }
}
