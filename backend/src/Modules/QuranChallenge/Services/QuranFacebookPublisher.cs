using Microsoft.EntityFrameworkCore;
using Modules.QuranChallenge.Domain;
using Shared.Infrastructure;

namespace Modules.QuranChallenge.Services;

public sealed class QuranFacebookPublisher
{
    private readonly AppDbContext _dbContext;
    private readonly QuranVideoGenerator _videoGenerator;
    private readonly FacebookReelsUploadClient _uploadClient;

    public QuranFacebookPublisher(
        AppDbContext dbContext,
        QuranVideoGenerator videoGenerator,
        FacebookReelsUploadClient uploadClient)
    {
        _dbContext = dbContext;
        _videoGenerator = videoGenerator;
        _uploadClient = uploadClient;
    }

    public async Task<QuranPublicationResult> PublishAsync(
        QuranFacebookSettings settings,
        QuranVerseSelection? selection,
        CancellationToken cancellationToken)
    {
        var page = await ConnectedPageAsync(settings, cancellationToken);
        var video = await _videoGenerator.GenerateAsync(selection, cancellationToken);
        var upload = new FacebookReelUpload(video.Title, video.Caption(settings.CaptionTemplate), video.VideoBytes);
        var reelId = await _uploadClient.UploadAsync(upload, page.PageAccessToken, cancellationToken);
        return new QuranPublicationResult(reelId, video.SurahNumber, video.AyahNumber, video.SurahName);
    }

    private async Task<Modules.Facebook.Domain.ConnectedPage> ConnectedPageAsync(
        QuranFacebookSettings settings,
        CancellationToken cancellationToken)
    {
        var pageId = settings.FacebookPageId
            ?? throw new InvalidOperationException("لم يتم اختيار صفحة Facebook للنشر.");
        return await _dbContext.ConnectedPages.IgnoreQueryFilters()
            .SingleOrDefaultAsync(page => page.ProjectId == settings.ProjectId
                && page.FacebookPageId == pageId
                && page.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("صفحة Facebook غير متصلة أو انتهت صلاحيتها.");
    }
}
