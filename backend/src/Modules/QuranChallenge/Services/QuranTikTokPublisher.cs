using Modules.QuranChallenge.Domain;

namespace Modules.QuranChallenge.Services;

public sealed class QuranTikTokPublisher
{
    private readonly TikTokApiClient _apiClient;
    private readonly TikTokConnectionService _connectionService;
    private readonly QuranVideoGenerator _videoGenerator;

    public QuranTikTokPublisher(
        TikTokApiClient apiClient,
        TikTokConnectionService connectionService,
        QuranVideoGenerator videoGenerator)
    {
        _apiClient = apiClient;
        _connectionService = connectionService;
        _videoGenerator = videoGenerator;
    }

    public async Task<string> PublishAsync(
        QuranTikTokSettings settings,
        QuranVerseSelection selection,
        TikTokPostRequest post,
        CancellationToken cancellationToken)
    {
        var accessToken = await _connectionService.AccessTokenAsync(settings, cancellationToken);
        var creator = await _apiClient.CreatorInfoAsync(accessToken, cancellationToken);
        ValidateCreatorOptions(creator, post);
        var video = await _videoGenerator.GenerateAsync(selection, cancellationToken);
        if (video.DurationSeconds > creator.MaxVideoPostDurationSeconds)
        {
            throw new InvalidOperationException(
                $"مدة الفيديو {Math.Ceiling(video.DurationSeconds)} ثانية، بينما حساب TikTok يسمح بحد أقصى {creator.MaxVideoPostDurationSeconds} ثانية.");
        }
        var initialization = await _apiClient.InitializePostAsync(
            accessToken,
            post,
            video.VideoBytes.LongLength,
            cancellationToken);
        await _apiClient.UploadAsync(
            initialization.UploadUrl,
            video.VideoBytes,
            cancellationToken);
        return initialization.PublishId;
    }

    private static void ValidateCreatorOptions(TikTokCreatorInfo creator, TikTokPostRequest post)
    {
        if (!creator.PrivacyLevelOptions.Contains(post.PrivacyLevel))
        {
            throw new InvalidOperationException("مستوى الخصوصية المختار غير متاح لهذا الحساب.");
        }
        if (post.AllowComment && creator.CommentDisabled)
        {
            throw new InvalidOperationException("التعليقات معطلة من إعدادات حساب TikTok.");
        }
        if (post.AllowDuet && creator.DuetDisabled)
        {
            throw new InvalidOperationException("Duet معطل من إعدادات حساب TikTok.");
        }
        if (post.AllowStitch && creator.StitchDisabled)
        {
            throw new InvalidOperationException("Stitch معطل من إعدادات حساب TikTok.");
        }
    }
}
