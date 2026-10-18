using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>
/// Ubisoft Connect games from HKLM install records, with cover/logo art parsed
/// from the launcher's binary configurations cache.
/// </summary>
public static partial class UbisoftScanner
{
    public const string AssetsBase = "https://ubistatic3-a.akamaihd.net/orbit/uplay_launcher_3_0/assets/";

    private static readonly string[] ExcludedExeTokens =
    {
        "setup", "unins", "uninstall", "crash", "redist", "helper", "cleanup",
        "dxsetup", "vc_redist", "_be", "eac", "bootstrap", "updater", "launcher",
        "sen_launcher", "eaanticheat", "installer",
    };

    private static readonly (string Field, string Kind)[] ArtFields =
    {
        ("thumb_image", "grids"),
        ("logo_image", "logos"),
    };

    [GeneratedRegex(@"^\s*([A-Za-z_]+):\s*(\S.*?)\s*$", RegexOptions.Multiline)]
    private static partial Regex FieldRegex();

    /// <summary>Largest plausible game executable directly inside the folder.</summary>
    public static string? PickMainExe(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        try
        {
            var candidates = Directory.EnumerateFiles(folder, "*.exe")
                .Where(p => !ExcludedExeTokens.Any(t => Path.GetFileName(p).ToLowerInvariant().Contains(t)))
                .ToList();
            if (candidates.Count == 0)
                return null;
            return candidates.OrderByDescending(p => new FileInfo(p).Length).First();
        }
        catch
        {
            return null;
        }
    }

    private static int ConvertValue(int value)
    {
        if (value > 256 * 256)
        {
            value -= 128 * 256 * (int)Math.Ceiling(value / (256.0 * 256.0));
            value -= 128 * (int)Math.Ceiling(value / 256.0);
        }
        else if (value > 256)
        {
            value -= 128 * (int)Math.Ceiling(value / 256.0);
        }
        return value;
    }

    private static (int Size, int InstallId, int Launch2, int Head) Header(byte[] buf, int start, bool second)
    {
        try
        {
            int offset = start + 1, mult = 1, size = 0, tmp = 0;

            if (second)
            {
                while (buf[offset] != 0x08 || (buf[offset] == 0x08 && buf[offset + 1] == 0x08))
                {
                    size += buf[offset] * mult;
                    mult *= 256;
                    offset++;
                    tmp++;
                }
            }
            else
            {
                while (buf[offset] != 0x08 || size == 0)
                {
                    size += buf[offset] * mult;
                    mult *= 256;
                    offset++;
                    tmp++;
                }
            }

            size = ConvertValue(size);
            offset += 1;

            int multL = 1, launch = 0;
            while (buf[offset] != 0x10 || buf[offset + 1] == 0x10)
            {
                launch += buf[offset] * multL;
                multL *= 256;
                offset++;
            }
            launch = ConvertValue(launch);
            offset += 1;

            int mult2 = 1, launch2 = 0;
            while (buf[offset] != 0x1A || (buf[offset] == 0x1A && buf[offset + 1] == 0x1A))
            {
                launch2 += buf[offset] * mult2;
                mult2 *= 256;
                offset++;
            }
            launch2 = ConvertValue(launch2);

            if (size - offset < 128 && size >= 120)
            {
                tmp -= 1;
                size += 1;
            }

            return (size - offset, launch, launch2, offset + tmp + 1);
        }
        catch (IndexOutOfRangeException)
        {
            return (0, 0, 0, 10);
        }
    }

    /// <summary>(install_id, offset, size) per YAML record in the configurations file.</summary>
    public static List<(int InstallId, int Offset, int Size)> Records(byte[] data)
    {
        var records = new List<(int, int, int)>();
        var pos = 0;
        while (pos < data.Length)
        {
            var (size, installId, _, head) = Header(data, pos, second: false);
            if (size > 500)
                records.Add((installId, pos + head, size));

            var before = pos;
            pos += size + head;
            if (pos < data.Length && data[pos] != 0x0A)
            {
                var (size2, _, _, head2) = Header(data, pos, second: true);
                pos = before + size2 + head2;
            }
        }
        return records;
    }

    /// <summary>CDN URL for one image field, resolving a bare key via the record's localizations.</summary>
    public static string Asset(string blob, string field)
    {
        Match? match = null;
        foreach (Match m in FieldRegex().Matches(blob))
        {
            if (m.Groups[1].Value == field)
            {
                match = m;
                break;
            }
        }
        if (match == null)
            return "";

        var value = match.Groups[2].Value.Trim().Trim('\'', '"');
        if (value.ToLowerInvariant().EndsWith(".jpg") || value.ToLowerInvariant().EndsWith(".png"))
            return AssetsBase + value;

        var loc = Regex.Match(blob, $"^\\s+{Regex.Escape(value)}:\\s*(\\S+)\\s*$", RegexOptions.Multiline);
        return loc.Success ? AssetsBase + loc.Groups[1].Value.Trim().Trim('\'', '"') : "";
    }

    /// <summary>install_id → art URLs by kind, from Ubisoft Connect's local configurations cache.</summary>
    public static Dictionary<int, Dictionary<string, string>> Art()
    {
        var result = new Dictionary<int, Dictionary<string, string>>();
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
        var local = Path.Combine(localAppData, "Ubisoft Game Launcher", "cache", "configuration", "configurations");
        if (!File.Exists(local))
            return result;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(local);
        }
        catch
        {
            return result;
        }

        foreach (var (installId, offset, size) in Records(data))
        {
            if (result.ContainsKey(installId))
                continue;
            if (offset < 0 || offset + size > data.Length)
                continue;
            var blob = Encoding.UTF8.GetString(data, offset, size);
            var art = new Dictionary<string, string>();
            foreach (var (field, kind) in ArtFields)
            {
                var url = Asset(blob, field);
                if (url.Length > 0)
                    art[kind] = url;
            }
            if (art.Count > 0)
                result[installId] = art;
        }

        return result;
    }

    /// <summary>Normalizes a registry InstallDir (forward slashes, trailing separator) or null.</summary>
    public static string? NormalizeInstallDir(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;
        try
        {
            var normalized = Path.GetFullPath(value);
            var trimmed = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length >= 3 ? trimmed : normalized;
        }
        catch
        {
            return null;
        }
    }

    public static List<GameInfo> Scan()
    {
        var result = new List<GameInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var artById = Art();

        foreach (var row in RegistryReader.Values(@"SOFTWARE\Ubisoft\Launcher\Installs", "InstallDir"))
        {
            var raw = row.GetValueOrDefault("InstallDir", "");
            var folder = NormalizeInstallDir(raw);
            if (folder == null || !Directory.Exists(folder))
                continue;
            if (!seenDirs.Add(folder))
                continue;

            var name = Path.GetFileName(folder).Trim();
            if (name.EndsWith(" TS", StringComparison.OrdinalIgnoreCase))
                continue; // Ubisoft's Test Server build of the same game

            var exe = PickMainExe(folder);
            if (exe == null)
                continue;

            var g = new GameInfo
            {
                Name = name,
                Exe = exe,
                StartDir = folder,
                Source = GameSource.Ubisoft,
                SourceId = row.GetValueOrDefault("_key", ""),
                Tags = { "Ubisoft" },
            };
            var key = row.GetValueOrDefault("_key", "");
            if (int.TryParse(key, out var installId) && artById.TryGetValue(installId, out var art))
                foreach (var (k, v) in art)
                    g.ArtUrls[k] = v;

            result.Add(g);
        }

        return result;
    }
}
