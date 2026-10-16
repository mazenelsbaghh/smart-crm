using Shared.Imaging;

namespace Modules.Media.Services;

public interface IImageTransformer
{
    Task<Stream> CreateThumbnailAsync(Stream inputStream);
    Task<Stream> OptimizeForWhatsAppAsync(Stream inputStream);
}

public sealed class ImageTransformer : IImageTransformer
{
    public async Task<Stream> CreateThumbnailAsync(Stream inputStream)
    {
        inputStream.Position = 0;
        using var image = await RasterImage.LoadAsync(inputStream);
        image.ResizeToFit(150, 150);
        return await EncodeAsync(image, 75);
    }

    public async Task<Stream> OptimizeForWhatsAppAsync(Stream inputStream)
    {
        inputStream.Position = 0;
        using var image = await RasterImage.LoadAsync(inputStream);
        if (image.Width > 1600 || image.Height > 1600) image.ResizeToFit(1600, 1600);
        return await EncodeAsync(image, 80);
    }

    private static async Task<Stream> EncodeAsync(RasterImage image, int quality)
    {
        var output = new MemoryStream();
        await image.SaveJpegAsync(output, quality);
        output.Position = 0;
        return output;
    }
}
