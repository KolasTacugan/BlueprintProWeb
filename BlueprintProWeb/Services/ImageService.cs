using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace BlueprintProWeb.Services
{
    public class ImageService
    {
        private readonly IWebHostEnvironment _env;

        public ImageService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task ApplyWatermark(string imagePath)
        {
            var webRoot = _env.WebRootPath;
            var watermarkPath = Path.Combine(webRoot, "images", "BPP-watermark.png");

            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Target image not found", imagePath);

            using (var image = await Image.LoadAsync<Rgba32>(imagePath))
            {
                if (File.Exists(watermarkPath))
                {
                    using (var watermark = await Image.LoadAsync<Rgba32>(watermarkPath))
                    {
                        // Resize watermark to match or fit proportionally
                        watermark.Mutate(w => w.Resize(image.Width, image.Height));

                        float opacity = 0.28f; // ✅ adjustable transparency
                        image.Mutate(ctx => ctx.DrawImage(watermark, new Point(0, 0), opacity));

                        var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                        await image.SaveAsync(imagePath, encoder); // overwrite the same file
                    }
                }
            }
        }
    }
}
