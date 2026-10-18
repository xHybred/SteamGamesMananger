using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sgm.Core.Steam;

/// <summary>A login user parsed from <c>loginusers.vdf</c>.</summary>
public sealed record SteamLoginUser(
    string SteamId64,
    string AccountId,
    string Account,
    string Persona,
    string Recent,
    string Timestamp);

/// <summary>Parses Steam's <c>config/loginusers.vdf</c> text VDF.</summary>
public static partial class SteamUsers
{
    [GeneratedRegex("\"(\\d{17})\"\\s*\\{(.*?)\\n\\s*\\}", RegexOptions.Singleline)]
    private static partial Regex UserBlockRegex();

    public static List<SteamLoginUser> ReadLoginUsers(string steamDir)
    {
        var path = Path.Combine(steamDir, "config", "loginusers.vdf");
        var users = new List<SteamLoginUser>();
        if (!File.Exists(path))
            return users;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return users;
        }

        foreach (Match m in UserBlockRegex().Matches(text))
        {
            var sid = m.Groups[1].Value;
            var body = m.Groups[2].Value;

            string Value(string key)
            {
                var mm = Regex.Match(body, $"\"{Regex.Escape(key)}\"\\s+\"(.*?)\"");
                return mm.Success ? mm.Groups[1].Value : "";
            }

            users.Add(new SteamLoginUser(
                sid,
                SteamId.AccountIdFromSteamId64(sid),
                Value("AccountName"),
                Value("PersonaName"),
                Value("MostRecent"),
                Value("Timestamp")));
        }

        return users;
    }

    /// <summary>Best-effort current account: MostRecent flag, else highest login Timestamp.</summary>
    public static string? MostRecentAccountId(string steamDir)
    {
        var users = ReadLoginUsers(steamDir);
        if (users.Count == 0)
            return null;

        var flagged = users.Where(u => u.Recent == "1").ToList();
        if (flagged.Count > 0)
            return flagged[0].AccountId;

        return users
            .OrderByDescending(u => long.TryParse(u.Timestamp, out var ts) ? ts : 0)
            .First()
            .AccountId;
    }
}
