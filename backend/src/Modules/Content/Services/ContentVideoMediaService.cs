using System.Diagnostics;
using Modules.Content.Domain;
using Shared.Storage;

namespace Modules.Content.Services;

public sealed class ContentVideoMediaService(
    IObjectStorage objectStorage,
    ILogger<ContentVideoMediaService> logger)
{
    public async Task<byte[]> ExtractLastFrameAsync(
        ContentVideoScene scene,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scene.VideoObjectKey))
            throw new ContentVideoException("PREVIOUS_SCENE_MISSING", "المشهد السابق غير مكتمل.");

        var tempRoot = CreateTempDirectory("content-video-frame");
        var inputPath = Path.Combine(tempRoot, "input.mp4");
        var outputPath = Path.Combine(tempRoot, "last-frame.png");
        try
        {
            await using (var source = await objectStorage.DownloadAsync(scene.VideoObjectKey, cancellationToken))
            await using (var input = File.Create(inputPath))
            {
                await source.CopyToAsync(input, cancellationToken);
            }

            await RunFfmpegAsync(
                ["-y", "-sseof", "-0.1", "-i", inputPath, "-frames:v", "1", outputPath],
                cancellationToken);
            return await File.ReadAllBytesAsync(outputPath, cancellationToken);
        }
        finally
        {
            Cleanup(tempRoot);
        }
    }

    public async Task<string> AssembleAndStoreAsync(
        Guid projectId,
        Guid videoId,
        IReadOnlyList<ContentVideoScene> scenes,
        CancellationToken cancellationToken)
    {
        if (scenes.Count is < ContentVideoCapabilities.MinimumSceneCount
            or > ContentVideoCapabilities.MaximumSceneCount
            || scenes.Any(scene => scene.Status != ContentVideoSceneStatus.Completed
                || string.IsNullOrWhiteSpace(scene.VideoObjectKey)))
        {
            throw new ContentVideoException(
                "SCENES_NOT_READY_FOR_ASSEMBLY",
                "كل المشاهد يجب أن تكتمل قبل تجميع الفيديو.");
        }

        var tempRoot = CreateTempDirectory("content-video-assembly");
        var concatPath = Path.Combine(tempRoot, "scenes.ffconcat");
        var outputPath = Path.Combine(tempRoot, "final.mp4");
        try
        {
            var scenePaths = new List<string>(scenes.Count);
            foreach (var scene in scenes.OrderBy(scene => scene.SceneIndex))
            {
                var scenePath = Path.Combine(tempRoot, $"scene-{scene.SceneIndex:D2}.mp4");
                await using var source = await objectStorage.DownloadAsync(
                    scene.VideoObjectKey!,
                    cancellationToken);
                await using var destination = File.Create(scenePath);
                await source.CopyToAsync(destination, cancellationToken);
                scenePaths.Add(scenePath);
            }

            var concatLines = new[] { "ffconcat version 1.0" }
                .Concat(scenePaths.Select(path => $"file '{EscapeConcatPath(path)}'"));
            await File.WriteAllLinesAsync(concatPath, concatLines, cancellationToken);
            await RunFfmpegAsync(
                [
                    "-y", "-f", "concat", "-safe", "0", "-i", concatPath,
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                    "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", outputPath
                ],
                cancellationToken);

            var objectKey = $"content/{projectId:N}/videos/{videoId:N}/final.mp4";
            await using var output = File.OpenRead(outputPath);
            await objectStorage.UploadAsync(objectKey, output, "video/mp4", cancellationToken);
            return objectKey;
        }
        finally
        {
            Cleanup(tempRoot);
        }
    }

    private static async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new ContentVideoException("FFMPEG_NOT_AVAILABLE", "تعذر تشغيل أداة معالجة الفيديو.");
        try
        {
            var errorDrain = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await errorDrain;
            if (process.ExitCode != 0)
                throw new ContentVideoException("FFMPEG_FAILED", "تعذر معالجة ملفات الفيديو المولدة.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string EscapeConcatPath(string path) => path.Replace("'", "'\\''", StringComparison.Ordinal);

    private void Cleanup(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException exception)
        {
            logger.LogDebug("Content video temporary cleanup failed: {ErrorCode}", exception.GetType().Name);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogDebug("Content video temporary cleanup was denied: {ErrorCode}", exception.GetType().Name);
        }
    }
}
