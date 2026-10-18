using System.Collections.Generic;
using System.IO;
using Sgm.Core.Logging;
using Sgm.Core.Models;
using Sgm.Core.Steam;
using Sgm.Core.Vdf;

namespace Sgm.Core.Artwork;

/// <summary>Aggregate result of fetching artwork for one account.</summary>
public sealed record ArtworkResult(int Ok, int Fail, Dictionary<uint, Dictionary<string, string>> Cache);

/// <summary>
/// Per-account artwork orchestration: SteamGridDB (keyed) first, keyless providers
/// filling any missing kinds, then the shortcut icon is back-patched.
/// </summary>
public static class ArtworkService
{
    private static readonly string[] AllKinds = { "grids", "heroes", "logos", "icons" };

    public static ArtworkResult ForAccount(
        SteamGridDbClient? client,
        IReadOnlyList<GameInfo> games,
        string userDir,
        Dictionary<uint, Dictionary<string, string>>? predownloaded = null,
        Dictionary<string, int>? overrides = null)
    {
        var grid = SteamPaths.GridDir(userDir);
        var cache = predownloaded != null
            ? new Dictionary<uint, Dictionary<string, string>>(predownloaded)
            : new Dictionary<uint, Dictionary<string, string>>();
        overrides ??= new Dictionary<string, int>();
        var ok = 0;
        var fail = 0;

        foreach (var g in games)
        {
            var appid = ShortcutStore.GridAppIdFor(g);
            try
            {
                Dictionary<string, string> saved;
                if (cache.TryGetValue(appid, out var cached) && !overrides.ContainsKey(g.Name) && cached.Count >= 4)
                {
                    saved = new Dictionary<string, string>();
                    foreach (var (kind, src) in cached)
                    {
                        var dest = Path.Combine(grid, Path.GetFileName(src));
                        if (!string.Equals(dest, src, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Copy(src, dest, overwrite: true); }
                            catch (IOException) { }
                        }
                        saved[kind] = dest;
                    }
                }
                else
                {
                    saved = new Dictionary<string, string>();
                    if (client != null)
                    {
                        saved = SteamGridDbArtwork.Fetch(client, g.Name, appid, grid,
                            overrides.TryGetValue(g.Name, out var oid) ? oid : null);
                        cache[appid] = new Dictionary<string, string>(saved);
                    }

                    var missing = new HashSet<string>(AllKinds.Except(saved.Keys));
                    if (missing.Count > 0)
                    {
                        var auto = AutoArtwork.Fetch(g.Name, g.Source, appid, grid, missing, g.ArtUrls);
                        foreach (var (k, v) in auto.Saved)
                        {
                            saved[k] = v;
                            if (!cache.TryGetValue(appid, out var entry))
                            {
                                entry = new Dictionary<string, string>();
                                cache[appid] = entry;
                            }
                            entry[k] = v;
                        }
                    }
                }

                if (saved.ContainsKey("icons"))
                    BackpatchIcon(userDir, g.Name, saved["icons"]);

                if (saved.Count > 0)
                    ok++;
                else
                    fail++;
            }
            catch (SteamGridDbException ex)
            {
                AppLog.Warn($"{g.Name}: {ex.Message}");
                fail++;
            }
        }

        return new ArtworkResult(ok, fail, cache);
    }

    private static void BackpatchIcon(string userDir, string gameName, string iconPath)
    {
        try
        {
            var path = SteamPaths.ShortcutsPath(userDir);
            var data = ShortcutStore.Load(path);
            if (data.TryGetValue("shortcuts", out var s) && s is Dictionary<string, object> shortcuts)
            {
                foreach (var v in shortcuts.Values)
                {
                    if (v is Dictionary<string, object> entry
                        && entry.TryGetValue("AppName", out var n) && n?.ToString() == gameName)
                    {
                        entry["icon"] = iconPath;
                        break;
                    }
                }
            }
            ShortcutStore.Write(path, data);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not back-patch icon for '{gameName}': {ex.Message}");
        }
    }

    /// <summary>Which artwork kinds already exist on disk for a game in any of the accounts.</summary>
    public static List<string> ExistingKinds(GameInfo game, IReadOnlyList<string> userDirs)
    {
        var kinds = new List<string>();
        var appid = ShortcutStore.GridAppIdFor(game);
        foreach (var userDir in userDirs)
        {
            var grid = SteamPaths.GridDir(userDir);
            if (File.Exists(Path.Combine(grid, $"{appid}p.jpg")))
                kinds.Add("grids");
            if (File.Exists(Path.Combine(grid, $"{appid}_hero.jpg")))
                kinds.Add("heroes");
            if (File.Exists(Path.Combine(grid, $"{appid}_logo.png")))
                kinds.Add("logos");
            if (File.Exists(Path.Combine(grid, $"{appid}.ico")))
                kinds.Add("icons");
            if (kinds.Count > 0)
                break;
        }
        return kinds;
    }
}
