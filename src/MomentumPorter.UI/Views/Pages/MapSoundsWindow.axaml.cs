namespace MomentumPorter.UI.Views.Pages;

using Avalonia.Controls;
using MomentumPorter.UI.ViewModels.Pages;

/// <summary>
/// A map's Sounds review. Closes with true when the picks are saved.
/// </summary>
public partial class MapSoundsWindow : Window
{
    public MapSoundsWindow()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MapSoundsViewModel)?.Stop();
        base.OnClosed(e);
    }
}
