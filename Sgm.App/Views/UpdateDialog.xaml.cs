using System.Windows;
using Wpf.Ui.Controls;

namespace Sgm.App.Views;

/// <summary>Prompts for an update and downloads it without closing the dialog.</summary>
public partial class UpdateDialog : ContentDialog
{
    private readonly Func<IProgress<double>, CancellationToken, Task<string>> _download;
    private CancellationTokenSource? _cts;
    private bool _downloading;
    private bool _cancelRequested;

    public string? InstallerPath { get; private set; }

    public UpdateDialog(
        Version version, Func<IProgress<double>, CancellationToken, Task<string>> download)
    {
        InitializeComponent();
        _download = download;

        MessageText.Text =
            $"Steam Games Manager {version.ToString(3)} is available. Install it now? " +
            "The app will close and reopen automatically when the update finishes.";
        ButtonClicked += OnButtonClicked;
        Closing += OnClosing;
    }

    private async void OnButtonClicked(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (args.Button == ContentDialogButton.Close && _downloading)
        {
            _cancelRequested = true;
            _cts?.Cancel();
            return;
        }

        if (args.Button != ContentDialogButton.Primary || _downloading)
            return;

        _downloading = true;
        _cancelRequested = false;
        _cts = new CancellationTokenSource();
        IsPrimaryButtonEnabled = false;
        PrimaryButtonText = "Downloading...";
        CloseButtonText = "Cancel";
        MessageText.Text = "Downloading the update...";
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Text = "0%";
        StatusText.Visibility = Visibility.Visible;

        var progress = new Progress<double>(value =>
        {
            DownloadProgress.Value = value * 100;
            StatusText.Text = value.ToString("P0");
        });

        try
        {
            InstallerPath = await _download(progress, _cts.Token);
            _downloading = false;
            Hide(ContentDialogResult.Primary);
        }
        catch (OperationCanceledException)
        {
            _downloading = false;
        }
        catch (Exception ex)
        {
            _downloading = false;
            MessageText.Text = "The update could not be downloaded.";
            StatusText.Text = ex.Message;
            DownloadProgress.Visibility = Visibility.Collapsed;
            IsFooterVisible = true;
            IsPrimaryButtonEnabled = true;
            PrimaryButtonText = "Retry";
            CloseButtonText = "Close";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_downloading && !_cancelRequested)
            args.Cancel = true;
    }
}
