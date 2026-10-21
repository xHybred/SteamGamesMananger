using CommunityToolkit.Mvvm.ComponentModel;

namespace Sgm.App.ViewModels;

/// <summary>A launcher filter chip with a live count and on/off state.</summary>
public sealed partial class LauncherChipViewModel : ObservableObject
{
    private readonly Action _onChanged;

    public string Source { get; }
    public string Label { get; }

    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isOn;

    public LauncherChipViewModel(string source, string label, bool isOn, Action onChanged)
    {
        Source = source;
        Label = label;
        _isOn = isOn;
        _onChanged = onChanged;
    }

    public string Display => Count > 0 ? $"{Label} [{Count}]" : Label;

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));

    partial void OnIsOnChanged(bool value) => _onChanged();
}

/// <summary>One Steam account (userdata dir) with a write-target checkbox.</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    public string UserDir { get; }
    public string Label { get; }
    public bool IsRecent { get; }

    [ObservableProperty] private bool _isChecked = true;

    public AccountItemViewModel(string userDir, string label, bool isRecent)
    {
        UserDir = userDir;
        Label = label;
        IsRecent = isRecent;
    }

    public string Display => IsRecent ? $"\u2605 {Label}" : Label;
}

/// <summary>A grid row of tiles, so the grid ListBox can virtualize rows.</summary>
public sealed partial class GameRowViewModel : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<GameItemViewModel> _items;

    public GameRowViewModel(IReadOnlyList<GameItemViewModel> items) => _items = items;
}
