using Sgm.Core.Steam;

namespace Sgm.Core.Tests;

public class SteamControlTests
{
    [Fact]
    public void SteamExePathNullWhenDirMissing()
    {
        Assert.Null(SteamControl.SteamExePath(null));
        Assert.Null(SteamControl.SteamExePath(""));
        Assert.Null(SteamControl.SteamExePath("   "));
    }

    [Fact]
    public void SteamExePathNullWhenExeAbsent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-steam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(SteamControl.SteamExePath(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SteamExePathResolvesWhenExePresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-steam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "steam.exe");
        File.WriteAllText(exe, "");
        try
        {
            Assert.Equal(exe, SteamControl.SteamExePath(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
