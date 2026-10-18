using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Sgm.Core.Artwork;
using Sgm.Core.Models;
using Sgm.Core.Steam;
using Sgm.Core.Vdf;

namespace Sgm.App.Services;

/// <summary>
/// Resolves a library cover for a game: an account's existing grid cover first,
/// otherwise store art cached under <c>.sgm_cache/thumb_{appid}.jpg</c>.
/// </summary>
public static class ThumbnailService
{
    public static string CacheDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, ".sgm_cache");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Best available cover path, or null. May download — call off the UI thread.</summary>
    public static string? Resolve(GameInfo game, IReadOnlyList<string> accountDirs)
    {
        var appid = ShortcutStore.GridAppIdFor(game);

        foreach (var dir in accountDirs)
        {
            var existing = Path.Combine(SteamPaths.GridDir(dir), $"{appid}p.jpg");
            if (File.Exists(existing) && new FileInfo(existing).Length > 0)
                return existing;
        }

        var dest = Path.Combine(CacheDir(), $"thumb_{appid}.jpg");
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return dest;

        using var source = AutoArtwork.LocalThumbImage(game.ArtUrls) ?? DownloadThumbnail(game.Name, game.Source);
        if (source == null)
            return null;

        try
        {
            const int height = 300;
            var width = Math.Max(1, (int)Math.Round((double)source.Width * height / source.Height));
            using var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, 0, 0, width, height);
            }
            if (ImageService.SaveJpg(resized, dest, 82))
                return dest;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static Bitmap? DownloadThumbnail(string name, string source)
    {
        var url = AutoArtwork.ThumbnailUrl(name, source);
        return string.IsNullOrEmpty(url) ? null : ImageService.Download(url);
    }
}
