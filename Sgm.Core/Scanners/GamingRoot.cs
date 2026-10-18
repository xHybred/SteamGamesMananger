using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>
/// Xbox / Game Pass (GDK) titles installed into a drive's ".GamingRoot" folder,
/// each described by <c>Content\MicrosoftGame.config</c>.
/// </summary>
public static class GamingRoot
{
    public static bool TestOverrideEnabled
        => Environment.GetEnvironmentVariable("SGM_TEST_GAMING_ROOT") != null;

    /// <summary>For test injection; production walks every drive for a .GamingRoot marker.</summary>
    public static Func<IReadOnlyList<string>> TargetProvider { get; set; } = DefaultTargets;

    private static List<string> DefaultTargets()
    {
        var targets = new List<string>();
        if (!OperatingSystem.IsWindows())
            return targets;

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = $"{letter}:\\";
            var marker = Path.Combine(drive, ".GamingRoot");
            byte[] data;
            try
            {
                if (!File.Exists(marker))
                    continue;
                data = File.ReadAllBytes(marker);
            }
            catch
            {
                continue;
            }

            if (data.Length <= 5 || data[0] != (byte)'R' || data[1] != (byte)'G' || data[2] != (byte)'B' || data[3] != (byte)'X')
                continue;

            var rel = new string(data.Skip(5).Where(b => b != 0).Select(b => (char)b).ToArray()).Trim();
            if (rel.Length == 0)
                continue;

            var target = drive + rel;
            if (Directory.Exists(target))
                targets.Add(target);
        }

        return targets;
    }

    private static string LocalName(XElement el) => el.Name.LocalName;

    /// <summary>Return (display_name, executable_path) or null if not a usable game.</summary>
    public static (string Name, string Exe)? ParseGameConfig(string configPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath);
        }
        catch (Exception)
        {
            return null;
        }

        var root = doc.Root;
        if (root == null || LocalName(root) != "Game")
            return null;

        var display = root.Descendants()
            .FirstOrDefault(el => LocalName(el) == "ShellVisuals")
            ?.Attribute("DefaultDisplayName")?.Value.Trim() ?? "";

        var exeName = root.Descendants()
            .FirstOrDefault(el => LocalName(el) == "Executable")
            ?.Attribute("Name")?.Value.Trim() ?? "";

        if (exeName.Length == 0)
            return null;

        var exe = Path.Combine(Path.GetDirectoryName(configPath)!, exeName);
        if (!File.Exists(exe))
            return null;

        return (display.Length > 0 ? display : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(configPath)!)!)!,
                exe);
    }

    public static string IdentityName(string configPath)
    {
        try
        {
            var doc = XDocument.Load(configPath);
            return doc.Root?.Descendants()
                .FirstOrDefault(el => LocalName(el) == "Identity")
                ?.Attribute("Name")?.Value.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Art files named in MicrosoftGame.config, resolved against the game folder.</summary>
    public static Dictionary<string, string> ConfigArt(string configPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath);
        }
        catch
        {
            return new Dictionary<string, string>();
        }

        var shell = doc.Root?.Descendants().FirstOrDefault(el => LocalName(el) == "ShellVisuals");
        if (shell == null)
            return new Dictionary<string, string>();

        var baseDir = Path.GetDirectoryName(configPath)!;

        string Existing(string key)
        {
            var rel = shell.Attribute(key)?.Value.Trim() ?? "";
            var full = rel.Length > 0 ? Path.Combine(baseDir, rel) : "";
            return full.Length > 0 && File.Exists(full) ? full : "";
        }

        var square = Existing("Square480x480Logo");
        if (square.Length == 0)
            square = Existing("Square150x150Logo");
        var splash = Existing("SplashScreenImage");

        var art = new Dictionary<string, string>();
        if (square.Length > 0)
            art["squares"] = square;
        else if (splash.Length > 0)
            art["grids"] = splash;
        if (splash.Length > 0)
            art["heroes"] = splash;

        return art;
    }

    /// <summary>Local art keyed by package identity name, for titles the AppX scan lists.</summary>
    public static Dictionary<string, Dictionary<string, string>> GamingRootArt()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        if (!OperatingSystem.IsWindows() && !TestOverrideEnabled)
            return result;

        foreach (var root in TargetProvider())
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var gameDir in entries)
            {
                var config = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                if (!File.Exists(config))
                    continue;
                var identity = IdentityName(config).ToLowerInvariant();
                var art = ConfigArt(config);
                if (identity.Length > 0 && art.Count > 0)
                    result[identity] = art;
            }
        }

        return result;
    }

    /// <summary>Unpackaged GDK games; identities in <paramref name="skipNames"/> are left out.</summary>
    public static List<GameInfo> ScanGamingRoots(IReadOnlySet<string>? skipNames = null)
    {
        var result = new List<GameInfo>();
        if (!OperatingSystem.IsWindows() && !TestOverrideEnabled)
            return result;

        var skip = new HashSet<string>((skipNames ?? new HashSet<string>()).Select(n => n.ToLowerInvariant()));

        foreach (var root in TargetProvider())
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateDirectories(root);
            }
            catch
            {
                continue;
            }

            foreach (var gameDir in entries)
            {
                var config = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                if (!File.Exists(config))
                    continue;
                var parsed = ParseGameConfig(config);
                if (parsed == null)
                    continue;
                if (skip.Contains(IdentityName(config).ToLowerInvariant()))
                    continue;

                var g = new GameInfo
                {
                    Name = parsed.Value.Name,
                    Exe = parsed.Value.Exe,
                    StartDir = Path.GetDirectoryName(config)!,
                    LaunchOptions = "",
                    Source = GameSource.Xbox,
                    SourceId = Path.GetFileName(gameDir),
                    Tags = { "Xbox", "Game Pass" },
                };
                foreach (var (k, v) in ConfigArt(config))
                    g.ArtUrls[k] = v;
                result.Add(g);
            }
        }

        return result;
    }
}
