using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Storage;
using Shared.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Modules.Advertising.Jobs;

public sealed class CreativeVariantJob(AppDbContext db, IObjectStorage storage, ILogger<CreativeVariantJob> logger)
{
    private static readonly (string Placement, int Width, int Height)[] Targets =
    [
        ("feed", 1080, 1080), ("story", 1080, 1920), ("facebook_reels", 1080, 1920)
    ];

    public async Task GenerateAsync(Guid projectId, Guid creativeId, CancellationToken cancellationToken)
    {
        var creative = await db.AdvertisingCreatives.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == creativeId, cancellationToken);
        if (creative?.EligibilityState != CreativeEligibility.Eligible || string.IsNullOrWhiteSpace(creative.SourceStoragePath)) return;
        foreach (var target in Targets)
        {
            if (await db.AdvertisingCreativeVariants.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.CreativeId == creativeId && x.Placement == target.Placement && x.SourceHash == creative.SourceHash, cancellationToken)) continue;
            await using var source = await storage.DownloadAsync(creative.SourceStoragePath, cancellationToken);
            var outputKey = $"projects/{projectId:N}/advertising/{creativeId:N}/{creative.SourceHash[..Math.Min(12, creative.SourceHash.Length)]}-{target.Placement}.{(creative.MediaType == CreativeMediaType.Video ? "mp4" : "jpg")}";
            if (creative.MediaType == CreativeMediaType.Video)
                await GenerateVideoAsync(source, outputKey, target.Width, target.Height, cancellationToken);
            else
                await GenerateImageAsync(source, outputKey, target.Width, target.Height, cancellationToken);
            db.AdvertisingCreativeVariants.Add(new AdvertisingCreativeVariant
            {
                ProjectId = projectId, CreativeId = creativeId, Placement = target.Placement,
                ContentType = creative.MediaType == CreativeMediaType.Video ? "video/mp4" : "image/jpeg",
                StoragePath = outputKey, Width = target.Width, Height = target.Height, SourceHash = creative.SourceHash
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task GenerateImageAsync(Stream source, string outputKey, int width, int height, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(source, cancellationToken);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center }));
        await using var output = new MemoryStream();
        await image.SaveAsync(output, new JpegEncoder { Quality = 86 }, cancellationToken);
        output.Position = 0;
        await storage.UploadAsync(outputKey, output, "image/jpeg", cancellationToken);
    }

    private async Task GenerateVideoAsync(Stream source, string outputKey, int width, int height, CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ads-variant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var input = Path.Combine(tempRoot, "input"); var output = Path.Combine(tempRoot, "output.mp4");
        try
        {
            await using (var file = File.Create(input)) await source.CopyToAsync(file, cancellationToken);
            var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-y", "-i", input, "-vf", $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}", "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-c:a", "aac", "-movflags", "+faststart", output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg could not start.");
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg failed ({process.ExitCode}).");
            await using var result = File.OpenRead(output);
            await storage.UploadAsync(outputKey, result, "video/mp4", cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Creative video variant failed for {OutputKey}: {ErrorCode}", outputKey, ex.GetType().Name);
            throw;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); }
            catch (IOException ex) { logger.LogDebug("Temporary creative directory cleanup failed: {ErrorCode}", ex.GetType().Name); }
            catch (UnauthorizedAccessException ex) { logger.LogDebug("Temporary creative directory cleanup was denied: {ErrorCode}", ex.GetType().Name); }
        }
    }
}
