using Modules.QuranChallenge.Domain;

namespace Modules.QuranChallenge.Services;

public sealed class QuranVideoPublisher
{
    private readonly QuranVideoGenerator _videoGenerator;
    private readonly YouTubePublishingClient _publishingClient;

    public QuranVideoPublisher(
        QuranVideoGenerator videoGenerator,
        YouTubePublishingClient publishingClient)
    {
        _videoGenerator = videoGenerator;
        _publishingClient = publishingClient;
    }

    public async Task<QuranPublicationResult> PublishAsync(
        QuranYouTubeSettings settings,
        QuranVerseSelection? selection,
        CancellationToken cancellationToken)
    {
        var video = await _videoGenerator.GenerateAsync(selection, cancellationToken);
        var upload = VideoUpload(settings, video);
        var videoId = await _publishingClient.UploadAsync(settings, upload, cancellationToken);
        return new QuranPublicationResult(videoId, video.SurahNumber, video.AyahNumber, video.SurahName);
    }

    private static YouTubeVideoUpload VideoUpload(QuranYouTubeSettings settings, QuranGeneratedVideo video)
    {
        return new YouTubeVideoUpload(video.Title[..Math.Min(video.Title.Length, 100)],
            video.Caption(settings.CaptionTemplate), settings.PrivacyStatus,
            ["أكمل الآية", "القرآن الكريم", "ياسر الدوسري", "Shorts"], video.VideoBytes);
    }
}

public sealed record QuranVerseSelection(int SurahNumber, int AyahNumber, int HiddenWordIndex);
public sealed record QuranPublicationResult(string VideoId, int SurahNumber, int AyahNumber, string SurahName);
