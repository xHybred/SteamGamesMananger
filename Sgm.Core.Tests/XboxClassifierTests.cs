using Sgm.Core.Scanners;
using Sgm.Core.Scanners.Xbox;

namespace Sgm.Core.Tests;

public class XboxClassifierTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-xb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator", "Calculator")]
    [InlineData("Microsoft.VCLibs.140.00", "Microsoft.VCLibs")]
    [InlineData("Microsoft.NET.Native.Framework", "Microsoft.NET")]
    [InlineData("Microsoft.XboxGameCallableUI", "Xbox")]
    [InlineData("Anything", "1527c705-839a-4832-9118-54d4bd6a0c89")]
    [InlineData("Anything", "")]
    public void ShouldSkipSystemAndGuidNames(string family, string name)
        => Assert.True(XboxRules.ShouldSkip(family, name));

    [Theory]
    [InlineData("Steam", "Steam")]
    [InlineData("PocketpairInc.Palworld", "Palworld")]
    [InlineData("BethesdaSoftworks.DOOMEternal", "DOOMEternal")]
    public void KeepsRealGames(string family, string name)
        => Assert.False(XboxRules.ShouldSkip(family, name));

    [Fact]
    public void UtilityByExtensionIsSkippedButGamesAreKept()
    {
        Assert.True(XboxRules.IsUtility("SomeApp", "windows.appExecutionAlias,windows.service", hasManifest: true));
        Assert.True(XboxRules.IsUtility("SomeDialog", "", hasManifest: false));
        Assert.False(XboxRules.IsUtility("Palworld", "windows.game", hasManifest: true));
    }

    [Fact]
    public void ExtensionKindsNormalizeWindowsPrefix()
    {
        var kinds = XboxRules.ExtensionKinds("windows.appExecutionAlias,Windows.service, game");
        Assert.Contains("appexecutionalias", kinds);
        Assert.Contains("service", kinds);
        Assert.Contains("game", kinds);
    }

    [Fact]
    public void ManifestArtUsesLargestScale()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Logo.scale-100.png"), new byte[4]);
            File.WriteAllBytes(Path.Combine(dir, "Logo.scale-200.png"), new byte[4]);
            File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"),
                "<?xml version=\"1.0\"?><Package><Applications><Application Id=\"App\">" +
                "<VisualElements Square150x150Logo=\"Logo.png\" /></Application></Applications></Package>");

            var art = XboxScanner.ManifestArt(dir);
            Assert.True(art.ContainsKey("squares"));
            Assert.EndsWith("Logo.scale-200.png", art["squares"]);
            Assert.Equal("App", XboxScanner.ManifestAppId(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CandidatePackagesExcludeFrameworksAndResources()
    {
        Assert.True(XboxRules.IsCandidatePackage("PocketpairInc.Palworld", "PocketpairInc.Palworld_abc", false, false));
        Assert.False(XboxRules.IsCandidatePackage("Microsoft.VCLibs.140.00", "Microsoft.VCLibs.140.00_abc", true, false));
        Assert.False(XboxRules.IsCandidatePackage("Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_abc", false, true));
        Assert.False(XboxRules.IsCandidatePackage("", "family", false, false));
    }

    [Fact]
    public void OnlyGamingRootIdentitiesCountAsGames()
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PocketpairInc.Palworld" };
        Assert.True(XboxRules.IsRegisteredGame("PocketpairInc.Palworld", identities));
        Assert.True(XboxRules.IsRegisteredGame("pocketpairinc.palworld", identities));
        Assert.False(XboxRules.IsRegisteredGame("Microsoft.WindowsCalculator", identities));
        Assert.False(XboxRules.IsRegisteredGame("", identities));
    }

    [Fact]
    public void ManifestReadsDisplayNameAndSplashArt()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Splash.png"), new byte[4]);
            File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"),
                "<?xml version=\"1.0\"?><Package><Properties><DisplayName>Ignored</DisplayName></Properties>" +
                "<Applications><Application Id=\"Game\"><VisualElements DisplayName=\"Palworld\" SplashScreenImage=\"Splash.png\" />" +
                "</Application></Applications></Package>");

            var manifest = XboxScanner.ReadManifest(dir);
            Assert.Equal("Palworld", manifest.DisplayName);
            Assert.Equal(new[] { "Game" }, manifest.AppIds);
            Assert.EndsWith("Splash.png", manifest.Art["heroes"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ManifestSkipsMsResourceDisplayNames()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"),
                "<?xml version=\"1.0\"?><Package><Applications><Application Id=\"Game\">" +
                "<VisualElements DisplayName=\"ms-resource://App/Resources/Name\" /></Application></Applications></Package>");

            Assert.Equal("", XboxScanner.ReadManifest(dir).DisplayName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FriendlyNamesExpandKnownIds()
    {
        Assert.Equal("Minecraft: Bedrock", XboxRules.FriendlyName("Microsoft.MinecraftUWP"));
        Assert.Equal("Palworld", XboxRules.FriendlyName("PocketpairInc.Palworld"));
    }
}
