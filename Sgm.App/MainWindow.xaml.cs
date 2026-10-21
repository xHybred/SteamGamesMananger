using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Sgm.App.ViewModels;
using Sgm.App.Views;
using Sgm.Core.Configuration;
using Sgm.Core.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Sgm.App;

/// <summary>Fluent (WPF-UI) shell window driven by WPF-UI navigation.</summary>
public partial class MainWindow : FluentWindow
{
    private const string BrandLink = "https://linktr.ee/Hybred";

    private readonly INavigationService _navigation;

    public MainWindow(MainViewModel viewModel, INavigationService navigation, ISnackbarService snackbar)
    {
        _navigation = navigation;

        InitializeComponent();
        DataContext = viewModel;

        RestoreWindowBounds(viewModel.Settings);

        _navigation.SetNavigationControl(NavView);
        snackbar.SetSnackbarPresenter(SnackbarPresenter);

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigation.Navigate(typeof(LibraryView));
    }

    /// <summary>Ctrl+F reaches the search box even when focus sits on the window chrome.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        e.Handled = true;

        if (LibraryView.Current is { IsLoaded: true } view)
        {
            view.FocusSearch();
            return;
        }

        _navigation.Navigate(typeof(LibraryView));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => LibraryView.Current?.FocusSearch());
    }

    private void OnBrandClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(BrandLink) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not open {BrandLink}: {ex.Message}");
        }
    }

    /// <summary>Apply the size and position stored from the previous session.</summary>
    private void RestoreWindowBounds(AppSettings settings)
    {
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowLeft is double left && settings.WindowTop is double top
            && IsReachable(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Guard against a saved position on a monitor that is no longer attached.</summary>
    private static bool IsReachable(double left, double top, double width, double height)
    {
        var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        var bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        return left + width > SystemParameters.VirtualScreenLeft + 40
               && left < right - 40
               && top >= SystemParameters.VirtualScreenTop
               && top + 40 < bottom;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        var settings = viewModel.Settings;
        settings.WindowLeft = bounds.Left;
        settings.WindowTop = bounds.Top;
        settings.WindowWidth = bounds.Width;
        settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        viewModel.SaveSettings();
    }
}
