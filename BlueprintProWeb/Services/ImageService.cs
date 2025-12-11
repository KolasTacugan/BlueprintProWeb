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

        public async Task<(string originalPath, string marketPath, string watermarkedPath)> ProcessImageAsync(IFormFile file, string? oldFileName = null)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Invalid image file");

            var webRoot = _env.WebRootPath;
            var originalsDir = Path.Combine(webRoot, "images", "originals");
            var marketDir = Path.Combine(webRoot, "images", "market");
            var watermarkedDir = Path.Combine(webRoot, "images", "watermarked");

            Directory.CreateDirectory(originalsDir);
            Directory.CreateDirectory(marketDir);
            Directory.CreateDirectory(watermarkedDir);

            // 🧼 If editing: clean up old files
            if (!string.IsNullOrEmpty(oldFileName))
            {
                var oldOriginal = Path.Combine(originalsDir, oldFileName);
                var oldMarket = Path.Combine(marketDir, oldFileName);
                var oldWatermark = Path.Combine(watermarkedDir, oldFileName);

                TryDeleteFile(oldOriginal);
                TryDeleteFile(oldMarket);
                TryDeleteFile(oldWatermark);
            }

            // 🆕 Generate new filename
            var ext = Path.GetExtension(file.FileName);
            var safeName = $"{Guid.NewGuid()}{ext}";

            var originalPath = Path.Combine(originalsDir, safeName);
            var marketPath = Path.Combine(marketDir, safeName);
            var watermarkedPath = Path.Combine(watermarkedDir, safeName);

            // 🧠 Load into memory (avoid file locks)
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            // 📝 Save original file to disk
            await System.IO.File.WriteAllBytesAsync(originalPath, fileBytes);

            memoryStream.Position = 0;
            using (var image = Image.Load<Rgba32>(memoryStream))
            {
                // 🛍 1. Marketplace version (resized clean)
                var marketWidth = 1200;
                var marketHeight = (int)(image.Height * (marketWidth / (double)image.Width));
                using (var marketImg = image.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(marketWidth, marketHeight),
                    Mode = ResizeMode.Max
                })))
                {
                    await marketImg.SaveAsync(marketPath);
                }

                // 💧 2. Watermarked version
                var watermarkFile = Path.Combine(webRoot, "images", "BPP-watermark.png");
                if (!System.IO.File.Exists(watermarkFile))
                {
                    // if no watermark image, just save a copy
                    await image.SaveAsync(watermarkedPath);
                }
                else
                {
                    using (var watermarkImg = Image.Load<Rgba32>(watermarkFile))
                    {
                        watermarkImg.Mutate(w => w.Resize(image.Width, image.Height));
                        float opacity = 0.55f;

                        image.Mutate(ctx => ctx.DrawImage(
                            watermarkImg,
                            new Point(0, 0),
                            opacity
                        ));

                        var encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                        await image.SaveAsync(watermarkedPath, encoder);
                    }
                }
            }

            var marketPublic = $"/images/market/{safeName}";
            var watermarkedPublic = $"/images/watermarked/{safeName}";

            return (originalPath, marketPublic, watermarkedPublic);
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch (IOException)
            {
                // if it's locked, just skip deletion
            }
            catch (Exception)
            {
                // swallow other errors silently
            }
        }
    }
}
