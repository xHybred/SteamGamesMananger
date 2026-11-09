using System.Windows;
using Wpf.Ui.Controls;

namespace Sgm.App.Views;

/// <summary>Prompts for an update and downloads it without closing the dialog.</summary>
public partial class UpdateDialog : ContentDialog
{
    private readonly Func<IProgress<double>, Task<string>> _download;
    private bool _downloading;

    public string? InstallerPath { get; private set; }

    public UpdateDialog(Version version, Func<IProgress<double>, Task<string>> download)
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
        if (args.Button != ContentDialogButton.Primary || _downloading)
            return;

        _downloading = true;
        IsPrimaryButtonEnabled = false;
        IsFooterVisible = false;
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
            InstallerPath = await _download(progress);
            _downloading = false;
            Hide(ContentDialogResult.Primary);
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
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_downloading)
            args.Cancel = true;
    }
}
