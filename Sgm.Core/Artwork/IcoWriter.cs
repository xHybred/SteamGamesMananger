using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Sgm.Core.Artwork;

/// <summary>
/// Writes PNG-compressed ICO files (Vista+), one entry per requested size.
/// </summary>
public static class IcoWriter
{
    private static readonly int[] DefaultSizes = { 256 };

    public static void Write(Bitmap source, string dest, int[]? sizes = null)
    {
        sizes ??= DefaultSizes;
        var images = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes)
        {
            images.Add((size, EncodePng(source, size)));
        }
        WriteContainer(images, dest);
    }

    private static byte[] EncodePng(Bitmap source, int size)
    {
        using var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, size, size);
        }
        using var ms = new MemoryStream();
        resized.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static void WriteContainer(List<(int Size, byte[] Png)> images, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);

        Span<byte> header = stackalloc byte[6];
        header[0] = 0; header[1] = 0;             // reserved
        header[2] = 1; header[3] = 0;             // type = icon
        header[4] = (byte)(images.Count & 0xFF);
        header[5] = (byte)((images.Count >> 8) & 0xFF);
        fs.Write(header);

        var offset = 6 + 16 * images.Count;
        Span<byte> entry = stackalloc byte[16];
        foreach (var (size, png) in images)
        {
            entry[0] = (byte)(size >= 256 ? 0 : size);
            entry[1] = (byte)(size >= 256 ? 0 : size);
            entry[2] = 0;                          // color count
            entry[3] = 0;                          // reserved
            entry[4] = 1; entry[5] = 0;            // planes
            entry[6] = 32; entry[7] = 0;           // bit count
            BitConverter.TryWriteBytes(entry[8..12], png.Length);
            BitConverter.TryWriteBytes(entry[12..16], offset);
            fs.Write(entry);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
            fs.Write(png);
    }
}
