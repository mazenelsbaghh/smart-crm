using Microsoft.EntityFrameworkCore;
using Modules.Content.Domain;
using Shared.Infrastructure;
using Shared.Storage;

namespace Modules.Content.Services;

public sealed class ContentPublishingService
{
    private readonly AppDbContext _dbContext;
    private readonly IObjectStorage _objectStorage;
    private readonly FacebookPhotoPublisher _publisher;
    private readonly ILogger<ContentPublishingService> _logger;

    public ContentPublishingService(
        AppDbContext dbContext,
        IObjectStorage objectStorage,
        FacebookPhotoPublisher publisher,
        ILogger<ContentPublishingService> logger)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ContentPost> PublishAsync(
        Guid projectId,
        Guid postId,
        CancellationToken cancellationToken)
    {
        var post = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId && candidate.Id == postId, cancellationToken)
            ?? throw new InvalidOperationException("المنشور غير موجود.");
        if (post.Status == ContentPostStatus.Published) return post;
        if (post.Status is not (ContentPostStatus.Approved or ContentPostStatus.PublishFailed))
            throw new InvalidOperationException("المنشور غير جاهز للنشر.");
        if (string.IsNullOrWhiteSpace(post.ImageObjectKey))
            throw new InvalidOperationException("صورة المنشور غير موجودة.");

        var settings = await _dbContext.ContentAutomationSettings.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.ProjectId == projectId, cancellationToken);
        if (!string.Equals(post.BrandLogoObjectKey, settings.LogoObjectKey, StringComparison.Ordinal))
            throw new InvalidOperationException("تم تغيير اللوجو بعد إنشاء هذا المنشور. أنشئ منشورًا جديدًا بالهوية الحالية.");
        if (!string.Equals(post.BrandStylePrompt, settings.StylePrompt, StringComparison.Ordinal))
            throw new InvalidOperationException("تم تغيير شكل التصميم بعد إنشاء هذا المنشور. أنشئ منشورًا جديدًا أولاً.");
        if (string.IsNullOrWhiteSpace(settings.FacebookPageId))
            throw new InvalidOperationException("اختر صفحة Facebook للنشر.");
        var page = await _dbContext.ConnectedPages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.ProjectId == projectId
                && candidate.FacebookPageId == settings.FacebookPageId
                && candidate.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("صفحة Facebook غير متصلة أو غير نشطة.");

        var formattedCaption = ContentGenerationService.NormalizeCaptionTone(post.Caption);
        await using var image = await _objectStorage.DownloadAsync(post.ImageObjectKey, cancellationToken);

        var publishingStartedAtUtc = DateTime.UtcNow;
        var claimed = await _dbContext.ContentPosts.IgnoreQueryFilters()
            .Where(candidate => candidate.ProjectId == projectId
                && candidate.Id == postId
                && (candidate.Status == ContentPostStatus.Approved
                    || candidate.Status == ContentPostStatus.PublishFailed))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(candidate => candidate.Status, ContentPostStatus.Publishing)
                .SetProperty(candidate => candidate.Error, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, publishingStartedAtUtc),
                cancellationToken);
        if (claimed != 1)
            throw new InvalidOperationException("بدأ طلب آخر نشر هذا المنشور بالفعل.");

        post.Status = ContentPostStatus.Publishing;
        post.Caption = formattedCaption;
        post.Error = null;
        post.UpdatedAt = publishingStartedAtUtc;

        try
        {
            var publication = new FacebookPhotoPublication(
                page.FacebookPageId,
                page.PageAccessToken,
                post.Caption,
                image,
                post.ImageMimeType);
            var facebookPhoto = await _publisher.PublishAsync(publication, cancellationToken);
            post.FacebookPostId = facebookPhoto.PostId;
            post.PublishedAtUtc = DateTime.UtcNow;
            post.Status = ContentPostStatus.Published;
            post.UpdatedAt = DateTime.UtcNow;
            settings.LastPublishedAtUtc = post.PublishedAtUtc;
            settings.LastError = null;
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return post;
        }
        catch (FacebookPublishException exception)
        {
            post.Status = exception.OutcomeUnknown
                ? ContentPostStatus.PublishUnknown
                : ContentPostStatus.PublishFailed;
            post.Error = Truncate(exception.Message);
            settings.LastError = post.Error;
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogError(exception, "Facebook content publish failed for project {ProjectId}, post {PostId}", projectId, postId);
            throw;
        }
        catch (Exception exception)
        {
            post.Status = ContentPostStatus.PublishUnknown;
            post.Error = Truncate(exception.Message);
            settings.LastError = post.Error;
            settings.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogError(exception, "Facebook content publish outcome is unknown for project {ProjectId}, post {PostId}", projectId, postId);
            throw;
        }
    }

    private static string Truncate(string message) => message[..Math.Min(message.Length, 1000)];
}
