using System.Collections.Generic;
using System.IO;
using Sgm.Core.Text;
using Sgm.Core.Vdf;

namespace Sgm.Core.Steam;

/// <summary>
/// Titles already installed in the user's Steam libraries, read from
/// <c>libraryfolders.vdf</c> + each <c>appmanifest_*.acf</c>.
/// </summary>
public static class SteamLibrary
{
    private static readonly HashSet<string> NonGameAppIds = new() { "228980" };

    /// <summary>All steamapps folders: the main one plus any extra libraries.</summary>
    public static List<string> LibraryRoots(string steamDir)
    {
        var baseDir = Path.Combine(steamDir, "steamapps");
        var roots = new List<string> { baseDir };
        var vdfPath = Path.Combine(baseDir, "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return roots;

        Dictionary<string, object> data;
        try
        {
            data = KeyValuesParser.ParseFile(vdfPath);
        }
        catch
        {
            return roots;
        }

        if (!data.TryGetValue("libraryfolders", out var foldersObj) || foldersObj is not Dictionary<string, object> folders)
            return roots;

        foreach (var entry in folders.Values)
        {
            if (entry is Dictionary<string, object> lib && lib.TryGetValue("path", out var pathObj) && pathObj is string path)
            {
                var extra = Path.Combine(path, "steamapps");
                if (Directory.Exists(extra) && !roots.Contains(extra, System.StringComparer.OrdinalIgnoreCase))
                    roots.Add(extra);
            }
        }

        return roots;
    }

    /// <summary>Normalized names of games installed in any Steam library.</summary>
    public static HashSet<string> InstalledTitles(string? steamDir)
    {
        var titles = new HashSet<string>();
        if (string.IsNullOrEmpty(steamDir) || !Directory.Exists(steamDir))
            return titles;

        foreach (var root in LibraryRoots(steamDir))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var acf in Directory.EnumerateFiles(root, "appmanifest_*.acf"))
            {
                Dictionary<string, object> data;
                try
                {
                    data = KeyValuesParser.ParseFile(acf);
                }
                catch
                {
                    continue;
                }

                if (!data.TryGetValue("AppState", out var stateObj) || stateObj is not Dictionary<string, object> state)
                    continue;

                var appId = state.TryGetValue("appid", out var idObj) ? idObj?.ToString() ?? "" : "";
                var name = state.TryGetValue("name", out var nameObj) ? nameObj?.ToString()?.Trim() ?? "" : "";
                if (name.Length == 0 || NonGameAppIds.Contains(appId))
                    continue;

                titles.Add(TitleNormalizer.Normalize(name));
            }
        }

        return titles;
    }
}
