using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Sgm.Core.Models;
using Sgm.Core.Text;
using Sgm.Core.Utilities;

namespace Sgm.Core.Scanners;

/// <summary>
/// EA App games from Uninstall records holding an EAInstaller / Cleanup.exe marker,
/// with art from the bundled title list (then the exe's own icon).
/// </summary>
public static class EaScanner
{
    private static readonly Lazy<List<JsonElement>> Catalog = new(LoadCatalog);

    private static List<JsonElement> LoadCatalog()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("ea_app_titles.json", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            return new List<JsonElement>();

        try
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<JsonElement>();
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch
        {
            return new List<JsonElement>();
        }
    }

    private static string CatalogUrl(JsonElement entry, string key)
    {
        if (entry.TryGetProperty(key, out var obj) && obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
        {
            var value = pathEl.GetString() ?? "";
            return value.StartsWith("http", StringComparison.Ordinal) ? value : "";
        }
        return "";
    }

    /// <summary>Cover art from the title list: exact title first, then closest at 60% or more.</summary>
    public static Dictionary<string, string> Art(string name)
    {
        var want = TitleNormalizer.Normalize(name);
        if (want.Length == 0)
            return new Dictionary<string, string>();

        JsonElement? best = null;
        var bestScore = 0.0;
        foreach (var entry in Catalog.Value)
        {
            var title = TitleNormalizer.Normalize(entry.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "");
            if (title.Length == 0)
                continue;
            if (title == want)
            {
                best = entry;
                bestScore = 1.0;
                break;
            }
            var score = Similarity(want, title);
            if (score > bestScore)
            {
                best = entry;
                bestScore = score;
            }
        }

        if (best == null || bestScore < 0.6)
            return new Dictionary<string, string>();

        var art = new Dictionary<string, string>();
        var pack = CatalogUrl(best.Value, "packArtImage");
        var key = CatalogUrl(best.Value, "keyArtImage");
        var logo = CatalogUrl(best.Value, "logoImage");
        if (pack.Length > 0 || key.Length > 0)
            art["grids"] = pack.Length > 0 ? pack : key;
        if (key.Length > 0)
            art["heroes"] = key;
        if (logo.Length > 0)
            art["logos"] = logo;
        return art;
    }

    private static double Similarity(string a, string b)
    {
        // Simple normalized Levenshtein ratio (difflib.SequenceMatcher equivalent enough for title matching).
        if (a.Length == 0 && b.Length == 0)
            return 1.0;
        var max = Math.Max(a.Length, b.Length);
        if (max == 0)
            return 1.0;
        return 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static Dictionary<string, string> ExeIconArt(string exe)
    {
        var sha = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(exe));
        var hash = Convert.ToHexString(sha).ToLowerInvariant();
        var dest = Path.Combine(Path.GetTempPath(), "sgm_icons", $"{hash}.png");
        return ExeIconExtractor.ExtractPng(exe, dest)
            ? new Dictionary<string, string> { ["icons"] = dest }
            : new Dictionary<string, string>();
    }

    public static List<GameInfo> Scan()
    {
        var result = new List<GameInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var row in RegistryReader.Values(uninstall, "DisplayName", "InstallLocation"))
        {
            var name = row.GetValueOrDefault("DisplayName", "").Trim();
            var folder = row.GetValueOrDefault("InstallLocation", "").Trim().Trim('"');
            if (name.Length == 0 || folder.Length == 0 || !Directory.Exists(folder))
                continue;

            var lowered = name.ToLowerInvariant();
            if (lowered.Contains("ea app") || lowered.Contains("ea desktop") || lowered.Contains("origin"))
                continue; // the launcher itself

            if (!Directory.Exists(Path.Combine(folder, "EAInstaller")) && !File.Exists(Path.Combine(folder, "Cleanup.exe")))
                continue;

            var exe = UbisoftScanner.PickMainExe(folder);
            if (exe == null)
                continue;

            var art = Art(name);
            if (art.Count == 0)
                art = ExeIconArt(exe);

            var g = new GameInfo
            {
                Name = name,
                Exe = exe,
                StartDir = folder,
                Source = GameSource.Ea,
                SourceId = row.GetValueOrDefault("_key", ""),
                Tags = { "EA" },
            };
            foreach (var (k, v) in art)
                g.ArtUrls[k] = v;
            result.Add(g);
        }

        return result;
    }
}
