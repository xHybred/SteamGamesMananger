using CommunityToolkit.Mvvm.ComponentModel;

namespace Sgm.App.ViewModels;

/// <summary>Static help content shown on the Help page.</summary>
public sealed class HelpViewModel : ObservableObject
{
    public string Text { get; } =
        "1. Close Steam (the header pill tells you when it is safe).\n" +
        "2. Press 'Add All', or add games one by one from the Library.\n" +
        "3. 'Match Art' locks a game to the right artwork entry.\n" +
        "4. Without a SteamGridDB key, local caches and Epic / GOG / Steam store\n" +
        "   art are used; with a key, SteamGridDB art takes priority.\n" +
        "5. Restart Steam to see shortcuts and artwork.\n\n" +
        "Each write backs up shortcuts.vdf (shortcuts.vdf.bak-<timestamp>) when it\n" +
        "already exists; every run is logged to SteamGamesManager.log.";

    public string Limitations { get; } =
        "Notes\n" +
        "\u2022 Packaged Xbox / Game Pass titles launch through explorer.exe shell:AppsFolder entries;\n" +
        "  unpackaged GDK titles launch their executable directly.\n" +
        "\u2022 Ubisoft and EA games launch their own main executable directly.\n" +
        "\u2022 Battle.net titles need the Battle.net client installed.\n" +
        "\u2022 Battle.net titles with a documented launch code start the game directly; others open the game's page in the Battle.net client.\n" +
        "\u2022 Steam must be closed before writing shortcuts.vdf (or enable the force option).";

    public string About { get; } =
        "Steam Games Manager: import Epic/GOG/Xbox/Ubisoft/EA/Battle.net games\n" +
        "into Steam with artwork.";
}
