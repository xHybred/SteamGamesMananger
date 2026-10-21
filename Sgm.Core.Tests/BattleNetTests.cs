using System.Text;
using Sgm.Core.Scanners;

namespace Sgm.Core.Tests;

public class BattleNetTests
{
    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                b |= 0x80;
            bytes.Add(b);
        } while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] Key(int number, int wire) => Varint((ulong)((number << 3) | wire));

    private static byte[] LenField(int number, byte[] data)
        => Key(number, 2).Concat(Varint((ulong)data.Length)).Concat(data).ToArray();

    private static byte[] VarintField(int number, ulong value)
        => Key(number, 0).Concat(Varint(value)).ToArray();

    private static byte[] Installation(string uid, string productCode, string installPath, bool installed)
    {
        var settings = LenField(1, Encoding.UTF8.GetBytes(installPath));
        var baseState = VarintField(1, installed ? 1UL : 0UL);
        var state = LenField(1, baseState);
        return LenField(1, Encoding.UTF8.GetBytes(uid))
            .Concat(LenField(2, Encoding.UTF8.GetBytes(productCode)))
            .Concat(LenField(3, settings))
            .Concat(LenField(4, state))
            .ToArray();
    }

    [Fact]
    public void ParseInstallReadsUidPathAndInstalledFlag()
    {
        var blob = Installation("fenris", "fen", @"C:\Games\Diablo IV", installed: true);
        var parsed = BattleNetScanner.ParseInstall(blob);
        Assert.NotNull(parsed);
        Assert.Equal("fenris", parsed!.Value.Uid);
        Assert.Equal("fen", parsed.Value.ProductCode);
        Assert.True(parsed.Value.Installed);
        Assert.Equal(@"C:\Games\Diablo IV", parsed.Value.InstallPath);
    }

    [Fact]
    public void UninstalledFlagIsReported()
    {
        var blob = Installation("fenris", "fen", @"C:\Games\Diablo IV", installed: false);
        Assert.False(BattleNetScanner.ParseInstall(blob)!.Value.Installed);
    }

    [Fact]
    public void ProductDbTopLevelEnumeratesInstallations()
    {
        var db = Installation("fenris", "fen", @"C:\a", true)
            .Concat(Installation("w3", "w3", @"C:\b", true)).ToArray();
        var installs = BattleNetScanner.Fields(db).Where(f => f.Number == 1 && f.Wire == 2).ToList();
        Assert.Equal(2, installs.Count);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("agent_beta")]
    [InlineData("battle.net")]
    [InlineData("wow_beta")]
    [InlineData("wowt")]
    [InlineData("wow_classic_ptr")]
    public void IgnoredUidsAreDetected(string uid)
        => Assert.True(BattleNetScanner.IsIgnoredUid(uid));

    [Theory]
    [InlineData("wow")]
    [InlineData("wow_classic")]
    [InlineData("wow_classic_era")]
    [InlineData("fenris")]
    [InlineData("brand_new")]
    public void ShippableUidsAreNotIgnored(string uid)
        => Assert.False(BattleNetScanner.IsIgnoredUid(uid));

    [Fact]
    public void KnownGamesTableHasCoreTitles()
    {
        Assert.Equal("Diablo IV", BattleNetScanner.KnownGames["fenris"].Title);
        Assert.Equal("fenris", BattleNetScanner.KnownGames["fenris"].ProductCode);
        Assert.Equal("rtro", BattleNetScanner.KnownGames["rtro"].LauncherId);
        Assert.Equal("WoW", BattleNetScanner.KnownGames["wow"].LauncherId);
    }

    [Fact]
    public void ConflictingLaunchCodesFallBackToGamePage()
    {
        Assert.Equal("", BattleNetScanner.KnownGames["wow_classic"].LauncherId);
        Assert.Equal("--uri=\"battlenet://game/wow_classic\"", BattleNetScanner.LaunchOptionsFor("wow_classic", "wow_classic"));
    }

    [Fact]
    public void DocumentedLaunchCodeUsesExec()
        => Assert.Equal("--exec=\"launch Fen\"", BattleNetScanner.LaunchOptionsFor("fenris", "fenris"));

    [Fact]
    public void DocumentedLaunchCodeWinsOverProductCode()
        => Assert.Equal("--exec=\"launch WoW\"", BattleNetScanner.LaunchOptionsFor("wow", "wow"));

    [Fact]
    public void UnknownProductUsesGamePage()
        => Assert.Equal("--uri=\"battlenet://game/fenris\"", BattleNetScanner.LaunchOptionsFor("fenris-unknown", "fenris"));

    [Fact]
    public void UnknownProductWithoutCodeLaunchesClient()
        => Assert.Equal("", BattleNetScanner.LaunchOptionsFor("fenris-unknown", ""));

    [Fact]
    public void AggregateCatalogReadsNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-bn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "aggregate.json"),
                "{\"installed\":[{\"product_id\":\"brand_new\",\"name\":\"Brand New Game\"}]}");
            var catalog = BattleNetScanner.AggregateCatalog(dir);
            Assert.Equal("Brand New Game", catalog["brand_new"].Name);
            Assert.Empty(BattleNetScanner.AggregateLogos(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AggregateLogosReadsProductIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sgm-bn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "aggregate.json"),
                "{\"installed\":[{\"product_id\":\"fenris\",\"logo_art_uri\":\"https://cdn/d4.png\"}]}");
            var logos = BattleNetScanner.AggregateLogos(dir);
            Assert.Equal("https://cdn/d4.png", logos["fenris"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
