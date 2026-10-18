using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>
/// Battle.net games from the agent's protobuf <c>product.db</c> plus the client's
/// uninstall record and <c>aggregate.json</c> logos.
/// </summary>
public static class BattleNetScanner
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net";
    private const string HostedCover = "https://dlss-swapper-downloads.beeradmoore.com/images/covers/battlenet/{0}.webp";

    public static readonly Dictionary<string, (string LauncherId, string Title)> KnownGames = new()
    {
        ["rtro"] = ("rtro", "Blizzard Arcade Collection"),
        ["auks"] = ("auks", "Call of Duty"),
        ["wlby"] = ("wlby", "Crash Bandicoot 4: It's About Time"),
        ["w1r"] = ("w1r", "Warcraft I: Remastered"),
        ["diablo3"] = ("d3", "Diablo III"),
        ["aris"] = ("aris", "Doom: The Dark Ages"),
        ["heroes"] = ("hero", "Heroes of the Storm"),
        ["d3cn"] = ("d3cn", "暗黑破壞神III"),
        ["aqua"] = ("aqua", "Avowed"),
        ["s2"] = ("s2", "StarCraft II"),
        ["w2"] = ("w2bn", "Warcraft II: Battle.net Edition"),
        ["fenris"] = ("fenris", "Diablo IV"),
        ["d1"] = ("drtl", "Diablo"),
        ["scor"] = ("scor", "Sea of Thieves"),
        ["w3"] = ("w3", "Warcraft III: Reforged"),
        ["fore"] = ("fore", "Call of Duty: Vanguard"),
        ["s1"] = ("s1", "StarCraft"),
        ["osi"] = ("osi", "Diablo II: Resurrected"),
        ["lazarus"] = ("lazr", "Call of Duty: MW2 Campaign Remastered"),
        ["odin"] = ("odin", "Call of Duty: Modern Warfare"),
        ["pinta"] = ("pinta", "Call of Duty: Modern Warfare III"),
        ["prometheus"] = ("pro", "Overwatch"),
        ["viper"] = ("viper", "Call of Duty: Black Ops 4"),
        ["zeus"] = ("zeus", "Call of Duty: Black Ops Cold War"),
        ["w1"] = ("war1", "Warcraft: Orcs & Humans"),
        ["w2r"] = ("w2r", "Warcraft II Remastered"),
        ["hs_beta"] = ("hsb", "Hearthstone"),
        ["lbra"] = ("lbra", "Tony Hawk's Pro Skater 3+4"),
        ["ark"] = ("ark", "The Outer Worlds 2"),
        ["nina"] = ("nina", "Call of Duty: Modern Warfare II"),
    };

    private static readonly string[] IgnoredPrefixes =
    {
        "agent", "agent_beta", "beta", "battle.net", "wow",
    };

    public static bool IsIgnoredUid(string uid)
        => IgnoredPrefixes.Any(p => uid.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static ulong ReadVarint(byte[] buf, ref int pos)
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = buf[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
    }

    /// <summary>Yield (field_number, wire_type, value) for one protobuf message.</summary>
    public static IEnumerable<(int Number, int Wire, object Value)> Fields(byte[] buf)
    {
        var pos = 0;
        while (pos < buf.Length)
        {
            var key = ReadVarint(buf, ref pos);
            var number = (int)(key >> 3);
            var wire = (int)(key & 0x7);
            object value;
            switch (wire)
            {
                case 0:
                    value = ReadVarint(buf, ref pos);
                    break;
                case 1:
                    value = buf[pos..(pos + 8)];
                    pos += 8;
                    break;
                case 2:
                    var length = (int)ReadVarint(buf, ref pos);
                    value = buf[pos..(pos + length)];
                    pos += length;
                    break;
                case 5:
                    value = buf[pos..(pos + 4)];
                    pos += 4;
                    break;
                default:
                    throw new InvalidDataException($"unsupported wire type {wire}");
            }
            yield return (number, wire, value);
        }
    }

    private static object? First(byte[] buf, int number)
    {
        foreach (var (n, _, value) in Fields(buf))
            if (n == number)
                return value;
        return null;
    }

    public static (string Uid, string ProductCode, bool Installed, string InstallPath)? ParseInstall(byte[] raw)
    {
        if (First(raw, 1) is not byte[] uid
            || First(raw, 3) is not byte[] settings
            || First(raw, 4) is not byte[] state)
            return null;

        var installed = false;
        if (First(state, 1) is byte[] baseState && First(baseState, 1) is ulong flag)
            installed = flag != 0;

        var installPath = First(settings, 1) as byte[] ?? Array.Empty<byte>();
        var productCode = First(raw, 2) as byte[] ?? Array.Empty<byte>();

        return (Encoding.UTF8.GetString(uid),
                Encoding.UTF8.GetString(productCode),
                installed,
                Encoding.UTF8.GetString(installPath));
    }

    public static Dictionary<string, string> AggregateLogos(string agentDir)
    {
        var result = new Dictionary<string, string>();
        var path = Path.Combine(agentDir, "aggregate.json");
        if (!File.Exists(path))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("installed", out var installed) || installed.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in installed.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (item.TryGetProperty("product_id", out var pid) && item.TryGetProperty("logo_art_uri", out var logo))
                    result[pid.ToString()] = logo.ToString();
            }
        }
        catch
        {
            // ignore
        }
        return result;
    }

    public static string ProductDbPath()
    {
        var baseDir = Environment.GetEnvironmentVariable("ALLUSERSPROFILE")
                      ?? Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        return Path.Combine(baseDir, "Battle.net", "Agent", "product.db");
    }

    [SupportedOSPlatform("windows")]
    public static string ClientExe()
    {
        if (!OperatingSystem.IsWindows())
            return "";
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.OpenSubKey(UninstallKey);
            if (key?.GetValue("InstallLocation")?.ToString() is not string location)
                return "";
            var exe = Path.Combine(location.Trim().Trim('"'), "Battle.net.exe");
            return File.Exists(exe) ? exe : "";
        }
        catch
        {
            return "";
        }
    }

    public static List<GameInfo> Scan()
    {
        var result = new List<GameInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        var db = ProductDbPath();
        if (!File.Exists(db))
            return result;

        var client = ClientExe();
        if (client.Length == 0)
            return result;

        byte[] raw;
        try
        {
            using var stream = new FileStream(db, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            raw = ms.ToArray();
        }
        catch (IOException)
        {
            return result;
        }

        var logos = AggregateLogos(Path.GetDirectoryName(db)!);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        List<byte[]> installs;
        try
        {
            installs = Fields(raw).Where(f => f.Number == 1 && f.Wire == 2).Select(f => (byte[])f.Value).ToList();
        }
        catch (Exception)
        {
            return result;
        }

        foreach (var blob in installs)
        {
            (string Uid, string ProductCode, bool Installed, string InstallPath)? info;
            try
            {
                info = ParseInstall(blob);
            }
            catch (Exception)
            {
                continue;
            }

            if (info == null || !info.Value.Installed || IsIgnoredUid(info.Value.Uid))
                continue;
            if (!seen.Add(info.Value.Uid) || !KnownGames.ContainsKey(info.Value.Uid))
                continue;

            var folder = info.Value.InstallPath.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder))
                continue;

            var (launcherId, title) = KnownGames[info.Value.Uid];
            var cover = logos.TryGetValue(info.Value.ProductCode, out var logo)
                ? logo
                : string.Format(HostedCover, info.Value.Uid);

            var g = new GameInfo
            {
                Name = title,
                Exe = client,
                StartDir = folder,
                LaunchOptions = $"--exec=\"launch {launcherId}\"",
                Source = GameSource.BattleNet,
                SourceId = info.Value.Uid,
                Tags = { "Battle.net" },
            };
            g.ArtUrls["grids"] = cover;
            result.Add(g);
        }

        return result;
    }
}
