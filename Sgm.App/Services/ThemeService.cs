using System.Windows;

namespace Sgm.App.Services;

/// <summary>Swaps the active palette dictionary in place so DynamicResource picks up new brushes.</summary>
public static class ThemeService
{
    private const string SteamPath = "Themes/Palette.Steam.xaml";
    private const string GrayPath = "Themes/Palette.Gray.xaml";

    public static string Current { get; private set; } = "steam";

    public static string Next(string theme) => theme == "steam" ? "gray" : "steam";

    public static void Apply(string theme)
    {
        var normalized = theme == "gray" ? "gray" : "steam";
        var uri = new Uri(normalized == "gray" ? GrayPath : SteamPath, UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source != null && d.Source.OriginalString.Contains("Palette.", StringComparison.Ordinal));

        var replacement = new ResourceDictionary { Source = uri };
        if (existing != null)
            dictionaries[dictionaries.IndexOf(existing)] = replacement;
        else
            dictionaries.Insert(0, replacement);

        Current = normalized;
    }
}
