using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sgm.App.Views;

public partial class LibraryView : UserControl
{
    /// <summary>Most recently created view, so the shell can focus its search box from anywhere.</summary>
    public static LibraryView? Current { get; private set; }

    public LibraryView()
    {
        Current = this;
        InitializeComponent();
    }

    /// <summary>Focus and select the search box (Ctrl+F).</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }
}
