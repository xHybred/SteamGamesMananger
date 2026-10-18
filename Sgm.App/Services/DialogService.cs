using System.Windows;
using Sgm.App.Views;
using Wpf.Ui.Controls;

namespace Sgm.App.Services;

/// <summary>Shows modal dialogs and returns their results (kept out of the view models).</summary>
public sealed class DialogService
{
    public async Task<int?> ShowMatchDialogAsync(string gameName, IReadOnlyList<MatchArtworkDialog.Candidate> candidates, string autoInfo)
    {
        if (Application.Current?.MainWindow is not MainWindow main || main.RootDialogHost is null)
            return null;

        var dialog = new MatchArtworkDialog(gameName, candidates, autoInfo)
        {
            DialogHostEx = main.RootDialogHost,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;
        return (dialog.CandidateList.SelectedItem as MatchArtworkDialog.Candidate)?.Id;
    }
}
