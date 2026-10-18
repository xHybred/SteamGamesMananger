using System.Text.RegularExpressions;

namespace Sgm.Core.Text;

/// <summary>Comparison-friendly title / name normalization shared by scanners and UI.</summary>
public static partial class TitleNormalizer
{
    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();

    /// <summary>Lowercase, alphanumeric words only.</summary>
    public static string Normalize(string title)
        => string.Join(' ', WordRegex().Matches(title.Trim().ToLowerInvariant()).Select(m => m.Value));
}
