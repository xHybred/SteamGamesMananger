using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Sgm.Core.Models;
using Sgm.Core.Steam;

namespace Sgm.Core.Scanners;

/// <summary>
/// Epic Games Store scanner: reads <c>%PROGRAMDATA%\Epic\...\Manifests\*.item</c>
/// plus the launcher's base64 <c>catcache.bin</c> catalog art.
/// </summary>
public sealed class EpicScanner : IScanner
{
    private static readonly HashSet<string> SkipNamespaces = new(StringComparer.Ordinal) { "ue", "unrealengine" };
    private static readonly string[] SkipAppSuffixes = { "_dedicatedserver" };

    private readonly string? _manifests;
    private readonly string? _launcher;

    public string Name => GameSource.Epic;

    public EpicScanner(string? manifests = null, string? launcher = null)
    {
        _manifests = manifests;
        _launcher = launcher;
    }

    public static string ManifestsDir(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return SteamPaths.ExpandPath(explicitPath);
        var baseDir = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";
        return Path.Combine(baseDir, "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public static string LauncherExe(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;
        var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
        foreach (var rel in new[]
                 {
                     new[] { "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe" },
                     new[] { "Epic Games", "Launcher", "Portal", "Binaries", "Win32", "EpicGamesLauncher.exe" },
                 })
        {
            var cand = Path.Combine(new[] { pf86 }.Concat(rel).ToArray());
            if (File.Exists(cand))
                return cand;
        }
        return Path.Combine(pf86, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe");
    }

    public static string CatalogPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return SteamPaths.ExpandPath(explicitPath);
        var baseDir = Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";
        return Path.Combine(baseDir, "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
    }

    /// <summary>Map CatalogItemId → { grids, heroes } from the launcher's base64 JSON cache.</summary>
    public static Dictionary<string, Dictionary<string, string>> LoadCatalogArt(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        if (!File.Exists(path))
            return result;

        string text;
        try
        {
            text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return result;
        }

        JsonDocument doc;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(text));
            doc = JsonDocument.Parse(json);
        }
        catch (Exception)
        {
            return result;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("id", out var idEl) || string.IsNullOrEmpty(idEl.GetString()))
                    continue;

                var images = new Dictionary<string, string>();
                if (item.TryGetProperty("keyImages", out var keyImages) && keyImages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ki in keyImages.EnumerateArray())
                    {
                        if (ki.ValueKind != JsonValueKind.Object)
                            continue;
                        var type = ki.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        var url = ki.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                        if (type.Length > 0)
                            images[type] = url;
                    }
                }

                var art = new Dictionary<string, string>();
                if (images.TryGetValue("DieselGameBoxTall", out var tall))
                    art["grids"] = tall;
                else if (images.TryGetValue("DieselGameBox", out var box))
                    art["grids"] = box;
                if (images.TryGetValue("DieselGameBox", out var box2))
                    art["heroes"] = box2;
                if (art.Count > 0)
                    result[idEl.GetString()!] = art;
            }
        }

        return result;
    }

    private static bool IsGameManifest(JsonElement data)
    {
        var app = GetString(data, "AppName");
        if (app.Length == 0 || SkipAppSuffixes.Any(s => app.ToLowerInvariant().EndsWith(s, StringComparison.Ordinal)))
            return false;

        if (data.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array && cats.GetArrayLength() > 0)
        {
            var hasGames = cats.EnumerateArray().Any(c => c.GetString() == "games");
            if (!hasGames)
                return false;
        }

        var ns = GetString(data, "CatalogNamespace").ToLowerInvariant();
        if (SkipNamespaces.Contains(ns))
            return false;

        if (data.TryGetProperty("bIsApplication", out var isApp) && isApp.ValueKind == JsonValueKind.False)
            return false;

        if (GetString(data, "DisplayName").Length == 0 || GetString(data, "InstallLocation").Length == 0)
            return false;

        return true;
    }

    public static GameInfo? ParseManifest(string path, string launcher,
        Dictionary<string, Dictionary<string, string>>? catalog = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }

        using (doc)
        {
            var data = doc.RootElement;
            if (data.ValueKind != JsonValueKind.Object || !IsGameManifest(data))
                return null;

            var name = GetString(data, "DisplayName").Trim();
            var app = GetString(data, "AppName").Trim();
            var install = GetString(data, "InstallLocation").Trim();
            if (name.Length == 0 || app.Length == 0)
                return null;

            var art = new Dictionary<string, string>();
            var catalogId = GetString(data, "CatalogItemId");
            if (catalog != null && catalog.TryGetValue(catalogId, out var found))
                art = new Dictionary<string, string>(found);

            var game = new GameInfo
            {
                Name = name,
                Exe = launcher,
                StartDir = install,
                LaunchOptions = $"com.epicgames.launcher://apps/{app}?action=launch&silent=true",
                Source = GameSource.Epic,
                SourceId = app,
                Tags = { "Epic" },
            };
            foreach (var (k, v) in art)
                game.ArtUrls[k] = v;
            return game;
        }
    }

    private static string GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    public IReadOnlyList<GameInfo> Scan()
    {
        var mdir = ManifestsDir(_manifests);
        if (!Directory.Exists(mdir))
            return Array.Empty<GameInfo>();

        var launcher = LauncherExe(_launcher);
        var catalog = LoadCatalogArt(CatalogPath());
        var result = new List<GameInfo>();

        foreach (var file in Directory.EnumerateFiles(mdir, "*.item").OrderBy(f => f, StringComparer.Ordinal))
        {
            var g = ParseManifest(file, launcher, catalog);
            if (g == null)
                continue;
            result.Add(g);
        }

        return result;
    }
}
