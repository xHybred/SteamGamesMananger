using System.Collections.Generic;
using Sgm.Core.Vdf;

namespace Sgm.Core.Tests;

public class BinaryVdfTests
{
    [Fact]
    public void RoundTripsShortcutShape()
    {
        var data = new Dictionary<string, object>
        {
            ["shortcuts"] = new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["appid"] = -1392444581,
                    ["AppName"] = "Foo",
                    ["Exe"] = @"""C:\Games\Foo\foo.exe""",
                    ["StartDir"] = @"""C:\Games\Foo""",
                    ["icon"] = "",
                    ["ShortcutPath"] = "",
                    ["LaunchOptions"] = "com.epicgames.launcher://apps/Foo?action=launch&silent=true",
                    ["IsHidden"] = 0,
                    ["AllowDesktopConfig"] = 1,
                    ["AllowOverlay"] = 1,
                    ["OpenVR"] = 0,
                    ["LastPlayTime"] = 0,
                    ["tags"] = new Dictionary<string, object> { ["0"] = "Epic" },
                },
            },
        };

        var bytes = BinaryVdf.Dumps(data);
        var back = BinaryVdf.Loads(bytes);
        AssertEquivalent(data, back);
    }

    [Fact]
    public void EmptyInputYieldsEmptyDict()
    {
        Assert.Empty(BinaryVdf.Loads(Array.Empty<byte>()));
    }

    [Fact]
    public void UnterminatedStringThrows()
    {
        var bytes = new byte[] { 0x01, (byte)'k', (byte)'e', (byte)'y', 0x00, (byte)'v' };
        Assert.Throws<InvalidDataException>(() => BinaryVdf.Loads(bytes));
    }

    private static void AssertEquivalent(Dictionary<string, object> expected, Dictionary<string, object> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (key, value) in expected)
        {
            Assert.True(actual.ContainsKey(key), $"missing key {key}");
            switch (value)
            {
                case Dictionary<string, object> nested:
                    AssertEquivalent(nested, Assert.IsType<Dictionary<string, object>>(actual[key]));
                    break;
                case int i:
                    Assert.Equal(i, actual[key]);
                    break;
                default:
                    Assert.Equal(value.ToString(), actual[key]?.ToString());
                    break;
            }
        }
    }
}
