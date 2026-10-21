using System.Collections.Generic;
using Sgm.Core.Models;
using Sgm.Core.Vdf;

namespace Sgm.Core.Tests;

public class ShortcutStoreTests
{
    private static GameInfo Game(string name = "Demo", string exe = @"C:\demo.exe")
        => new() { Name = name, Exe = exe, StartDir = @"C:\", Source = GameSource.Epic };

    [Fact]
    public void MergeDedupesWithoutOverwriteThenUpdates()
    {
        var data = ShortcutStore.Empty();
        var g = Game();

        var s1 = ShortcutStore.MergeGames(data, new[] { g }, overwrite: false);
        Assert.Equal(1, s1.Added);

        var s2 = ShortcutStore.MergeGames(data, new[] { g }, overwrite: false);
        Assert.Equal(1, s2.Skipped);
        Assert.Single((Dictionary<string, object>)data["shortcuts"]);

        var s3 = ShortcutStore.MergeGames(data, new[] { g }, overwrite: true);
        Assert.Equal(1, s3.Updated);
    }

    [Fact]
    public void InSteamSetReadsNameExePairs()
    {
        var data = new Dictionary<string, object>
        {
            ["shortcuts"] = new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["AppName"] = "Demo", ["Exe"] = @"""C:\demo.exe""" },
            },
        };
        var set = ShortcutStore.InSteamSet(data);
        Assert.Contains(("Demo", @"""C:\demo.exe"""), set);
    }

    [Fact]
    public void PruneRemovesOnlySystemEntries()
    {
        var data = new Dictionary<string, object>
        {
            ["shortcuts"] = new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["AppName"] = "Dead Island 2", ["Exe"] = @"""C:\x.exe""" },
                ["1"] = new Dictionary<string, object> { ["AppName"] = "Microsoft.LockApp", ["Exe"] = @"""C:\Windows\explorer.exe""" },
                ["2"] = new Dictionary<string, object> { ["AppName"] = "1527c705-839a-4832-9118-54d4bd6a0c89", ["Exe"] = @"""C:\Windows\explorer.exe""" },
            },
        };

        var removed = ShortcutStore.PruneSystemEntries(data);
        Assert.Equal(2, removed);
        Assert.Equal(new[] { "0" }, ((Dictionary<string, object>)data["shortcuts"]).Keys.ToArray());
    }

    [Fact]
    public void RemoveGamesRemovesMatchingShortcuts()
    {
        var data = ShortcutStore.Empty();
        var g = Game();
        ShortcutStore.MergeGames(data, new[] { g }, overwrite: false);
        var other = Game("Other", @"C:\other.exe");
        ShortcutStore.MergeGames(data, new[] { other }, overwrite: false);

        var removed = ShortcutStore.RemoveGames(data, new[] { g });
        Assert.Equal(1, removed);
        var shortcuts = (Dictionary<string, object>)data["shortcuts"];
        Assert.Single(shortcuts);
        var entry = (Dictionary<string, object>)shortcuts["1"];
        Assert.Equal("Other", entry["AppName"]);
    }

    [Fact]
    public void WriteAndLoadRoundTripWithBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sgm-user-{Guid.NewGuid():N}");
        var userDir = Path.Combine(root, "userdata", "999");
        Directory.CreateDirectory(userDir);
        try
        {
            var path = Steam.SteamPaths.ShortcutsPath(userDir);
            var data = ShortcutStore.Empty();
            ShortcutStore.MergeGames(data, new[] { Game() }, overwrite: false);
            var firstBackup = ShortcutStore.Write(path, data);
            Assert.Null(firstBackup); // new file
            Assert.True(File.Exists(path));

            var loaded = ShortcutStore.Load(path);
            Assert.Single((Dictionary<string, object>)loaded["shortcuts"]);

            var secondBackup = ShortcutStore.Write(path, loaded);
            Assert.NotNull(secondBackup);
            Assert.True(File.Exists(secondBackup));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
