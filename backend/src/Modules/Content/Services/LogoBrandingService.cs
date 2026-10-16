using Shared.Imaging;

namespace Modules.Content.Services;

public sealed class LogoBrandingService
{
    private const int PaletteSampleSize = 96;
    private const int PaletteColorCount = 5;

    public async Task<IReadOnlyList<string>> ExtractPaletteAsync(
        Stream logoStream,
        CancellationToken cancellationToken)
    {
        using var logo = await RasterImage.LoadAsync(logoStream, cancellationToken);
        if (logo.Width > PaletteSampleSize || logo.Height > PaletteSampleSize)
            logo.ResizeToFit(PaletteSampleSize, PaletteSampleSize);

        var buckets = new Dictionary<int, ColorBucket>();
        var pixels = logo.PixelsRgba();
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var pixel = new PalettePixel(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
            if (pixel.A < 128) continue;
            var key = ((pixel.R >> 4) << 8) | ((pixel.G >> 4) << 4) | (pixel.B >> 4);
            if (!buckets.TryGetValue(key, out var bucket)) bucket = new ColorBucket();
            bucket.Add(pixel);
            buckets[key] = bucket;
        }

        var palette = new List<PalettePixel>();
        foreach (var bucket in buckets.Values.OrderByDescending(bucket => bucket.Count))
        {
            var color = bucket.Average();
            if (palette.Any(existing => ColorDistance(existing, color) < 72)) continue;
            palette.Add(color);
            if (palette.Count == PaletteColorCount) break;
        }

        return palette.Count > 0
            ? palette.Select(ToHex).ToArray()
            : new[] { "#111827" };
    }

    private static double ColorDistance(PalettePixel left, PalettePixel right)
    {
        var red = left.R - right.R;
        var green = left.G - right.G;
        var blue = left.B - right.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static string ToHex(PalettePixel color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private readonly record struct PalettePixel(byte R, byte G, byte B, byte A);

    private struct ColorBucket
    {
        private long _red;
        private long _green;
        private long _blue;

        public int Count { get; private set; }

        public void Add(PalettePixel pixel)
        {
            _red += pixel.R;
            _green += pixel.G;
            _blue += pixel.B;
            Count++;
        }

        public readonly PalettePixel Average() => Count == 0
            ? new PalettePixel(17, 24, 39, 255)
            : new PalettePixel((byte)(_red / Count), (byte)(_green / Count), (byte)(_blue / Count), 255);
    }
}
