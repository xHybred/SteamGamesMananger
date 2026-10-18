using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sgm.App.Services;
using Sgm.Core.Artwork;
using Sgm.Core.Logging;

namespace Sgm.App.ViewModels;

/// <summary>Settings view: SteamGridDB key, write options, and hidden-game management.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _keyState = "";
    [ObservableProperty] private bool _downloadArtwork = true;
    [ObservableProperty] private bool _overwriteShortcuts = true;
    [ObservableProperty] private bool _writeWhileSteamRunning;
    [ObservableProperty] private string _hiddenCountText = "0 Hidden Game(s)";

    public SettingsViewModel(MainViewModel shell)
    {
        _shell = shell;
        _apiKey = shell.Settings.ApiKey;
        _downloadArtwork = shell.Settings.Artwork;
        _overwriteShortcuts = shell.Settings.Overwrite;
        _writeWhileSteamRunning = shell.Settings.Force;
    }

    partial void OnApiKeyChanged(string value)
    {
        _shell.Settings.ApiKey = value.Trim();
        _shell.SaveSettings();
    }

    partial void OnDownloadArtworkChanged(bool value)
    {
        _shell.Settings.Artwork = value;
        _shell.SaveSettings();
    }

    partial void OnOverwriteShortcutsChanged(bool value)
    {
        _shell.Settings.Overwrite = value;
        _shell.SaveSettings();
    }

    partial void OnWriteWhileSteamRunningChanged(bool value)
    {
        _shell.Settings.Force = value;
        _shell.SaveSettings();
    }

    public void SetHiddenCount(int count) => HiddenCountText = $"{count} Hidden Game(s)";

    [RelayCommand]
    private async Task ValidateAsync()
    {
        var key = _shell.ApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            KeyState = "Enter a key first";
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var client = new SteamGridDbClient(key);
                client.Validate();
            });
            KeyState = "Valid \u2713";
        }
        catch (Exception ex)
        {
            KeyState = "Invalid";
            AppLog.Warn($"SteamGridDB key invalid: {ex.Message}");
        }
    }

    [RelayCommand]
    private void UnhideAll()
    {
        _shell.Settings.Hidden.Clear();
        _shell.SaveSettings();
        _shell.Library.ClearHidden();
        SetHiddenCount(0);
        AppLog.Info("All hidden games restored.");
    }
}
