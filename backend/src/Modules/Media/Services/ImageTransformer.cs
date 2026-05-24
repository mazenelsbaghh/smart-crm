using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Modules.Media.Services
{
    public interface IImageTransformer
    {
        Task<Stream> CreateThumbnailAsync(Stream inputStream);
        Task<Stream> OptimizeForWhatsAppAsync(Stream inputStream);
    }

    public class ImageTransformer : IImageTransformer
    {
        public async Task<Stream> CreateThumbnailAsync(Stream inputStream)
        {
            var outputStream = new MemoryStream();
            inputStream.Position = 0;

            // Load and transform the image on a separate thread to keep it async friendly
            await Task.Run(() =>
            {
                using var image = Image.Load(inputStream);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(150, 150),
                    Mode = ResizeMode.Max
                }));
                
                // Save as JPEG with 75% quality for thumbnail
                image.Save(outputStream, new JpegEncoder { Quality = 75 });
            });

            outputStream.Position = 0;
            return outputStream;
        }

        public async Task<Stream> OptimizeForWhatsAppAsync(Stream inputStream)
        {
            var outputStream = new MemoryStream();
            inputStream.Position = 0;

            await Task.Run(() =>
            {
                using var image = Image.Load(inputStream);
                
                // Downscale if the image exceeds 1600px in either dimension
                if (image.Width > 1600 || image.Height > 1600)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(1600, 1600),
                        Mode = ResizeMode.Max
                    }));
                }

                // Compress image to 80% quality to fit WhatsApp constraints
                image.Save(outputStream, new JpegEncoder { Quality = 80 });
            });

            outputStream.Position = 0;
            return outputStream;
        }
    }
}
