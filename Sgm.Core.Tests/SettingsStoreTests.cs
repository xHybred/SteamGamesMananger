using Sgm.Core.Configuration;
using Sgm.Core.Models;

namespace Sgm.Core.Tests;

public class SettingsStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"sgm-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void DefaultsAreExpected()
    {
        var d = new AppSettings();
        Assert.Equal(new Dictionary<string, bool>
        {
            [GameSource.Epic] = true,
            [GameSource.Gog] = true,
            [GameSource.Xbox] = false,
            [GameSource.Ubisoft] = true,
            [GameSource.Ea] = true,
            [GameSource.BattleNet] = true,
            [GameSource.Desktop] = true,
        }, d.Sources);
        Assert.True(d.Overwrite);
        Assert.Equal("list", d.View);
        Assert.Equal("compact", d.Density);
        Assert.Equal("steam", d.Theme);
    }

    [Fact]
    public void ThemeRoundTripAndBadValueFallsBackToSteam()
    {
        var path = TempFile();
        try
        {
            var store = new SettingsStore(path);
            var state = new AppSettings { Theme = "gray" };
            store.Save(state);
            Assert.Equal("gray", store.Load().Theme);

            store.Save(new AppSettings { Theme = "neon" });
            Assert.Equal("steam", store.Load().Theme);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RoundTripAndHardening()
    {
        var path = TempFile();
        try
        {
            var store = new SettingsStore(path);
            var state = new AppSettings();
            state.Sources[GameSource.Epic] = false;
            state.Density = "normal";
            state.ApiKey = "abc";
            state.Hidden.Add(new[] { "xbox", "VP9", "exe" });
            store.Save(state);

            var back = store.Load();
            Assert.False(back.Sources[GameSource.Epic]);
            Assert.False(back.Sources[GameSource.Xbox]);
            Assert.Equal("normal", back.Density);
            Assert.Equal("abc", back.ApiKey);
            Assert.Single(back.Hidden);
            Assert.Equal(new[] { "xbox", "VP9", "exe" }, back.Hidden[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingAndCorruptFilesReturnDefaults()
    {
        var missing = TempFile();
        Assert.True(new SettingsStore(missing).Load().Sources[GameSource.Gog]);

        var bad = TempFile();
        try
        {
            File.WriteAllText(bad, "{not json");
            var loaded = new SettingsStore(bad).Load();
            Assert.Equal("list", loaded.View);
            Assert.Empty(loaded.Hidden);

            File.WriteAllText(bad, "{\"view\": \"-coverflow-\", \"hidden\": [[\"a\"]]}");
            Assert.Equal("list", new SettingsStore(bad).Load().View);
        }
        finally
        {
            File.Delete(bad);
        }
    }
}
