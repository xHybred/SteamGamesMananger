using System.Drawing;
using Microsoft.Data.Sqlite;
using Sgm.Core.Artwork;
using Sgm.Core.Scanners;

namespace Sgm.Core.Tests;

public class ArtworkLocalTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-art-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePng(string path, int w = 8, int h = 8)
    {
        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.CornflowerBlue);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [Fact]
    public void DownloadLoadsLocalFile()
    {
        var dir = TempDir();
        try
        {
            var png = Path.Combine(dir, "local.png");
            WritePng(png);
            using var img = ImageService.Download(png);
            Assert.NotNull(img);
            Assert.Equal(8, img!.Width);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DownloadReturnsNullForMissingPath()
        => Assert.Null(ImageService.Download(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"))));

    [Fact]
    public void GalaxyArtPrefersWebcacheThenGamePiece()
    {
        var baseDir = TempDir();
        try
        {
            var storage = Path.Combine(baseDir, "storage");
            Directory.CreateDirectory(storage);
            var db = Path.Combine(storage, "galaxy-2.0.db");

            // Webcache cover for release 11111_22222.
            var webcacheFile = Path.Combine(baseDir, "webcache", "user1", "gog", "22222", "cover.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(webcacheFile)!);
            WritePng(webcacheFile);

            using (var con = new SqliteConnection($"Data Source={db};Pooling=False"))
            {
                con.Open();
                Exec(con, "CREATE TABLE WebCacheResourceTypes (id INTEGER, type TEXT)");
                Exec(con, "INSERT INTO WebCacheResourceTypes VALUES (3, 'verticalCover')");
                Exec(con, "CREATE TABLE WebCache (id INTEGER, releaseKey TEXT, userId TEXT)");
                Exec(con, "INSERT INTO WebCache VALUES (1, '11111_22222', 'user1')");
                Exec(con, "CREATE TABLE WebCacheResources (webCacheId INTEGER, filename TEXT, webCacheResourceTypeId INTEGER)");
                Exec(con, "INSERT INTO WebCacheResources VALUES (1, 'cover.jpg', 3)");
                Exec(con, "CREATE TABLE GamePieceTypes (id INTEGER, type TEXT)");
                Exec(con, "INSERT INTO GamePieceTypes VALUES (378, 'originalImages')");
                Exec(con, "CREATE TABLE GamePieces (releaseKey TEXT, value TEXT, gamePieceTypeId INTEGER)");
                Exec(con, "INSERT INTO GamePieces VALUES ('33333_44444', '{\"verticalCover\":\"https://cdn/gog.jpg\"}', 378)");
            }

            var art = GogScanner.GalaxyArt(db);
            Assert.Equal(webcacheFile, art["22222"]["grids"]);
            Assert.Equal("https://cdn/gog.jpg", art["44444"]["grids"]);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    private static void Exec(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void ComposeSquareCoverProducesPortraitWithCenteredMark()
    {
        using var square = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(square))
        {
            g.Clear(Color.Transparent);
            g.FillRectangle(Brushes.White, 16, 16, 32, 32);
        }

        var composed = ImageService.ComposeSquareCover(square, null, new Size(300, 450));
        Assert.Equal(300, composed.Width);
        Assert.Equal(450, composed.Height);
        composed.Dispose();
    }
}
