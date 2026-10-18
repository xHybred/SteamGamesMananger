using System.Windows;
using Wpf.Ui.Controls;

namespace Sgm.App.Views;

public partial class MatchArtworkDialog : ContentDialog
{
    public sealed class Candidate
    {
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string ReleaseDate { get; init; } = "";
        public string Display => string.IsNullOrEmpty(ReleaseDate) ? Name : $"{Name}  \u00B7  {ReleaseDate}";
    }

    public MatchArtworkDialog(string gameName, IReadOnlyList<Candidate> candidates, string autoInfo)
    {
        InitializeComponent();

        Title = $"Match Artwork \u2014 {gameName}";

        AutoInfo.Text = autoInfo;
        AutoInfo.Visibility = string.IsNullOrWhiteSpace(autoInfo) ? Visibility.Collapsed : Visibility.Visible;

        if (candidates.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            CandidateList.ItemsSource = candidates;
            CandidateList.SelectedIndex = 0;
        }
    }
}
