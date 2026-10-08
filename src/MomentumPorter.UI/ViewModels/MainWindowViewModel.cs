namespace MomentumPorter.UI.ViewModels;

using Material.Icons;
using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Pages;
using ReactiveUI;

public sealed class MainWindowViewModel : ViewModel
{
    private PageViewModel _selectedPage;
    private string? _statusMessage;
    private bool _isDragOver;

    public MainWindowViewModel(SettingsService settings, IDialogService dialogs, JobRunnerService runner, Action<Action> post)
    {
        Maps = new MapsPageViewModel(settings, dialogs, runner, post);
        Settings = new SettingsPageViewModel(settings, dialogs, post);
        // Its own runner, so building a sound pack doesn't wait for the map queue
        PlayerSounds = new PlayerSoundsPageViewModel(settings, new JobRunnerService(post), post);
        Weapons = new WeaponsPageViewModel(settings, new JobRunnerService(post), post);
        Pages =
        [
            Maps,
            Weapons,
            PlayerSounds,
            new PlaceholderPageViewModel("Player Models", MaterialIconKind.HumanHandsdown,
                "Player models for ghosts, replays and third person are coming in a later phase."),
            Settings,
        ];
        _selectedPage = Maps;
        _statusMessage = settings.LoadError;
    }

    public string Title => "MomentumPorter";

    public IReadOnlyList<PageViewModel> Pages { get; }

    public MapsPageViewModel Maps { get; }

    public SettingsPageViewModel Settings { get; }

    public PlayerSoundsPageViewModel PlayerSounds { get; }

    public WeaponsPageViewModel Weapons { get; }

    public PageViewModel SelectedPage
    {
        get => _selectedPage;
        set
        {
            // The nav list deselects briefly while it updates; keep showing a page
            if (value is not null)
                this.RaiseAndSetIfChanged(ref _selectedPage, value);
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>Files are being dragged over the window, show the drop overlay.</summary>
    public bool IsDragOver
    {
        get => _isDragOver;
        set => this.RaiseAndSetIfChanged(ref _isDragOver, value);
    }

    /// <summary>
    /// Routes dropped (or command line) files: maps go to the Maps queue, anything else gets a message saying what
    /// it was recognised as. Folders add the maps directly inside them.
    /// </summary>
    public void OpenFiles(IEnumerable<string> paths)
    {
        var maps = new List<string>();
        var others = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                maps.AddRange(Directory.EnumerateFiles(path).Where(MapsPageViewModel.IsMapFile).Order(StringComparer.OrdinalIgnoreCase));
                continue;
            }

            if (!File.Exists(path))
                continue;

            InputKind kind = Detect(path);
            if (InputDetector.IsMap(kind))
                maps.Add(path);
            else
                others.Add(kind == InputKind.Unknown
                    ? $"{Path.GetFileName(path)} isn't a file MomentumPorter recognises"
                    : $"{Path.GetFileName(path)} is a {PortJobFactory.Describe(kind)}; porting those comes in a later phase");
        }

        var messages = new List<string>();
        if (maps.Count > 0)
        {
            int added = Maps.AddFiles(maps).Count;
            SelectedPage = Maps;
            messages.Add(added == maps.Count
                ? $"Added {added} map{(added == 1 ? "" : "s")} to the queue"
                : $"Added {added} map{(added == 1 ? "" : "s")} to the queue ({maps.Count - added} already queued)");
        }

        messages.AddRange(others);
        if (messages.Count > 0)
            StatusMessage = string.Join(". ", messages);
    }

    private static InputKind Detect(string path)
    {
        try
        {
            return InputDetector.Detect(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return InputKind.Unknown;
        }
    }
}
