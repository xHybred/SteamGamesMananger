using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sgm.App.Services;
using Sgm.Core.Artwork;
using Sgm.Core.Configuration;
using Sgm.Core.Steam;
using Sgm.Core.Utilities;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ThemeService = Sgm.App.Services.ThemeService;

namespace Sgm.App.ViewModels;

/// <summary>Window shell: navigation, theme, status/flash, accounts, and shared services.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly ISnackbarService _snackbar;
    private readonly DispatcherTimer _steamTimer;
    private bool _steamBusy;

    public AppSettings Settings { get; }
    public string? SteamDir { get; private set; }
    public ObservableCollection<AccountItemViewModel> Accounts { get; } = new();

    public LibraryViewModel Library { get; }
    public AccountsViewModel AccountsVm { get; }
    public SettingsViewModel SettingsVm { get; }
    public HelpViewModel HelpVm { get; }

    [ObservableProperty] private bool _steamRunning;
    [ObservableProperty] private bool _steamMissing;
    [ObservableProperty] private string _themeName = "steam";

    public MainViewModel(SettingsStore store, AppSettings settings, ISnackbarService snackbar)
    {
        _store = store;
        _snackbar = snackbar;
        Settings = settings;
        ThemeName = ThemeService.Current;

        Library = new LibraryViewModel(this);
        AccountsVm = new AccountsViewModel(this);
        SettingsVm = new SettingsViewModel(this);
        HelpVm = new HelpViewModel();

        SettingsVm.SetHiddenCount(Settings.Hidden.Count);

        _steamTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _steamTimer.Tick += (_, _) => RefreshSteamRunning();
    }

    public async Task InitializeAsync()
    {
        Library.IsInitializing = true;
        try
        {
            SteamDir = await Task.Run(() => SteamPaths.FindInstallDir(null));
            if (SteamDir == null)
            {
                SteamMissing = true;
                Sgm.Core.Logging.AppLog.Warn("Steam install not found; library scanning is unavailable.");
                return;
            }

            await AccountsVm.LoadAsync();
            RefreshSteamRunning();
            _steamTimer.Start();
            await Library.ScanAsync();
        }
        catch (Exception ex)
        {
            Sgm.Core.Logging.AppLog.Error($"Initialization failed: {ex}");
            Library.SummaryText = "Scan failed. See log for details.";
        }
        finally
        {
            Library.IsInitializing = false;
        }
    }

    public void RefreshSteamRunning()
    {
        SteamRunning = SteamProcessGuard.IsSteamRunning();
        Library.SetSteamRunning(SteamRunning);
        // SteamDir is populated asynchronously after construction, and SteamRunning may
        // never change; re-evaluate the power commands here so they reflect current state.
        NotifySteamCommands();
    }

    public void Flash(string message, bool isError = false)
    {
        _snackbar.Show(
            "Steam Games Manager",
            message,
            isError ? ControlAppearance.Danger : ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(4));
    }

    public void SetAccounts(IReadOnlyList<AccountItemViewModel> accounts)
    {
        Accounts.Clear();
        foreach (var account in accounts)
            Accounts.Add(account);
    }

    public List<string> CheckedAccountDirs()
        => Accounts.Where(a => a.IsChecked).Select(a => a.UserDir).ToList();

    public List<string> AllAccountDirs()
        => Accounts.Select(a => a.UserDir).ToList();

    public void SaveSettings()
    {
        try
        {
            _store.Save(Settings);
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Warn($"Settings save failed: {ex.Message}");
        }
    }

    public string ApiKey()
    {
        if (!string.IsNullOrWhiteSpace(Settings.ApiKey))
            return Settings.ApiKey.Trim();
        return Environment.GetEnvironmentVariable("STEAMGRIDDB_API_KEY")?.Trim() ?? "";
    }

    public SteamGridDbClient? CreateArtClient()
    {
        var key = ApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return null;
        try
        {
            return new SteamGridDbClient(key);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void NotifyHiddenChanged(int count) => SettingsVm.SetHiddenCount(count);

    partial void OnSteamRunningChanged(bool value) => NotifySteamCommands();

    private void NotifySteamCommands()
    {
        LaunchSteamCommand.NotifyCanExecuteChanged();
        RestartSteamCommand.NotifyCanExecuteChanged();
        CloseSteamCommand.NotifyCanExecuteChanged();
    }

    private bool CanLaunchSteam() => SteamDir != null && !SteamRunning && !_steamBusy;
    private bool CanRestartSteam() => SteamDir != null && SteamRunning && !_steamBusy;
    private bool CanCloseSteam() => SteamDir != null && SteamRunning && !_steamBusy;

    [RelayCommand(CanExecute = nameof(CanLaunchSteam))]
    private async Task LaunchSteamAsync() => await SteamActionAsync(
        () => SteamControl.StartSteam(SteamDir),
        running: true,
        "Steam launched.",
        "Could not find steam.exe.");

    [RelayCommand(CanExecute = nameof(CanRestartSteam))]
    private async Task RestartSteamAsync() => await SteamActionAsync(
        () => SteamControl.RestartSteamAsync(SteamDir).GetAwaiter().GetResult(),
        running: true,
        "Steam restarted.",
        "Could not restart Steam.");

    [RelayCommand(CanExecute = nameof(CanCloseSteam))]
    private async Task CloseSteamAsync() => await SteamActionAsync(
        () => SteamControl.ShutdownSteamAsync(SteamDir).GetAwaiter().GetResult(),
        running: false,
        "Steam closed.",
        "Steam is still closing\u2026");

    /// <summary>Run a Steam launch/close action on a worker thread, then settle the UI state.</summary>
    private async Task SteamActionAsync(Func<bool> action, bool running, string okStatus, string failStatus)
    {
        if (SteamDir == null)
            return;

        try
        {
            _steamBusy = true;
            NotifySteamCommands();

            var ok = await Task.Run(action);
            await WaitForSteamStateAsync(running);

            if (ok)
                Flash(okStatus);
            else
                Flash(failStatus, isError: true);
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Error($"Steam action failed: {ex}");
            Flash(failStatus, isError: true);
        }
        finally
        {
            _steamBusy = false;
            RefreshSteamRunning();
            NotifySteamCommands();
        }
    }

    private static async Task<bool> WaitForSteamStateAsync(bool running, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (SteamProcessGuard.IsSteamRunning() != running && Environment.TickCount64 < deadline)
            await Task.Delay(500);
        return SteamProcessGuard.IsSteamRunning() == running;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = ThemeService.Next(ThemeName);
        ThemeService.Apply(next);
        ThemeName = next;
        Settings.Theme = next;
        SaveSettings();
    }
}
