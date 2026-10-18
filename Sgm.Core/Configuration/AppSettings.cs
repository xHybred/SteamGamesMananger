using System.Collections.Generic;
using Sgm.Core.Models;

namespace Sgm.Core.Configuration;

/// <summary>Persistent GUI/CLI settings.</summary>
public sealed class AppSettings
{
    public Dictionary<string, bool> Sources { get; set; } = DefaultSources();
    public string View { get; set; } = "list";
    public string Density { get; set; } = "compact";
    public bool Artwork { get; set; } = true;
    public bool Overwrite { get; set; } = true;
    public bool Force { get; set; }
    public string ApiKey { get; set; } = "";
    public List<string[]> Hidden { get; set; } = new();
    public string Theme { get; set; } = "steam";

    public static Dictionary<string, bool> DefaultSources() => new()
    {
        [GameSource.Epic] = true,
        [GameSource.Gog] = true,
        [GameSource.Xbox] = false,
        [GameSource.Ubisoft] = true,
        [GameSource.Ea] = true,
        [GameSource.BattleNet] = true,
        [GameSource.Desktop] = true,
    };

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.Sources = new Dictionary<string, bool>(Sources);
        clone.Hidden = Hidden.Select(h => h.ToArray()).ToList();
        return clone;
    }
}
