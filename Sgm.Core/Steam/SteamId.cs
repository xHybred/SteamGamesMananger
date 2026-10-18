using System.Globalization;

namespace Sgm.Core.Steam;

/// <summary>SteamID64 ↔ accountID32 conversion.</summary>
public static class SteamId
{
    public const long SteamId64Base = 76561197960265728L;

    /// <summary>Map a 64-bit SteamID to the userdata dir name (accountID32).</summary>
    public static string AccountIdFromSteamId64(string steamId64)
        => (long.Parse(steamId64, CultureInfo.InvariantCulture) - SteamId64Base)
            .ToString(CultureInfo.InvariantCulture);
}
