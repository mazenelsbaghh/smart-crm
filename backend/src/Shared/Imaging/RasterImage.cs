using ImageMagick;

namespace Shared.Imaging;

public sealed record RasterImageInfo(int Width, int Height, string MimeType);

public sealed class RasterImage : IDisposable
{
    private readonly MagickImage image;
    private RasterImage(MagickImage image) => this.image = image;
    public int Width => checked((int)image.Width);
    public int Height => checked((int)image.Height);

    public static async Task<RasterImage> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        _ = Inspect(bytes);
        var decoded = new MagickImage();
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            await decoded.ReadAsync(input, new MagickReadSettings { Format = FormatFor(bytes), FrameCount = 1 }, cancellationToken);
            return new RasterImage(decoded);
        }
        catch (MagickException exception) { decoded.Dispose(); throw new InvalidDataException("Invalid raster image.", exception); }
        catch { decoded.Dispose(); throw; }
    }

    public static RasterImageInfo Inspect(byte[] bytes)
    {
        try
        {
            var format = FormatFor(bytes);
            var info = new MagickImageInfo(bytes, new MagickReadSettings { Format = format, FrameCount = 1 });
            if ((ulong)info.Width * info.Height > 40_000_000) throw new InvalidDataException("Image exceeds 40 megapixels.");
            var mime = format switch
            {
                MagickFormat.Png => "image/png", MagickFormat.Jpeg => "image/jpeg", MagickFormat.WebP => "image/webp",
                MagickFormat.Gif => "image/gif", MagickFormat.Bmp => "image/bmp", _ => "image/tiff"
            };
            return new(checked((int)info.Width), checked((int)info.Height), mime);
        }
        catch (MagickException exception) { throw new InvalidDataException("Invalid raster image.", exception); }
    }

    public void ResizeToFit(int width, int height) => image.Resize((uint)width, (uint)height);
    public void AutoOrient() => image.AutoOrient();
    public byte[] PixelsRgba()
    {
        image.Depth = 8;
        return image.ToByteArray(MagickFormat.Rgba);
    }

    public void CropTo(int width, int height)
    {
        image.Resize(new MagickGeometry((uint)width, (uint)height) { FillArea = true });
        image.Crop((uint)width, (uint)height, Gravity.Center);
        image.ResetPage();
    }

    public Task SaveJpegAsync(Stream output, int quality, CancellationToken cancellationToken = default)
    {
        image.Quality = (uint)quality;
        image.Depth = 8;
        return image.WriteAsync(output, MagickFormat.Jpeg, cancellationToken);
    }

    private static MagickFormat FormatFor(byte[] bytes)
    {
        ReadOnlySpan<byte> header = bytes;
        if (header.StartsWith<byte>([137, 80, 78, 71, 13, 10, 26, 10])) return MagickFormat.Png;
        if (header.StartsWith<byte>([255, 216, 255])) return MagickFormat.Jpeg;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8)) return MagickFormat.WebP;
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return MagickFormat.Gif;
        if (header.StartsWith("BM"u8)) return MagickFormat.Bmp;
        if (header.StartsWith<byte>([73, 73, 42, 0]) || header.StartsWith<byte>([77, 77, 0, 42])
            || header.StartsWith<byte>([73, 73, 43, 0]) || header.StartsWith<byte>([77, 77, 0, 43])) return MagickFormat.Tiff;
        throw new InvalidDataException("Unsupported raster image format.");
    }

    public void Dispose() => image.Dispose();
}
