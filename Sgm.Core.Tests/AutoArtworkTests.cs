using Sgm.Core.Artwork;

namespace Sgm.Core.Tests;

public class AutoArtworkTests
{
    private static Dictionary<string, string> Ki(string type, string url)
        => new() { ["type"] = type, ["url"] = url };

    [Fact]
    public void RankedUrlsPrefersListedTypeThenLargerArea()
    {
        var keyImages = new List<Dictionary<string, string>>
        {
            Ki("DieselGameBoxTall", "https://cdn/small_300x400.jpg"),
            Ki("DieselGameBoxTall", "https://cdn/big_600x900.jpg"),
            Ki("Unrelated", "https://cdn/other_9999x9999.jpg"),
        };

        var ranked = AutoArtwork.RankedUrls(keyImages, new[] { "DieselGameBoxTall" }, 600.0 / 900.0, extras: false);
        Assert.Equal("https://cdn/big_600x900.jpg", ranked[0]);
        Assert.Equal("https://cdn/small_300x400.jpg", ranked[1]);
        Assert.DoesNotContain("https://cdn/other_9999x9999.jpg", ranked);
    }

    [Fact]
    public void RankedUrlsSkipsVideosButUsesCoverParameter()
    {
        var keyImages = new List<Dictionary<string, string>>
        {
            Ki("herocarouselvideo", "https://cdn/trailer.mp4?cover=https%3A%2F%2Fcdn%2Fcover.jpg"),
            Ki("DieselStoreFrontWide", "https://cdn/wide_1920x620.jpg"),
        };

        var ranked = AutoArtwork.RankedUrls(keyImages, new[] { "DieselStoreFrontWide" }, 1920.0 / 620.0, extras: true);
        Assert.Equal("https://cdn/wide_1920x620.jpg", ranked[0]);
        Assert.Contains("https://cdn/cover.jpg", ranked);
    }

    [Fact]
    public void RankedUrlsWithoutExtrasOmitsVideosAndFeaturedMedia()
    {
        var keyImages = new List<Dictionary<string, string>>
        {
            Ki("herocarouselvideo", "https://cdn/trailer.mp4?cover=https%3A%2F%2Fcdn%2Fcover.jpg"),
            Ki("featuredMedia", "https://cdn/featured_1600x900.jpg"),
        };

        Assert.Empty(AutoArtwork.RankedUrls(keyImages, new[] { "DieselStoreFrontWide" }, 1.0, extras: false));
    }

    [Fact]
    public void RankedUrlsDeduplicatesCandidates()
    {
        var keyImages = new List<Dictionary<string, string>>
        {
            Ki("DieselGameBoxTall", "https://cdn/dup.jpg"),
            Ki("DieselGameBoxTall", "https://cdn/dup.jpg"),
        };
        Assert.Single(AutoArtwork.RankedUrls(keyImages, new[] { "DieselGameBoxTall" }, 1.0));
    }
}
