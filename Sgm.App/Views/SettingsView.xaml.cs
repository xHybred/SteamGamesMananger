using System.Windows;
using System.Windows.Controls;
using Sgm.App.ViewModels;

namespace Sgm.App.Views;

public partial class SettingsView : UserControl
{
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncFromViewModel();
        Loaded += (_, _) => SyncFromViewModel();
    }

    private void SyncFromViewModel()
    {
        if (DataContext is not SettingsViewModel vm)
            return;
        _syncing = true;
        ApiKeyBox.Password = vm.ApiKey;
        _syncing = false;
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || DataContext is not SettingsViewModel vm)
            return;
        vm.ApiKey = ApiKeyBox.Password;
    }
}
