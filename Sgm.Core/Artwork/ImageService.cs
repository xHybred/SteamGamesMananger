using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;

namespace Sgm.Core.Artwork;

/// <summary>
/// Image loading, resizing, compositing and saving (System.Drawing) for the
/// artwork pipeline.
/// </summary>
public static class ImageService
{
    public static readonly Size GridSize = new(600, 900);
    public static readonly Size HeroSize = new(1920, 620);
    public const int IconSize = 256;

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    /// <summary>Load an image from a URL or a local file path (store caches).</summary>
    public static Bitmap? Download(string url)
    {
        try
        {
            if (File.Exists(url))
            {
                using var stream = File.OpenRead(url);
                using var src = new Bitmap(stream);
                return new Bitmap(src);
            }

            using var response = Client.GetAsync(url, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var ms = new MemoryStream(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            using var remote = new Bitmap(ms);
            return new Bitmap(remote);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Center-crop to the target aspect ratio, then high-quality resize.</summary>
    public static Bitmap CoverResize(Bitmap img, Size size)
    {
        var scale = Math.Max((double)size.Width / img.Width, (double)size.Height / img.Height);
        var scaledW = (int)Math.Round(img.Width * scale);
        var scaledH = (int)Math.Round(img.Height * scale);
        var x = Math.Max(0, (scaledW - size.Width) / 2);
        var y = Math.Max(0, (scaledH - size.Height) / 2);

        var output = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(img, new Rectangle(-x, -y, scaledW, scaledH));
        return output;
    }

    public static bool SaveJpg(Bitmap img, string dest, long quality = 90)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var flat = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(flat))
            {
                g.Clear(Color.Black);
                g.DrawImageUnscaled(img, 0, 0);
            }
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            flat.Save(dest, codec, parameters);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool SavePng(Bitmap img, string dest, int maxWidth = 1024)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (img.Width > maxWidth)
            {
                var resized = CoverResize(img, new Size(maxWidth, (int)Math.Round((double)img.Height * maxWidth / img.Width)));
                resized.Save(dest, ImageFormat.Png);
                resized.Dispose();
            }
            else
            {
                img.Save(dest, ImageFormat.Png);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool SaveIco(Bitmap img, string dest)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var square = CoverResize(img, new Size(IconSize, IconSize));
            IcoWriter.Write(square, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Crop a logo to its visible pixels so padding never pushes the mark off-center.</summary>
    public static Bitmap TrimTransparent(Bitmap img)
    {
        var minX = img.Width;
        var minY = img.Height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < img.Height; y++)
        {
            for (var x = 0; x < img.Width; x++)
            {
                if (img.GetPixel(x, y).A > 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < 0)
            return img;
        var rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var cropped = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(img, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return cropped;
    }

    /// <summary>Center-crop to a square so icons never come out stretched.</summary>
    public static Bitmap SquareCrop(Bitmap img)
    {
        var side = Math.Min(img.Width, img.Height);
        var x = (img.Width - side) / 2;
        var y = (img.Height - side) / 2;
        var square = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(square);
        g.DrawImage(img, new Rectangle(0, 0, side, side), new Rectangle(x, y, side, side), GraphicsUnit.Pixel);
        return square;
    }

    /// <summary>Square logo centered over a dimmed, blurred splash (or a flat dark field).</summary>
    public static Bitmap ComposeSquareCover(Bitmap square, Bitmap? backdrop, Size size)
    {
        var baseImg = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(baseImg))
        {
            if (backdrop != null)
            {
                using var resized = CoverResize(backdrop, size);
                using var blurred = GaussianBlur(resized, 14);
                g.DrawImage(blurred, 0, 0);
                using var dim = new SolidBrush(Color.FromArgb(128, 0, 0, 0));
                g.FillRectangle(dim, 0, 0, size.Width, size.Height);
            }
            else
            {
                g.Clear(Color.FromArgb(27, 40, 56));
            }

            var side = (int)Math.Round(size.Width * 0.8);
            using var mark = SquareCrop(square);
            var scaled = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var mg = Graphics.FromImage(scaled))
            {
                mg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                mg.DrawImage(mark, 0, 0, side, side);
            }
            g.DrawImage(scaled, new Point((size.Width - side) / 2, (size.Height - side) / 2));
        }
        return baseImg;
    }

    /// <summary>Approximate a Gaussian blur by compositing progressively smaller, offset copies.</summary>
    public static Bitmap GaussianBlur(Bitmap img, int radius)
    {
        var output = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(output);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        // Downscale + upscale is a cheap stand-in for a true Gaussian blur.
        var factor = Math.Max(2, radius / 2);
        var w = Math.Max(1, img.Width / factor);
        var h = Math.Max(1, img.Height / factor);
        using var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.DrawImage(img, 0, 0, w, h);
        }
        g.DrawImage(small, 0, 0, img.Width, img.Height);
        return output;
    }
}
