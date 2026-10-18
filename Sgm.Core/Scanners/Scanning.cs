using System.Collections.Generic;
using Sgm.Core.Models;

namespace Sgm.Core.Scanners;

/// <summary>A launcher scanner: enumerates the games it can discover on this machine.</summary>
public interface IScanner
{
    string Name { get; }
    IReadOnlyList<GameInfo> Scan();
}

/// <summary>Shared de-duplication and filtering helpers.</summary>
public static class Scanning
{
    /// <summary>Drop exact (name, exe) duplicates, keeping the first occurrence.</summary>
    public static List<GameInfo> Dedupe(IEnumerable<GameInfo> games)
    {
        var seen = new HashSet<(string, string)>();
        var outGames = new List<GameInfo>();
        foreach (var g in games)
        {
            var key = (g.Name.ToLowerInvariant(), g.Exe.ToLowerInvariant());
            if (seen.Add(key))
                outGames.Add(g);
        }
        return outGames;
    }

    /// <summary>Same title from two storefronts: keep the first (scan order = priority).</summary>
    public static List<GameInfo> DedupeTitles(IEnumerable<GameInfo> games)
    {
        var seen = new HashSet<string>();
        var outGames = new List<GameInfo>();
        foreach (var g in Dedupe(games))
        {
            var key = Text.TitleNormalizer.Normalize(g.Name);
            if (seen.Add(key))
                outGames.Add(g);
        }
        return outGames;
    }

    public static List<GameInfo> Filter(IEnumerable<GameInfo> games, string? include)
    {
        if (string.IsNullOrEmpty(include))
            return games.ToList();
        var needle = include.ToLowerInvariant();
        return games.Where(g => g.Name.ToLowerInvariant().Contains(needle)).ToList();
    }
}
