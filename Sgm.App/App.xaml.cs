using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Sgm.App.Services;
using Sgm.App.ViewModels;
using Sgm.App.Views;
using Sgm.Core.Configuration;
using Sgm.Core.Logging;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
using ThemeService = Sgm.App.Services.ThemeService;

namespace Sgm.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Start();
        }
        catch (Exception ex)
        {
            try
            {
                var crash = AppPaths.DataFile("crash.log");
                System.IO.File.WriteAllText(crash, ex.ToString());
            }
            catch
            {
                // Logging is best-effort; never mask the original startup error.
            }
            AppLog.Error($"Startup failed: {ex}");
            MessageBox.Show(ex.ToString(), "Steam Games Manager — startup error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Start()
    {
        var logPath = AppPaths.DataFile("SteamGamesManager.log");
        AppLog.StartFile(logPath);

        _services = BuildServices();

        var settings = _services.GetRequiredService<AppSettings>();
        ThemeService.Apply(settings.Theme);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();

        var viewModel = _services.GetRequiredService<MainViewModel>();
        _ = viewModel.InitializeAsync();
        _ = viewModel.CheckForUpdatesOnLaunchAsync();

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error($"Unhandled: {args.Exception}");
            MessageBox.Show(args.Exception.Message, "Steam Games Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Core services
        services.AddSingleton<SettingsStore>();
        services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Load());

        // WPF-UI shell services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();

        // Application view model (owns the per-view view models)
        services.AddSingleton<MainViewModel>();

        // Pages: each view is created with the matching view model as its DataContext.
        services.AddTransient(sp => new LibraryView
        {
            DataContext = sp.GetRequiredService<MainViewModel>().Library,
        });
        services.AddTransient(sp => new AccountsView
        {
            DataContext = sp.GetRequiredService<MainViewModel>().AccountsVm,
        });
        services.AddTransient(sp => new SettingsView
        {
            DataContext = sp.GetRequiredService<MainViewModel>().SettingsVm,
        });
        services.AddTransient(sp => new HelpView
        {
            DataContext = sp.GetRequiredService<MainViewModel>().HelpVm,
        });

        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
