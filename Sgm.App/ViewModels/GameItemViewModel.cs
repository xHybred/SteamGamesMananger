using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Sgm.Core.Models;

namespace Sgm.App.ViewModels;

/// <summary>Stable identity for a discovered game across rescans.</summary>
public readonly record struct GameKey(string Source, string Name, string Exe);

/// <summary>One detected game, with its Steam/artwork status and cover image.</summary>
public sealed partial class GameItemViewModel : ObservableObject
{
    public GameInfo Game { get; }
    public GameKey Key { get; }

    public string Name => Game.Name;
    public string Source => Game.Source;
    public string SourceLabel => GameSource.Label(Game.Source);
    public string Exe => Game.Exe;

    [ObservableProperty] private bool _inSteam;
    [ObservableProperty] private bool _ownedOnSteam;
    [ObservableProperty] private string _artStatus = "";
    [ObservableProperty] private string? _coverPath;
    [ObservableProperty] private BitmapImage? _coverImage;

    public GameItemViewModel(GameInfo game)
    {
        Game = game;
        Key = new GameKey(game.Source, game.Name, game.Exe);
    }

    public bool IsNew => !InSteam;

    public string StatusText
    {
        get
        {
            var state = InSteam ? "In Steam" : OwnedOnSteam ? "Owned on Steam" : "New";
            return ArtStatus.Length > 0 ? $"{state} · {ArtStatus}" : state;
        }
    }

    public string DisplayName => Name.Length <= 34 ? Name : Name[..33] + "\u2026";

    partial void OnInSteamChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnOwnedOnSteamChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnArtStatusChanged(string value) => OnPropertyChanged(nameof(StatusText));

    partial void OnCoverPathChanged(string? value) => CoverImage = LoadImage(value);

    private static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 320;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
