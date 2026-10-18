using System.Text;
using Sgm.Core.Models;
using Sgm.Core.Scanners;

namespace Sgm.Core.Tests;

public class DetectionTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void EpicCategoryFilterKeepsOnlyGames()
    {
        var dir = TempDir();
        try
        {
            const string baseJson = "\"DisplayName\":\"X\",\"AppName\":\"X\",\"InstallLocation\":\"C:\\\\x\"";
            File.WriteAllText(Path.Combine(dir, "game.item"),
                $"{{{baseJson},\"AppCategories\":[\"public\",\"games\",\"applications\"]}}");
            File.WriteAllText(Path.Combine(dir, "engine.item"),
                $"{{{baseJson},\"AppCategories\":[\"engines/ue5\",\"engines\"]}}");
            File.WriteAllText(Path.Combine(dir, "hidden.item"),
                $"{{{baseJson},\"AppCategories\":[\"Content\",\"hidden\"]}}");

            Assert.NotNull(EpicScanner.ParseManifest(Path.Combine(dir, "game.item"), "launcher.exe"));
            Assert.Null(EpicScanner.ParseManifest(Path.Combine(dir, "engine.item"), "launcher.exe"));
            Assert.Null(EpicScanner.ParseManifest(Path.Combine(dir, "hidden.item"), "launcher.exe"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EpicCatalogArtIsExactAndAttached()
    {
        var dir = TempDir();
        try
        {
            const string items = "[{\"id\":\"abc\",\"keyImages\":[" +
                                 "{\"type\":\"DieselGameBoxTall\",\"url\":\"https://cdn/tall.jpg\"}," +
                                 "{\"type\":\"DieselGameBox\",\"url\":\"https://cdn/box.jpg\"}]}," +
                                 "{\"id\":\"nothing\",\"keyImages\":[]}]";
            var cache = Path.Combine(dir, "catcache.bin");
            File.WriteAllText(cache, Convert.ToBase64String(Encoding.UTF8.GetBytes(items)));

            var catalog = EpicScanner.LoadCatalogArt(cache);
            Assert.Equal("https://cdn/tall.jpg", catalog["abc"]["grids"]);
            Assert.Equal("https://cdn/box.jpg", catalog["abc"]["heroes"]);
            Assert.False(catalog.ContainsKey("nothing"));

            var manifest = Path.Combine(dir, "g.item");
            File.WriteAllText(manifest, "{\"DisplayName\":\"Game\",\"AppName\":\"G\",\"InstallLocation\":\"C:\\\\g\"," +
                                        "\"CatalogItemId\":\"abc\",\"AppCategories\":[\"games\"]}");
            var game = EpicScanner.ParseManifest(manifest, "launcher.exe", catalog);
            Assert.NotNull(game);
            Assert.Equal("https://cdn/tall.jpg", game!.ArtUrls["grids"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EpicCatalogMissingFileIsEmpty()
    {
        Assert.Empty(EpicScanner.LoadCatalogArt(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".bin")));
    }

    [Fact]
    public void MainExePrefersLargestNonHelper()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Game.exe"), new byte[5000]);
            File.WriteAllBytes(Path.Combine(dir, "GameHelper.exe"), new byte[9000]);
            File.WriteAllBytes(Path.Combine(dir, "unins000.exe"), new byte[9999]);
            File.WriteAllBytes(Path.Combine(dir, "Crash_Reporter.exe"), new byte[9999]);
            Assert.Equal("Game.exe", Path.GetFileName(UbisoftScanner.PickMainExe(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MainExeNoneWhenOnlyHelpers()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "setup.exe"), new byte[1]);
            Assert.Null(UbisoftScanner.PickMainExe(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CrossStoreDuplicatesKeepHighestPriority()
    {
        var games = new[]
        {
            new GameInfo { Name = "Palworld", Exe = "a.exe", Source = GameSource.Epic },
            new GameInfo { Name = "Palworld!", Exe = "b.exe", Source = GameSource.Xbox },
            new GameInfo { Name = "Other", Exe = "c.exe", Source = GameSource.Gog },
        };
        var result = Scanning.DedupeTitles(games);
        Assert.Equal(new[] { GameSource.Epic, GameSource.Gog }, result.Select(g => g.Source).ToArray());
    }

    [Fact]
    public void EpicManifestParsingUsesProtocolForm()
    {
        var dir = TempDir();
        try
        {
            var manifest = Path.Combine(dir, "Demo.item");
            File.WriteAllText(manifest, "{\"DisplayName\":\"Demo Game\",\"AppName\":\"DemoApp\",\"CatalogNamespace\":\"demo\"," +
                                        "\"InstallLocation\":\"C:\\\\Games\\\\Demo\",\"LaunchExecutable\":\"Demo.exe\",\"bIsApplication\":true}");
            var g = EpicScanner.ParseManifest(manifest, @"C:\Epic\EpicGamesLauncher.exe");
            Assert.NotNull(g);
            Assert.Equal("Demo Game", g!.Name);
            Assert.EndsWith("EpicGamesLauncher.exe", g.Exe);
            Assert.Equal("com.epicgames.launcher://apps/DemoApp?action=launch&silent=true", g.LaunchOptions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EpicManifestSkipsNonGames()
    {
        var dir = TempDir();
        try
        {
            var p = Path.Combine(dir, "ue.item");
            File.WriteAllText(p, "{\"DisplayName\":\"UE\",\"AppName\":\"UE\",\"CatalogNamespace\":\"ue\"}");
            Assert.Null(EpicScanner.ParseManifest(p, "x"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
