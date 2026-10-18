using Sgm.Core.Vdf;

namespace Sgm.Core.Tests;

public class AppIdMathTests
{
    [Fact]
    public void TopBitIsSetAndMatchesCrcVector()
    {
        var exe = @"C:\Games\Foo\foo.exe";
        var name = "Foo";

        // Reference implementation: crc32(exe+name) | 0x80000000.
        var crc = Crc32Of(exe + name);
        var expected = crc | 0x80000000u;

        Assert.Equal(expected, AppIdMath.Unsigned(exe, name));
        Assert.True((AppIdMath.Unsigned(exe, name) & 0x80000000u) != 0);

        var signed = AppIdMath.Signed(exe, name);
        Assert.Equal(expected >= 0x80000000u ? unchecked((int)expected) : (int)expected, signed);
    }

    [Fact]
    public void GridAppIdUsesUnquotedExe()
    {
        var a = AppIdMath.Unsigned(@"C:\x.exe", "X");
        var b = AppIdMath.Unsigned(@"""C:\x.exe""", "X");
        Assert.NotEqual(a, b); // quoting changes the hash; callers must strip quotes first
    }

    private static uint Crc32Of(string text)
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
