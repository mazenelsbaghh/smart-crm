using System.Collections.Concurrent;
using System.Net;
using Amazon.S3;
using Modules.Content.Domain;
using Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Modules.Content.Services;

public sealed class ContentImagePreviewService
{
    private const int PreviewMaxPixels = 1080;
    private const int PreviewJpegQuality = 84;

    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<ContentImagePreviewService> _logger;
    private readonly SemaphoreSlim _previewSlots = new(2);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _postLocks = new();

    public ContentImagePreviewService(
        IObjectStorage objectStorage,
        ILogger<ContentImagePreviewService> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<Stream> GetOrCreateAsync(ContentPost post, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(post.ImageObjectKey))
            throw new InvalidOperationException("صورة المحتوى غير موجودة.");

        var postLock = _postLocks.GetOrAdd(post.Id, _ => new SemaphoreSlim(1, 1));
        await postLock.WaitAsync(cancellationToken);
        try
        {
            var cached = await TryDownloadPreviewAsync(post, cancellationToken);
            if (cached is not null) return cached;

            await _previewSlots.WaitAsync(cancellationToken);
            try
            {
                await using var original = await _objectStorage.DownloadAsync(post.ImageObjectKey, cancellationToken);
                using var image = await Image.LoadAsync(original, cancellationToken);
                image.Mutate(context => context.AutoOrient());
                if (image.Width > PreviewMaxPixels || image.Height > PreviewMaxPixels)
                {
                    image.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Size = new Size(PreviewMaxPixels, PreviewMaxPixels),
                        Mode = ResizeMode.Max
                    }));
                }

                await using var encoded = new MemoryStream();
                await image.SaveAsJpegAsync(
                    encoded,
                    new JpegEncoder { Quality = PreviewJpegQuality },
                    cancellationToken);
                var bytes = encoded.ToArray();
                try
                {
                    await using var upload = new MemoryStream(bytes, writable: false);
                    await _objectStorage.UploadAsync(
                        PreviewObjectKey(post),
                        upload,
                        "image/jpeg",
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(
                        exception,
                        "Content preview cache upload failed for post {PostId}",
                        post.Id);
                }

                return new MemoryStream(bytes, writable: false);
            }
            finally
            {
                _previewSlots.Release();
            }
        }
        finally
        {
            postLock.Release();
        }
    }

    private async Task<Stream?> TryDownloadPreviewAsync(
        ContentPost post,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _objectStorage.DownloadAsync(PreviewObjectKey(post), cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound
            || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static string PreviewObjectKey(ContentPost post) =>
        $"content/{post.ProjectId:N}/previews/{post.Id:N}.jpg";
}
