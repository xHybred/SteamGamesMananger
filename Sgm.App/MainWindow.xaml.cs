using System.Windows;
using Sgm.App.ViewModels;
using Sgm.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Sgm.App;

/// <summary>Fluent (WPF-UI) shell window driven by WPF-UI navigation.</summary>
public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigation;

    public MainWindow(MainViewModel viewModel, INavigationService navigation, ISnackbarService snackbar)
    {
        _navigation = navigation;

        InitializeComponent();
        DataContext = viewModel;

        _navigation.SetNavigationControl(NavView);
        snackbar.SetSnackbarPresenter(SnackbarPresenter);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigation.Navigate(typeof(LibraryView));
    }
}
