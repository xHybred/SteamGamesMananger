using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sgm.Core.Logging;
using Sgm.Core.Services;
using Sgm.Core.Steam;

namespace Sgm.App.ViewModels;

/// <summary>Accounts view: pick which userdata accounts to write, and prune system junk.</summary>
public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public ObservableCollection<AccountItemViewModel> Accounts => _shell.Accounts;

    public AccountsViewModel(MainViewModel shell) => _shell = shell;

    public async Task LoadAsync()
    {
        var steamDir = _shell.SteamDir;
        if (steamDir == null)
            return;

        var list = await Task.Run(() =>
        {
            var recent = SteamUsers.MostRecentAccountId(steamDir);
            var users = SteamUsers.ReadLoginUsers(steamDir);
            var result = new List<AccountItemViewModel>();
            foreach (var dir in SteamPaths.ResolveAllUserDirs(steamDir))
            {
                var label = SteamWriter.AccountLabel(steamDir, dir, users);
                result.Add(new AccountItemViewModel(dir, label, Path.GetFileName(dir) == recent));
            }
            return result;
        });

        // Default the write/detect scope to the active account; fall back to all
        // when no active account could be determined.
        var defaultToRecent = list.Any(a => a.IsRecent);
        foreach (var account in list)
        {
            account.IsChecked = defaultToRecent ? account.IsRecent : true;
            account.PropertyChanged += OnAccountPropertyChanged;
        }

        _shell.SetAccounts(list);
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountItemViewModel.IsChecked))
            return;
        _ = RefreshLibraryScopeAsync();
    }

    private async Task RefreshLibraryScopeAsync()
    {
        try
        {
            await _shell.Library.RefreshStatesAsync();
            _shell.Library.ApplyFilterPublic();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Account scope refresh failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task PruneAsync()
    {
        var steamDir = _shell.SteamDir;
        if (steamDir == null)
            return;

        var accounts = _shell.CheckedAccountDirs();
        if (accounts.Count == 0)
            return;

        try
        {
            foreach (var dir in accounts)
            {
                var dir1 = dir;
                var (_, _, error) = await Task.Run(() => SteamWriter.PruneAccount(dir1));
                if (error != null)
                    AppLog.Error($"[{Path.GetFileName(dir)}] prune failed: {error}");
            }

            await _shell.Library.RefreshStatesAsync();
            _shell.Library.ApplyFilterPublic();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Prune failed: {ex}");
        }
    }
}
