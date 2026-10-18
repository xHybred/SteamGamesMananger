using System.Collections.Generic;

namespace Sgm.Core.Models;

/// <summary>Launcher source ids, display labels and canonical display order.</summary>
public static class GameSource
{
    public const string Epic = "epic";
    public const string Gog = "gog";
    public const string Xbox = "xbox";
    public const string Ubisoft = "ubisoft";
    public const string Ea = "ea";
    public const string BattleNet = "battlenet";
    public const string Desktop = "desktop";

    public static readonly IReadOnlyList<string> Order = new[]
    {
        Epic, Gog, Xbox, Ubisoft, Ea, BattleNet, Desktop,
    };

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Epic] = "Epic",
        [Gog] = "GOG",
        [Xbox] = "Xbox",
        [Ubisoft] = "Ubisoft",
        [Ea] = "EA",
        [BattleNet] = "Battle.net",
        [Desktop] = "Desktop",
    };

    public static string Label(string source)
        => Labels.TryGetValue(source, out var l) ? l : source;

    public static readonly HashSet<string> Known = new(Order);
}
