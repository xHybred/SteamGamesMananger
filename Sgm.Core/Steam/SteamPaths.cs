using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Sgm.Core.Steam;

/// <summary>Steam install / userdata / grid-directory resolution (Windows + Linux).</summary>
public static class SteamPaths
{
    private static readonly (RegistryHive Hive, string SubKey)[] SteamRegistryKeys =
    {
        (RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
    };

    /// <summary>Locate the Steam installation directory, or null when not found.</summary>
    public static string? FindInstallDir(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var p = ExpandPath(explicitPath);
            if (Directory.Exists(Path.Combine(p, "userdata")))
                return p;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, sub) in SteamRegistryKeys)
            {
                var found = RegistrySteamPath(hive, sub);
                if (found != null)
                    return found;
            }
        }

        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
            var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            candidates.Add(Path.Combine(pf86, "Steam"));
            candidates.Add(Path.Combine(pf, "Steam"));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, ".steam", "steam"));
            candidates.Add(Path.Combine(home, ".local", "share", "Steam"));
            candidates.Add("/usr/share/steam");
            candidates.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam",
                ".local", "share", "Steam"));
        }

        foreach (var c in candidates)
        {
            if (Directory.Exists(Path.Combine(c, "userdata")))
                return c;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? RegistrySteamPath(RegistryHive hive, string subKey)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey);
            if (key == null)
                return null;

            foreach (var value in new[] { "SteamPath", "InstallPath" })
            {
                if (key.GetValue(value) is string raw && raw.Length > 0)
                {
                    var p = ExpandPath(raw);
                    if (Directory.Exists(Path.Combine(p, "userdata")))
                        return p;
                }
            }
        }
        catch
        {
            // Missing key / access denied — treated as "not found".
        }

        return null;
    }

    public static List<string> UserDataDirs(string steamDir)
    {
        var baseDir = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(baseDir))
            return new List<string>();

        return Directory.EnumerateDirectories(baseDir)
            .Where(d => Path.GetFileName(d).All(char.IsDigit) && Path.GetFileName(d).Length > 0)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
    }

    public static string ConfigDir(string userDir)
    {
        var c = Path.Combine(userDir, "config");
        Directory.CreateDirectory(c);
        return c;
    }

    public static string GridDir(string userDir)
    {
        var g = Path.Combine(ConfigDir(userDir), "grid");
        Directory.CreateDirectory(g);
        return g;
    }

    public static string ShortcutsPath(string userDir)
        => Path.Combine(ConfigDir(userDir), "shortcuts.vdf");

    /// <summary>All numeric userdata dirs, or only the requested ids.</summary>
    public static List<string> ResolveAllUserDirs(string steamDir, IReadOnlyList<string>? userIds = null)
    {
        if (userIds != null && userIds.Count > 0)
        {
            var outDirs = new List<string>();
            foreach (var uid in userIds)
            {
                var cand = Path.Combine(steamDir, "userdata", uid);
                if (!Directory.Exists(cand))
                    throw new FileNotFoundException($"User dir not found: {cand}");
                outDirs.Add(cand);
            }
            return outDirs;
        }

        var dirs = UserDataDirs(steamDir);
        if (dirs.Count == 0)
            throw new FileNotFoundException($"No numeric userdata dirs under {Path.Combine(steamDir, "userdata")}");
        return dirs;
    }

    /// <summary>Expand %VARS% and a leading ~.</summary>
    public static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expanded.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = home + expanded[1..];
        }
        return Path.GetFullPath(expanded);
    }
}
