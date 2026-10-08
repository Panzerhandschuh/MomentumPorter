namespace MomentumPorter.UI.ViewModels.Pages;

using Material.Icons;

/// <summary>
/// A page in the main window's navigation.
/// </summary>
public abstract class PageViewModel : ViewModel
{
    public abstract string Title { get; }

    public abstract MaterialIconKind Icon { get; }
}

/// <summary>
/// A page whose porting logic isn't wired into the app yet.
/// </summary>
public sealed class PlaceholderPageViewModel(string title, MaterialIconKind icon, string description) : PageViewModel
{
    public override string Title { get; } = title;

    public override MaterialIconKind Icon { get; } = icon;

    public string Description { get; } = description;
}
