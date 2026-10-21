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

    /// <summary>
    /// One Battle.net product. <see cref="LauncherId"/> is the code accepted by
    /// <c>Battle.net.exe --exec="launch &lt;code&gt;"</c> and is empty when no code is
    /// documented; <see cref="ProductCode"/> feeds <c>battlenet://game/&lt;code&gt;</c>.
    /// </summary>
    public sealed record KnownGame(string Title, string ProductCode, string LauncherId);

    /// <summary>
    /// Installed Battle.net products keyed by product UID. Launch codes come from
    /// ShizCalev/Battlenet-Launcher, jayclassless/SteamBattleNetLauncher, dafzor/bnetlauncher
    /// (<c>gamesdb.ini</c>), Lutris' Battle.net definitions, and DLSS Swapper's Battle.net table.
    /// Products whose sources disagree (World of Warcraft Classic) fall back to the game page.
    /// </summary>
    public static readonly Dictionary<string, KnownGame> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rtro"] = new("Blizzard Arcade Collection", "rtro", "rtro"),
        ["auks"] = new("Call of Duty", "auks", "AUKS"),
        ["wlby"] = new("Crash Bandicoot 4: It's About Time", "wlby", "WLBY"),
        ["w1r"] = new("Warcraft I: Remastered", "w1r", "W1R"),
        ["diablo3"] = new("Diablo III", "d3", "D3"),
        ["aris"] = new("Doom: The Dark Ages", "aris", "ARIS"),
        ["heroes"] = new("Heroes of the Storm", "hero", "Hero"),
        ["d3cn"] = new("暗黑破壞神III", "d3cn", "D3CN"),
        ["aqua"] = new("Avowed", "aqua", "AQUA"),
        ["s2"] = new("StarCraft II", "s2", "S2"),
        ["w2"] = new("Warcraft II: Battle.net Edition", "w2bn", "W2"),
        ["fenris"] = new("Diablo IV", "fenris", "Fen"),
        ["d1"] = new("Diablo", "drtl", "D1"),
        ["scor"] = new("Sea of Thieves", "scor", "SCOR"),
        ["w3"] = new("Warcraft III: Reforged", "w3", "W3"),
        ["fore"] = new("Call of Duty: Vanguard", "fore", "FORE"),
        ["s1"] = new("StarCraft", "s1", "S1"),
        ["osi"] = new("Diablo II: Resurrected", "osi", "OSI"),
        ["lazarus"] = new("Call of Duty: MW2 Campaign Remastered", "lazarus", "LAZR"),
        ["odin"] = new("Call of Duty: Modern Warfare", "odin", "ODIN"),
        ["pinta"] = new("Call of Duty: Modern Warfare III", "pinta", "PNTA"),
        ["prometheus"] = new("Overwatch", "pro", "Pro"),
        ["viper"] = new("Call of Duty: Black Ops 4", "viper", "VIPR"),
        ["zeus"] = new("Call of Duty: Black Ops Cold War", "zeus", "ZEUS"),
        ["w1"] = new("Warcraft: Orcs & Humans", "war1", "W1"),
        ["w2r"] = new("Warcraft II Remastered", "w2r", "W2R"),
        ["hs_beta"] = new("Hearthstone", "hsb", "WTCG"),
        ["lbra"] = new("Tony Hawk's Pro Skater 3+4", "lbra", "LBRA"),
        ["ark"] = new("The Outer Worlds 2", "ark", "ARK"),
        ["nina"] = new("Call of Duty: Modern Warfare II", "nina", "NINA"),
        ["wow"] = new("World of Warcraft", "wow", "WoW"),
        // Sources disagree (WoWC / wow_classic / WoW_wow_classic), so open the game page instead.
        ["wow_classic"] = new("World of Warcraft Classic", "wow_classic", ""),
        ["wow_classic_era"] = new("World of Warcraft Classic Era", "wow_classic_era", "wow_classic_era"),
    };

    private static readonly string[] IgnoredPrefixes =
    {
        "agent", "beta", "battle.net",
    };

    /// <summary>WoW branches that are shippable products; every other <c>wow*</c> UID is PTR/beta.</summary>
    private static readonly HashSet<string> WorldOfWarcraftUids = new(StringComparer.OrdinalIgnoreCase)
    {
        "wow", "wow_classic", "wow_classic_era",
    };

    public static bool IsIgnoredUid(string uid)
    {
        if (IgnoredPrefixes.Any(p => uid.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (uid.StartsWith("wow", StringComparison.OrdinalIgnoreCase))
            return !WorldOfWarcraftUids.Contains(uid);
        return false;
    }

    /// <summary>
    /// Builds the client arguments for an installed product: a documented launch code starts the
    /// game, a product code opens the game's Battle.net page, and neither launches the client.
    /// </summary>
    public static string LaunchOptionsFor(string uid, string productCode)
    {
        KnownGames.TryGetValue(uid, out var known);
        if (known is { LauncherId.Length: > 0 })
            return $"--exec=\"launch {known.LauncherId}\"";

        var code = productCode.Trim();
        if (code.Length == 0)
            code = known?.ProductCode ?? "";
        return code.Length > 0 ? $"--uri=\"battlenet://game/{code}\"" : "";
    }

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

    /// <summary>Aggregate <c>installed[]</c> entry: artwork URL and display name.</summary>
    public sealed record AggregateItem(string Logo, string Name);

    public static Dictionary<string, AggregateItem> AggregateCatalog(string agentDir)
    {
        var result = new Dictionary<string, AggregateItem>();
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
                if (!item.TryGetProperty("product_id", out var pid))
                    continue;
                var id = pid.ToString();
                if (id.Length == 0)
                    continue;
                var logo = item.TryGetProperty("logo_art_uri", out var art) ? art.ToString() : "";
                var name = item.TryGetProperty("name", out var display) ? display.ToString() : "";
                result[id] = new AggregateItem(logo, name);
            }
        }
        catch
        {
            // ignore
        }
        return result;
    }

    public static Dictionary<string, string> AggregateLogos(string agentDir)
        => AggregateCatalog(agentDir)
            .Where(pair => pair.Value.Logo.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Logo);

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

        var catalog = AggregateCatalog(Path.GetDirectoryName(db)!);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            if (!seen.Add(info.Value.Uid))
                continue;

            var folder = info.Value.InstallPath.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder))
                continue;

            KnownGames.TryGetValue(info.Value.Uid, out var known);
            var productCode = info.Value.ProductCode.Trim();
            if (productCode.Length == 0)
                productCode = known?.ProductCode ?? "";

            catalog.TryGetValue(productCode, out var aggregate);
            var title = known?.Title;
            if (string.IsNullOrWhiteSpace(title))
                title = aggregate?.Name;
            if (string.IsNullOrWhiteSpace(title))
                title = new DirectoryInfo(folder).Name;

            var cover = aggregate is { Logo.Length: > 0 }
                ? aggregate.Logo
                : string.Format(HostedCover, info.Value.Uid);

            var g = new GameInfo
            {
                Name = title!,
                Exe = client,
                StartDir = folder,
                LaunchOptions = LaunchOptionsFor(info.Value.Uid, productCode),
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
