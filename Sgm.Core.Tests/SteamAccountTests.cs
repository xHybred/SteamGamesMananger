using Sgm.Core.Steam;

namespace Sgm.Core.Tests;

public class SteamAccountTests
{
    [Fact]
    public void AccountIdMappingKnownValues()
    {
        Assert.Equal("410010172", SteamId.AccountIdFromSteamId64("76561198370275900"));
        Assert.Equal("1172768277", SteamId.AccountIdFromSteamId64("76561199133034005"));
        Assert.Equal("1152391232", SteamId.AccountIdFromSteamId64("76561199112656960"));
    }

    [Fact]
    public void LoginUsersParsingAndMostRecent()
    {
        var steam = Path.Combine(Path.GetTempPath(), $"sgm-steam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        File.WriteAllText(Path.Combine(steam, "config", "loginusers.vdf"),
            "\"users\"\n{\n" +
            "\t\"76561198370275900\"\n\t{\n" +
            "\t\t\"AccountName\"\t\t\"kingownagexv\"\n" +
            "\t\t\"PersonaName\"\t\t\"Hybred\"\n" +
            "\t\t\"Timestamp\"\t\t\"1792051172\"\n\t}\n" +
            "\t\"76561199133034005\"\n\t{\n" +
            "\t\t\"AccountName\"\t\t\"thyhybred\"\n" +
            "\t\t\"PersonaName\"\t\t\"xHybred\"\n" +
            "\t\t\"Timestamp\"\t\t\"1791679260\"\n\t}\n}\n");

        try
        {
            var users = SteamUsers.ReadLoginUsers(steam);
            Assert.Equal(new HashSet<string> { "410010172", "1172768277" }, users.Select(u => u.AccountId).ToHashSet());
            Assert.Equal("410010172", SteamUsers.MostRecentAccountId(steam));
        }
        finally
        {
            Directory.Delete(steam, recursive: true);
        }
    }

    [Fact]
    public void ResolveAllUserDirsHonoursRequestedIds()
    {
        var steam = Path.Combine(Path.GetTempPath(), $"sgm-steam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "111", "config"));
        Directory.CreateDirectory(Path.Combine(steam, "userdata", "222", "config"));
        try
        {
            Assert.Equal(new[] { "111", "222" },
                SteamPaths.ResolveAllUserDirs(steam).Select(Path.GetFileName).ToArray());
            Assert.Equal(new[] { "222" },
                SteamPaths.ResolveAllUserDirs(steam, new[] { "222" }).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            Directory.Delete(steam, recursive: true);
        }
    }
}
