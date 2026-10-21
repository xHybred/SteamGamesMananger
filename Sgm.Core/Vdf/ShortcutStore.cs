using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Sgm.Core.Models;
using Sgm.Core.Scanners.Xbox;

namespace Sgm.Core.Vdf;

/// <summary>Result counts from a merge operation.</summary>
public sealed record MergeStats(int Added, int Updated, int Skipped);

/// <summary>Safe load / merge / write for Steam's binary <c>shortcuts.vdf</c>.</summary>
public static partial class ShortcutStore
{
    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidRegex();

    /// <summary>Deserialize shortcuts.vdf. Corrupt files throw; missing → empty.</summary>
    public static Dictionary<string, object> Load(string path)
    {
        if (!File.Exists(path))
            return Empty();
        var raw = File.ReadAllBytes(path);
        if (raw.Length == 0)
            return Empty();

        Dictionary<string, object> data;
        try
        {
            data = BinaryVdf.Loads(raw);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Corrupted shortcuts.vdf at {path}: {ex.Message}", ex);
        }

        if (!data.TryGetValue("shortcuts", out var shortcutsObj) || shortcutsObj is not Dictionary<string, object>)
            throw new InvalidDataException($"Unexpected shortcuts.vdf structure at {path}: missing 'shortcuts'");

        return data;
    }

    public static byte[] Dump(Dictionary<string, object> data) => BinaryVdf.Dumps(data);

    public static Dictionary<string, object> Empty() => new() { ["shortcuts"] = new Dictionary<string, object>() };

    private static Dictionary<string, object> Shortcuts(Dictionary<string, object> data)
    {
        if (data.TryGetValue("shortcuts", out var s) && s is Dictionary<string, object> dict)
            return dict;
        var fresh = new Dictionary<string, object>();
        data["shortcuts"] = fresh;
        return fresh;
    }

    /// <summary>The (AppName, quoted Exe) pair used to match shortcuts, without building a full entry.</summary>
    public static (string Name, string Exe) NameExe(GameInfo game)
        => (game.Name, $"\"{UnquotedExe(game.Exe)}\"");

    private static string UnquotedExe(string exe)
    {
        exe = exe.Trim();
        if (exe.Length >= 2 && exe[0] == '"' && exe[^1] == '"')
            exe = exe[1..^1];
        return exe;
    }

    /// <summary>Build one shortcuts.vdf entry for a game.</summary>
    public static Dictionary<string, object> ToEntry(GameInfo game, int lastPlaytime = 0)
    {
        var exe = UnquotedExe(game.Exe);

        var tags = new Dictionary<string, object>();
        var sourceTags = game.Tags.Count > 0 ? game.Tags : new List<string> { game.Source };
        for (var i = 0; i < sourceTags.Count; i++)
            tags[i.ToString()] = sourceTags[i];

        return new Dictionary<string, object>
        {
            ["appid"] = AppIdMath.Signed(exe, game.Name),
            ["AppName"] = game.Name,
            ["Exe"] = $"\"{exe}\"",
            ["StartDir"] = string.IsNullOrEmpty(game.StartDir) ? "" : $"\"{game.StartDir}\"",
            ["icon"] = game.Icon,
            ["ShortcutPath"] = "",
            ["LaunchOptions"] = game.LaunchOptions,
            ["IsHidden"] = 0,
            ["AllowDesktopConfig"] = 1,
            ["AllowOverlay"] = 1,
            ["OpenVR"] = 0,
            ["Devkit"] = 0,
            ["DevkitGameID"] = "",
            ["DevkitOverrideAppID"] = 0,
            ["LastPlayTime"] = lastPlaytime,
            ["tags"] = tags,
        };
    }

    /// <summary>Map (AppName, Exe) → index key for dedup.</summary>
    public static Dictionary<(string Name, string Exe), string> ExistingKeys(Dictionary<string, object> data)
    {
        var result = new Dictionary<(string, string), string>();
        foreach (var (k, v) in Shortcuts(data))
        {
            if (v is Dictionary<string, object> entry)
            {
                var name = entry.TryGetValue("AppName", out var n) ? n?.ToString() ?? "" : "";
                var exe = entry.TryGetValue("Exe", out var e) ? e?.ToString() ?? "" : "";
                result[(name, exe)] = k;
            }
        }
        return result;
    }

    public static int NextIndex(Dictionary<string, object> data)
    {
        var best = -1;
        foreach (var k in Shortcuts(data).Keys)
        {
            if (int.TryParse(k, out var i))
                best = Math.Max(best, i);
        }
        return best + 1;
    }

    /// <summary>Append/update shortcuts in memory. Returns the merged stats.</summary>
    public static MergeStats MergeGames(Dictionary<string, object> data, IReadOnlyList<GameInfo> games, bool overwrite)
    {
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var lookup = ExistingKeys(data);
        var shortcuts = Shortcuts(data);
        var idx = NextIndex(data);

        foreach (var g in games)
        {
            g.Normalized();
            var key = (g.Name, $"\"{g.Exe}\"");
            if (lookup.TryGetValue(key, out var existingKey))
            {
                if (overwrite)
                {
                    shortcuts[existingKey] = ToEntry(g);
                    updated++;
                }
                else
                {
                    skipped++;
                }
                continue;
            }

            shortcuts[idx.ToString()] = ToEntry(g);
            idx++;
            added++;
        }

        return new MergeStats(added, updated, skipped);
    }

    /// <summary>Atomic write with timestamped backup. Returns the backup path (or null if new).</summary>
    public static string? Write(string path, Dictionary<string, object> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backup = BackupFile(path);
        var raw = Dump(data);
        var tmp = Path.ChangeExtension(path, ".vdf.tmp");
        File.WriteAllBytes(tmp, raw);
        File.Move(tmp, path, overwrite: true);
        return backup;
    }

    public static string? BackupFile(string path)
    {
        if (!File.Exists(path))
            return null;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var dest = Path.Combine(dir, $"{name}.vdf.bak-{stamp}");
        File.Copy(path, dest, overwrite: true);
        return dest;
    }

    public static uint GridAppIdFor(GameInfo game)
    {
        var exe = game.Exe.Trim().Trim('"');
        return AppIdMath.Unsigned(exe, game.Name);
    }

    /// <summary>True for UWP/OS junk the old Xbox scanner imported (GUID names, system families).</summary>
    public static bool IsSystemEntry(Dictionary<string, object> entry)
    {
        var name = entry.TryGetValue("AppName", out var n) ? n?.ToString()?.Trim() ?? "" : "";
        if (name.Length == 0 || GuidRegex().IsMatch(name))
            return true;
        return XboxRules.SystemPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
    }

    /// <summary>Remove system/UWP junk entries in memory. Returns the removed count.</summary>
    public static int PruneSystemEntries(Dictionary<string, object> data)
    {
        var shortcuts = Shortcuts(data);
        var doomed = shortcuts
            .Where(kv => kv.Value is Dictionary<string, object> e && IsSystemEntry(e))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in doomed)
            shortcuts.Remove(k);
        return doomed.Count;
    }

    /// <summary>Remove shortcuts matching the given games (by AppName + Exe). Returns removed count.</summary>
    public static int RemoveGames(Dictionary<string, object> data, IReadOnlyList<GameInfo> games)
    {
        var shortcuts = Shortcuts(data);
        var targets = new HashSet<(string Name, string Exe)>();
        foreach (var g in games)
        {
            g.Normalized();
            targets.Add((g.Name, $"\"{g.Exe}\""));
        }
        var doomed = shortcuts
            .Where(kv => kv.Value is Dictionary<string, object> e
                && targets.Contains((
                    e.TryGetValue("AppName", out var n) ? n?.ToString() ?? "" : "",
                    e.TryGetValue("Exe", out var ex) ? ex?.ToString() ?? "" : "")))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in doomed)
            shortcuts.Remove(k);
        return doomed.Count;
    }

    /// <summary>(AppName, Exe) pairs present in one shortcuts.vdf dict.</summary>
    public static HashSet<(string Name, string Exe)> InSteamSet(Dictionary<string, object> data)
    {
        var result = new HashSet<(string, string)>();
        foreach (var v in Shortcuts(data).Values)
        {
            if (v is Dictionary<string, object> entry)
            {
                var name = entry.TryGetValue("AppName", out var n) ? n?.ToString() ?? "" : "";
                var exe = entry.TryGetValue("Exe", out var e) ? e?.ToString() ?? "" : "";
                result.Add((name, exe));
            }
        }
        return result;
    }
}
