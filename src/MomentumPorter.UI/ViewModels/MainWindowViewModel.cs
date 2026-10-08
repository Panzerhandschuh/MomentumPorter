namespace MomentumPorter.UI.ViewModels;

using MomentumPorter.Steam;
using ReactiveUI;

public class ViewModel : ReactiveObject;

public sealed class MainWindowViewModel : ViewModel
{
    public MainWindowViewModel()
    {
        GameLocation? momentum = new SteamLocator().FindMomentum();
        Status = momentum is null
            ? "Momentum Mod wasn't found in your Steam libraries"
            : $"Momentum Mod: {momentum.InstallDirectory}";
    }

    public string Title => "MomentumPorter";

    public string Status { get; }
}
