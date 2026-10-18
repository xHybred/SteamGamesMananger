using Sgm.Core.Vdf;

namespace Sgm.Core.Tests;

public class KeyValuesParserTests
{
    [Fact]
    public void ParsesNestedObjectsAndStrings()
    {
        var text = "\"libraryfolders\"\n{\n" +
                   "\t\"0\"\n\t{\n" +
                   "\t\t\"path\"\t\t\"C:\\\\Games\\\\Steam\"\n" +
                   "\t\t\"label\"\t\t\"\"\n\t}\n}\n";
        var data = KeyValuesParser.Parse(text);
        var folders = Assert.IsType<Dictionary<string, object>>(data["libraryfolders"]);
        var zero = Assert.IsType<Dictionary<string, object>>(folders["0"]);
        Assert.Equal(@"C:\Games\Steam", zero["path"]);
    }
}
