using System.Text;

namespace Sgm.Core.Vdf;

/// <summary>
/// Canonical Steam non-Steam shortcut AppID calculation (CRC32 of exe+name, top bit set).
/// </summary>
public static class AppIdMath
{
    private static readonly uint[] CrcTable = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Unsigned 32-bit AppID used for grid filenames.</summary>
    public static uint Unsigned(string exe, string appName)
    {
        var crc = Crc32(exe + appName);
        return crc | 0x80000000u;
    }

    /// <summary>Signed 32-bit form stored in <c>shortcuts.vdf</c>.</summary>
    public static int Signed(string exe, string appName)
        => unchecked((int)Unsigned(exe, appName));
}
