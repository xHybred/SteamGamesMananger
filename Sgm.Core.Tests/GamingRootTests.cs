using Sgm.Core.Models;
using Sgm.Core.Scanners;

namespace Sgm.Core.Tests;

public class GamingRootTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-gr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ParsesGameConfigWithDisplayNameAndExe()
    {
        var root = TempDir();
        try
        {
            var game = Path.Combine(root, "SomeGame");
            var content = Path.Combine(game, "Content");
            Directory.CreateDirectory(content);
            File.WriteAllBytes(Path.Combine(content, "Game.exe"), new byte[4]);
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"),
                "<?xml version=\"1.0\"?><Game><Identity Name=\"Pub.SomeGame\" />" +
                "<Executable Name=\"Game.exe\" />" +
                "<ShellVisuals DefaultDisplayName=\"Some Game\" Square480x480Logo=\"Logo.png\" />" +
                "</Game>");

            var parsed = GamingRoot.ParseGameConfig(Path.Combine(content, "MicrosoftGame.config"));
            Assert.NotNull(parsed);
            Assert.Equal("Some Game", parsed!.Value.Name);
            Assert.EndsWith("Game.exe", parsed.Value.Exe);
            Assert.Equal("pub.somegame", GamingRoot.IdentityName(Path.Combine(content, "MicrosoftGame.config")).ToLowerInvariant());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SkipsWhenExecutableMissing()
    {
        var root = TempDir();
        try
        {
            var content = Path.Combine(root, "Dead", "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"),
                "<Game><Executable Name=\"Missing.exe\" /></Game>");
            Assert.Null(GamingRoot.ParseGameConfig(Path.Combine(content, "MicrosoftGame.config")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrefersSquareLogoOverSplash()
    {
        var root = TempDir();
        try
        {
            var content = Path.Combine(root, "G", "Content");
            Directory.CreateDirectory(content);
            File.WriteAllBytes(Path.Combine(content, "logo.png"), new byte[4]);
            File.WriteAllBytes(Path.Combine(content, "splash.png"), new byte[4]);
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"),
                "<Game><ShellVisuals Square480x480Logo=\"logo.png\" SplashScreenImage=\"splash.png\" /></Game>");

            var art = GamingRoot.ConfigArt(Path.Combine(content, "MicrosoftGame.config"));
            Assert.True(art.ContainsKey("squares"));
            Assert.EndsWith("logo.png", art["squares"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanGamingRootsHonoursSkipNames()
    {
        var root = TempDir();
        try
        {
            var content = Path.Combine(root, "G", "Content");
            Directory.CreateDirectory(content);
            File.WriteAllBytes(Path.Combine(content, "Game.exe"), new byte[4]);
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"),
                "<Game><Identity Name=\"Pub.Game\" /><Executable Name=\"Game.exe\" />" +
                "<ShellVisuals DefaultDisplayName=\"Game\" /></Game>");

            GamingRoot.TargetProvider = () => new List<string> { root };
            try
            {
                Assert.Single(GamingRoot.ScanGamingRoots());
                Assert.Empty(GamingRoot.ScanGamingRoots(new HashSet<string> { "pub.game" }));
            }
            finally
            {
                GamingRoot.TargetProvider = () => new List<string>();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
