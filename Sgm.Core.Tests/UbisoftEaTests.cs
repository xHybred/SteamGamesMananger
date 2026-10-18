using Sgm.Core.Scanners;

namespace Sgm.Core.Tests;

public class UbisoftEaTests
{
    [Fact]
    public void UbisoftAssetReturnsDirectImageUrl()
    {
        const string blob = "thumb_image: covers/assassins-creed.jpg\n";
        Assert.Equal(
            UbisoftScanner.AssetsBase + "covers/assassins-creed.jpg",
            UbisoftScanner.Asset(blob, "thumb_image"));
    }

    [Fact]
    public void UbisoftAssetResolvesLocalizationKey()
    {
        const string blob = "logo_image: br_logo\n    br_logo: covers/splinter-cell_br.png\n";
        Assert.Equal(
            UbisoftScanner.AssetsBase + "covers/splinter-cell_br.png",
            UbisoftScanner.Asset(blob, "logo_image"));
    }

    [Fact]
    public void UbisoftAssetMissingFieldIsEmpty()
        => Assert.Equal("", UbisoftScanner.Asset("other: x\n", "thumb_image"));

    [Fact]
    public void UbisoftInstallDirStripsTrailingSeparators()
    {
        var normalized = UbisoftScanner.NormalizeInstallDir(
            "C:/Program Files (x86)/Ubisoft/Ubisoft Game Launcher/games/Tom Clancy's Rainbow Six Siege/");
        Assert.NotNull(normalized);
        Assert.Equal("Tom Clancy's Rainbow Six Siege", Path.GetFileName(normalized!));
        Assert.DoesNotContain("/", normalized!);
    }

    [Fact]
    public void UbisoftInstallDirRejectsEmpty()
    {
        Assert.Null(UbisoftScanner.NormalizeInstallDir("  "));
    }

    [Fact]
    public void EaArtMatchesExactTitle()
    {
        var art = EaScanner.Art("2006 FIFA World CupT");
        Assert.True(art.ContainsKey("grids"));
        Assert.StartsWith("https://", art["grids"]);
    }

    [Fact]
    public void EaArtRejectsUnknownTitle()
        => Assert.Empty(EaScanner.Art("ZZZ Definitely Not A Real EA Game 999"));

    [Fact]
    public void EaArtToleratesCaseAndPunctuation()
    {
        var art = EaScanner.Art("2006 fifa world cupt");
        Assert.True(art.ContainsKey("grids"));
    }
}
