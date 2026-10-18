using System.Collections.Generic;

namespace Sgm.Core.Models;

/// <summary>
/// A game discovered in a third-party launcher, including local
/// store-cache artwork hints.
/// </summary>
public sealed class GameInfo
{
    public required string Name { get; set; }
    public required string Exe { get; set; }
    public string StartDir { get; set; } = "";
    public string LaunchOptions { get; set; } = "";
    public string Icon { get; set; } = "";

    /// <summary>epic | gog | xbox | ubisoft | ea | battlenet | desktop</summary>
    public string Source { get; set; } = "unknown";

    /// <summary>AppName / gameId / PackageFullName.</summary>
    public string SourceId { get; set; } = "";

    public List<string> Tags { get; init; } = new();

    /// <summary>
    /// Artwork already on disk from the store's own cache, keyed by kind
    /// ("grids", "heroes", "logos", "icons", "squares"). Used before any online lookup.
    /// </summary>
    public Dictionary<string, string> ArtUrls { get; init; } = new();

    /// <summary>Trim the fields normalized before comparison / writing.</summary>
    public GameInfo Normalized()
    {
        Name = NormalizeWhitespace(Name);
        Exe = Unquote(Exe);
        StartDir = Unquote(StartDir);
        LaunchOptions = LaunchOptions.Trim();
        return this;
    }

    private static string NormalizeWhitespace(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));

    private static string Unquote(string value)
    {
        var v = value.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            v = v[1..^1];
        return v;
    }
}
