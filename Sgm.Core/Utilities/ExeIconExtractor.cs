using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Sgm.Core.Utilities;

/// <summary>Extract an executable's shell icon to a PNG (Windows).</summary>
public static class ExeIconExtractor
{
    /// <summary>Write the executable's icon to <paramref name="dest"/> as PNG. False when unavailable.</summary>
    public static bool ExtractPng(string exe, string dest)
    {
        if (!File.Exists(exe))
            return false;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exe);
            if (icon == null)
                return false;
            using var bitmap = icon.ToBitmap();
            var dir = Path.GetDirectoryName(dest);
            if (dir != null)
                Directory.CreateDirectory(dir);
            bitmap.Save(dest, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
