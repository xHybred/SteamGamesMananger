using Sgm.Core.Configuration;

namespace Sgm.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void ProgramFilesPathIsDetected()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrEmpty(root))
            return;

        var install = Path.Combine(root, "Hybred Tools", "Steam Games Mananger");
        Assert.True(AppPaths.IsUnderProgramFiles(install));
    }

    [Fact]
    public void UserProfilePathIsNotProgramFiles()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamGamesManager");
        Assert.False(AppPaths.IsUnderProgramFiles(folder));
    }

    [Fact]
    public void CacheDirectoryIsCreated()
    {
        var dir = AppPaths.CacheDirectory($"sgm-test-cache-{Guid.NewGuid():N}");
        try
        {
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
