using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.Content.Services;

public sealed class LogoBrandingService
{
    private const int PaletteSampleSize = 96;
    private const int PaletteColorCount = 5;

    public async Task<IReadOnlyList<string>> ExtractPaletteAsync(
        Stream logoStream,
        CancellationToken cancellationToken)
    {
        using var logo = await Image.LoadAsync<Rgba32>(logoStream, cancellationToken);
        logo.Mutate(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(PaletteSampleSize, PaletteSampleSize)
        }));

        var buckets = new Dictionary<int, ColorBucket>();
        logo.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (var pixel in row)
                {
                    if (pixel.A < 128) continue;
                    var key = ((pixel.R >> 4) << 8) | ((pixel.G >> 4) << 4) | (pixel.B >> 4);
                    if (!buckets.TryGetValue(key, out var bucket)) bucket = new ColorBucket();
                    bucket.Add(pixel);
                    buckets[key] = bucket;
                }
            }
        });

        var palette = new List<Rgba32>();
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

    private static double ColorDistance(Rgba32 left, Rgba32 right)
    {
        var red = left.R - right.R;
        var green = left.G - right.G;
        var blue = left.B - right.B;
        return Math.Sqrt(red * red + green * green + blue * blue);
    }

    private static string ToHex(Rgba32 color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private struct ColorBucket
    {
        private long _red;
        private long _green;
        private long _blue;

        public int Count { get; private set; }

        public void Add(Rgba32 pixel)
        {
            _red += pixel.R;
            _green += pixel.G;
            _blue += pixel.B;
            Count++;
        }

        public readonly Rgba32 Average() => Count == 0
            ? new Rgba32(17, 24, 39)
            : new Rgba32((byte)(_red / Count), (byte)(_green / Count), (byte)(_blue / Count));
    }
}
