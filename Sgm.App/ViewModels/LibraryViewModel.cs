using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sgm.App.Services;
using Sgm.App.Views;
using Sgm.Core.Artwork;
using Sgm.Core.Logging;
using Sgm.Core.Models;
using Sgm.Core.Scanners;
using Sgm.Core.Services;
using Sgm.Core.Steam;
using Sgm.Core.Text;
using Sgm.Core.Vdf;

namespace Sgm.App.ViewModels;

/// <summary>The game library: scanning, filtering, status, and add/artwork actions.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private readonly DialogService _dialogs = new();

    private readonly List<GameItemViewModel> _all = new();
    private readonly Dictionary<GameKey, (int Id, string Name)> _overrides = new();
    private readonly Dictionary<GameKey, string> _artStatus = new();
    private readonly Dictionary<string, int> _sourceCounts = new();
    private readonly HashSet<GameKey> _hidden = new();
    private HashSet<GameKey> _steamState = new();
    private HashSet<string> _steamTitles = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<GameItemViewModel> Games { get; } = new();
    public ObservableCollection<GameRowViewModel> GameRows { get; } = new();
    public ObservableCollection<LauncherChipViewModel> Chips { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isListView = true;
    [ObservableProperty] private bool _isGridView;
    [ObservableProperty] private bool _isCompactView = true;
    [ObservableProperty] private bool _isNormalView;
    [ObservableProperty] private bool _isLargeView;
    [ObservableProperty] private double _rowThumbWidth = 28;
    [ObservableProperty] private double _rowThumbHeight = 40;
    [ObservableProperty] private double _rowFontSize = 11;
    [ObservableProperty] private double _rowButtonHeight = 26;
    [ObservableProperty] private double _tileCoverWidth = 120;
    [ObservableProperty] private double _tileCoverHeight = 160;
    [ObservableProperty] private double _tileTitleFontSize = 11;
    [ObservableProperty] private double _tileMetaFontSize = 9;
    [ObservableProperty] private double _tileIconFontSize = 11;
    [ObservableProperty] private double _tileButtonSize = 24;
    [ObservableProperty] private int _gridColumns = 5;
    [ObservableProperty] private string _summaryText = "Scanning…";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _steamRunning;
    [ObservableProperty] private string _steamStatusText = "Steam: \u2014";
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isInitializing;
    [ObservableProperty] private bool _isAddingAll;

    /// <summary>True while a scan or the initial library load is running.</summary>
    public bool ShowBusy => IsBusy || IsInitializing;

    /// <summary>The empty-state card should stay hidden behind the initial progress ring.</summary>
    public bool ShowEmptyState => IsEmpty && !ShowBusy;

    public LibraryViewModel(MainViewModel shell)
    {
        _shell = shell;

        foreach (var source in GameSource.Order)
        {
            var source1 = source;
            Chips.Add(new LauncherChipViewModel(
                source,
                GameSource.Label(source),
                _shell.Settings.Sources.TryGetValue(source, out var on) && on,
                () => OnChipChanged(source1)));
        }

        foreach (var entry in _shell.Settings.Hidden)
        {
            if (entry.Length == 3)
                _hidden.Add(new GameKey(entry[0], entry[1], entry[2]));
        }

        IsListView = _shell.Settings.View != "grid";
        IsGridView = !IsListView;
        IsCompactView = _shell.Settings.Density == "compact";
        IsNormalView = _shell.Settings.Density == "normal";
        IsLargeView = _shell.Settings.Density == "large";
        if (!IsCompactView && !IsNormalView && !IsLargeView)
            IsCompactView = true;
        ApplyDensity();
    }

    /// <summary>The shell view model, for header status/flash bindings.</summary>
    public MainViewModel Shell => _shell;

    private void ApplyDensity()
    {
        if (IsLargeView)
        {
            RowThumbWidth = 56; RowThumbHeight = 78; RowFontSize = 13; RowButtonHeight = 34;
            TileCoverWidth = 188; TileCoverHeight = 250;
            TileTitleFontSize = 14; TileMetaFontSize = 11; TileIconFontSize = 15; TileButtonSize = 32;
            GridColumns = 3;
        }
        else if (IsNormalView)
        {
            RowThumbWidth = 40; RowThumbHeight = 58; RowFontSize = 12; RowButtonHeight = 30;
            TileCoverWidth = 150; TileCoverHeight = 200;
            TileTitleFontSize = 12; TileMetaFontSize = 10; TileIconFontSize = 13; TileButtonSize = 28;
            GridColumns = 4;
        }
        else
        {
            RowThumbWidth = 28; RowThumbHeight = 40; RowFontSize = 11; RowButtonHeight = 26;
            TileCoverWidth = 120; TileCoverHeight = 160;
            TileTitleFontSize = 11; TileMetaFontSize = 9; TileIconFontSize = 11; TileButtonSize = 24;
            GridColumns = 5;
        }

        RebuildRows();
    }

    /// <summary>Re-chunk the grid into rows of <see cref="GridColumns"/> items without rebuilding untouched rows.</summary>
    private void RebuildRows()
    {
        var columns = GridColumns > 0 ? GridColumns : 4;
        var rowCount = (Games.Count + columns - 1) / columns;

        while (GameRows.Count > rowCount)
            GameRows.RemoveAt(GameRows.Count - 1);
        while (GameRows.Count < rowCount)
            GameRows.Add(new GameRowViewModel(Array.Empty<GameItemViewModel>()));

        for (var r = 0; r < rowCount; r++)
        {
            var items = Games.Skip(r * columns).Take(columns).ToList();
            if (!GameRows[r].Items.SequenceEqual(items))
                GameRows[r].Items = items;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyBusyState();

    partial void OnIsInitializingChanged(bool value) => NotifyBusyState();

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    private void NotifyBusyState()
    {
        OnPropertyChanged(nameof(ShowBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void OnChipChanged(string source)
    {
        var chip = Chips.First(c => c.Source == source);
        _shell.Settings.Sources[source] = chip.IsOn;
        _shell.SaveSettings();
        ApplyFilter();
    }

    [RelayCommand]
    private void SetListView()
    {
        IsListView = true;
        IsGridView = false;
        _shell.Settings.View = "list";
        _shell.SaveSettings();
    }

    [RelayCommand]
    private void SetGridView()
    {
        IsGridView = true;
        IsListView = false;
        _shell.Settings.View = "grid";
        _shell.SaveSettings();
    }

    [RelayCommand]
    private void SetCompactView()
    {
        IsCompactView = true;
        IsNormalView = false;
        IsLargeView = false;
        ApplyDensity();
        _shell.Settings.Density = "compact";
        _shell.SaveSettings();
    }

    [RelayCommand]
    private void SetNormalView()
    {
        IsNormalView = true;
        IsCompactView = false;
        IsLargeView = false;
        ApplyDensity();
        _shell.Settings.Density = "normal";
        _shell.SaveSettings();
    }

    [RelayCommand]
    private void SetLargeView()
    {
        IsLargeView = true;
        IsCompactView = false;
        IsNormalView = false;
        ApplyDensity();
        _shell.Settings.Density = "large";
        _shell.SaveSettings();
    }

    // ------------------------------------------------------------------ scanning

    public async Task ScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        try
        {
            var scanner = new GameScanner();
            var games = await Task.Run(() => scanner.Scan(GameSource.Order, token), token);

            _all.Clear();
            foreach (var game in games)
                _all.Add(new GameItemViewModel(game));

            _sourceCounts.Clear();
            foreach (var game in _all)
                _sourceCounts[game.Source] = _sourceCounts.GetValueOrDefault(game.Source) + 1;

            await RefreshStatesAsync();
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Scan failed: {ex}");
        }
        finally
        {
            IsBusy = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }

        await LoadCoversAsync();
    }

    [RelayCommand]
    private void CancelScan() => _scanCts?.Cancel();

    /// <summary>Re-apply the current filters (used after external status changes).</summary>
    public void ApplyFilterPublic() => ApplyFilter();

    /// <summary>Forget all hidden games (Settings → Unhide All).</summary>
    public void ClearHidden()
    {
        _hidden.Clear();
        _shell.Settings.Hidden.Clear();
        ApplyFilter();
    }

    /// <summary>Recompute in-Steam / owned / artwork status for every game.</summary>
    public async Task RefreshStatesAsync()
    {
        var steamDir = _shell.SteamDir;
        var accountDirs = _shell.CheckedAccountDirs();
        if (accountDirs.Count == 0)
            AppLog.Warn("No accounts checked \u2014 'In Steam' status will be empty.");

        var (state, titles, artStatus) = await Task.Run(() =>
        {
            var states = new List<HashSet<(string Name, string Exe)>>();
            foreach (var dir in accountDirs)
            {
                try
                {
                    states.Add(ShortcutStore.InSteamSet(ShortcutStore.Load(SteamPaths.ShortcutsPath(dir))));
                }
                catch (InvalidDataException)
                {
                    // Corrupt account file: skip its state.
                }
            }

            var inSteam = new HashSet<GameKey>();
            var art = new Dictionary<GameKey, string>();
            foreach (var game in _all)
            {
                var key = game.Key;
                var pair = ShortcutStore.NameExe(game.Game);
                if (states.Any(s => s.Contains(pair)))
                    inSteam.Add(key);

                var kinds = ArtworkService.ExistingKinds(game.Game, accountDirs);
                if (kinds.Count > 0)
                    art[key] = PrettyArt(kinds);
            }

            var owned = SteamLibrary.InstalledTitles(steamDir);
            return (inSteam, owned, art);
        });

        _steamState = state;
        _steamTitles = titles;
        _artStatus.Clear();
        foreach (var (key, value) in artStatus)
            _artStatus[key] = value;

        foreach (var game in _all)
        {
            game.InSteam = _steamState.Contains(game.Key);
            game.OwnedOnSteam = _steamTitles.Contains(TitleNormalizer.Normalize(game.Name));
            game.ArtStatus = _artStatus.TryGetValue(game.Key, out var s) ? "Art: " + s : "";
        }
    }

    private static string PrettyArt(IReadOnlyList<string> kinds)
    {
        string Pretty(string kind) => kind switch
        {
            "grids" => "Cover",
            "heroes" => "Banner",
            "logos" => "Logo",
            "icons" => "Icon",
            _ => kind,
        };
        return string.Join(", ", kinds.Select(Pretty));
    }

    private async Task LoadCoversAsync()
    {
        var accountDirs = _shell.AllAccountDirs();
        var found = 0;
        foreach (var game in _all)
        {
            if (game.CoverImage != null)
            {
                found++;
                continue;
            }
            var game1 = game;
            try
            {
                var path = await Task.Run(() => ThumbnailService.Resolve(game1.Game, accountDirs));
                if (!string.IsNullOrEmpty(path))
                {
                    game1.CoverPath = path;
                    found++;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Cover failed for {game.Name}: {ex.Message}");
            }
        }
        AppLog.Info($"Cover pass: {found}/{_all.Count} cover(s) resolved.");
    }

    // ------------------------------------------------------------------ filtering

    private void ApplyFilter()
    {
        var needle = SearchText.Trim().ToLowerInvariant();
        var sources = Chips.ToDictionary(c => c.Source, c => c.IsOn);

        var visible = new List<GameItemViewModel>();
        var newCount = 0;
        foreach (var game in _all)
        {
            if (!sources.GetValueOrDefault(game.Source, false))
                continue;
            if (_hidden.Contains(game.Key))
                continue;
            if (needle.Length > 0 && !game.Name.ToLowerInvariant().Contains(needle))
                continue;
            visible.Add(game);
            if (!game.InSteam)
                newCount++;
        }

        // Merge every enabled storefront into one alphabetical library.
        visible.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        Synchronize(Games, visible);

        NewCount = newCount;
        IsEmpty = Games.Count == 0;
        SummaryText = $"{Games.Count}/{_all.Count}  \u00B7  {newCount} New";
        AddAllNewCommand.NotifyCanExecuteChanged();

        RebuildRows();

        foreach (var chip in Chips)
            chip.Count = _sourceCounts.GetValueOrDefault(chip.Source, 0);

        SteamStatusText = SteamRunning ? "Steam: Running" : "Steam: Closed";
    }

    /// <summary>
    /// Mirror <paramref name="desired"/> into <paramref name="target"/> with minimal collection
    /// notifications, so a state refresh does not clear and rebuild every visible row.
    /// </summary>
    private static void Synchronize(ObservableCollection<GameItemViewModel> target, IReadOnlyList<GameItemViewModel> desired)
    {
        var wanted = new HashSet<GameItemViewModel>(desired);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
                target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            var current = target.IndexOf(item);
            if (current < 0)
                target.Insert(i, item);
            else if (current != i)
                target.Move(current, i);
        }
    }

    public void SetSteamRunning(bool running)
    {
        SteamRunning = running;
        SteamStatusText = running ? "Steam: Running" : "Steam: Closed";
    }

    // ------------------------------------------------------------------ actions

    private bool CanAddAllNew() => NewCount > 0;

    [RelayCommand(CanExecute = nameof(CanAddAllNew), AllowConcurrentExecutions = true)]
    private async Task AddAllNewAsync()
    {
        var fresh = Games.Where(g => !g.InSteam).ToList();
        if (fresh.Count == 0)
            return;
        IsAddingAll = true;
        try
        {
            await AddGamesAsync(fresh);
        }
        finally
        {
            IsAddingAll = false;
        }
    }

    private bool CanClearSearch() => SearchText.Length > 0;

    [RelayCommand(CanExecute = nameof(CanClearSearch))]
    private void ClearSearch() => SearchText = "";

    private readonly Stack<GameKey> _hiddenUndo = new();

    /// <summary>Restore the most recently hidden game (Ctrl+Z).</summary>
    [RelayCommand]
    private void UndoHide()
    {
        if (_hiddenUndo.Count == 0)
        {
            _shell.Flash("Nothing to undo.");
            return;
        }

        var key = _hiddenUndo.Pop();
        _hidden.Remove(key);
        PersistHidden();
        _shell.Flash($"{key.Name} restored.");
    }

    private void PersistHidden()
    {
        _shell.Settings.Hidden = _hidden.Select(k => new[] { k.Source, k.Name, k.Exe }).ToList();
        _shell.SaveSettings();
        _shell.NotifyHiddenChanged(_hidden.Count);
        ApplyFilter();
    }

    // One shared command instance backs every row's Add button. Without concurrent execution,
    // the async command reports CanExecute=false while a write runs, so every Add button in the
    // list renders its disabled (dark) state. AddGamesAsync already serializes with IsBusy.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddSingleAsync(GameItemViewModel? game)
    {
        if (game == null)
            return;
        if (game.InSteam)
            await RemoveGamesAsync(new List<GameItemViewModel> { game });
        else
            await AddGamesAsync(new List<GameItemViewModel> { game });
    }

    private async Task AddGamesAsync(IReadOnlyList<GameItemViewModel> games)
    {
        if (IsBusy)
            return;

        var steamDir = _shell.SteamDir;
        if (steamDir == null)
        {
            _shell.Flash("Steam was not found.", isError: true);
            return;
        }

        var accounts = _shell.CheckedAccountDirs();
        if (accounts.Count == 0)
        {
            _shell.Flash("Select at least one Steam account.", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            _shell.RefreshSteamRunning();
            SetSteamRunning(_shell.SteamRunning);
            if (_shell.SteamRunning && !_shell.Settings.Force)
            {
                _shell.Flash("Failed. Close Steam.", isError: true);
                return;
            }

            foreach (var game in games)
                game.IsAdding = true;

            var gameModels = games.Select(g => g.Game).ToList();
            foreach (var dir in accounts)
            {
                var result = await Task.Run(() =>
                    SteamWriter.WriteAccount(dir, gameModels, _shell.Settings.Overwrite));
                if (result.Error != null)
                {
                    AppLog.Error($"[{Path.GetFileName(dir)}] write failed: {result.Error}");
                    continue;
                }
            }

            if (_shell.Settings.Artwork)
            {
                var client = _shell.CreateArtClient();
                var overrides = games
                    .Where(g => _overrides.ContainsKey(g.Key))
                    .ToDictionary(g => g.Name, g => _overrides[g.Key].Id);
                var cache = new Dictionary<uint, Dictionary<string, string>>();
                foreach (var dir in accounts)
                {
                    var dir1 = dir;
                    var (_, _, newCache) = await Task.Run(() =>
                        ArtworkService.ForAccount(client, gameModels, dir1, cache, overrides));
                    cache = newCache;
                }
            }

            await RefreshStatesAsync();
            ApplyFilter();
            _shell.Flash("Added. Restart Steam to see changes.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Add failed: {ex}");
        }
        finally
        {
            foreach (var game in games)
                game.IsAdding = false;
            IsBusy = false;
            AddAllNewCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task RemoveGamesAsync(IReadOnlyList<GameItemViewModel> games)
    {
        if (IsBusy)
            return;

        var steamDir = _shell.SteamDir;
        if (steamDir == null)
        {
            _shell.Flash("Steam was not found.", isError: true);
            return;
        }

        var accounts = _shell.CheckedAccountDirs();
        if (accounts.Count == 0)
        {
            _shell.Flash("Select at least one Steam account.", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            _shell.RefreshSteamRunning();
            SetSteamRunning(_shell.SteamRunning);
            if (_shell.SteamRunning && !_shell.Settings.Force)
            {
                _shell.Flash("Failed. Close Steam.", isError: true);
                return;
            }

            foreach (var game in games)
                game.IsAdding = true;

            var gameModels = games.Select(g => g.Game).ToList();
            foreach (var dir in accounts)
            {
                var (removed, backup, error) = await Task.Run(() =>
                    SteamWriter.RemoveAccount(dir, gameModels));
                if (error != null)
                    AppLog.Error($"[{Path.GetFileName(dir)}] remove failed: {error}");
            }

            await RefreshStatesAsync();
            ApplyFilter();
            _shell.Flash("Removed. Restart Steam to see changes.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Remove failed: {ex}");
        }
        finally
        {
            foreach (var game in games)
                game.IsAdding = false;
            IsBusy = false;
            AddAllNewCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task MatchArtworkAsync(GameItemViewModel? game)
    {
        if (game == null)
            return;

        var key = _shell.ApiKey();

        List<MatchArtworkDialog.Candidate> candidates = new();
        if (!string.IsNullOrEmpty(key))
        {
            try
            {
                candidates = await Task.Run(() =>
                {
                    var client = new SteamGridDbClient(key);
                    return client.SearchGame(game.Name)
                        .Select(ToCandidate)
                        .Where(c => c != null)
                        .Select(c => c!)
                        .ToList();
                });
            }
            catch (SteamGridDbException ex)
            {
                AppLog.Warn($"Art search failed for {game.Name}: {ex.Message}");
                return;
            }
        }

        string autoInfo;
        try
        {
            autoInfo = await Task.Run(() => AutoArtwork.MatchLabel(game.Name, game.Source));
        }
        catch
        {
            autoInfo = "";
        }

        var chosen = await _dialogs.ShowMatchDialogAsync(game.Name, candidates, autoInfo);
        if (chosen is int id)
        {
            var name = candidates.FirstOrDefault(c => c.Id == id)?.Name ?? $"id {id}";
            _overrides[game.Key] = (id, name);
            _shell.Flash($"{game.Name}: artwork locked.", isError: false);
        }
    }

    private static MatchArtworkDialog.Candidate? ToCandidate(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id))
            return null;
        var name = element.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
        var release = element.TryGetProperty("release_date", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? ""
            : "";
        return new MatchArtworkDialog.Candidate { Id = id, Name = name, ReleaseDate = release };
    }

    [RelayCommand]
    private void Hide(GameItemViewModel? game)
    {
        if (game == null)
            return;
        _hidden.Add(game.Key);
        _hiddenUndo.Push(game.Key);
        PersistHidden();
        _shell.Flash($"{game.Name} hidden. Press Ctrl+Z to undo.");
    }

    [RelayCommand]
    private async Task RescanAsync() => await ScanAsync();
}
