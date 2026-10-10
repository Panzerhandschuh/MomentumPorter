namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using Material.Icons;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.UI.Services;
using ReactiveUI;

/// <summary>A game the porter knows about, and whether it's installed.</summary>
public sealed record DetectedGame(string Name, string? Path, bool Found)
{
    public string PathText => Path ?? "not found";
}

/// <summary>
/// Where Momentum, the games, studiomdl and the folders are. Every change is saved straight away.
/// </summary>
public sealed class SettingsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _post;
    private string? _detectedMomentum;
    private string? _detectedMomentumMaps;
    private string? _detectedKreedzClimbing;
    private string? _detectedKreedzClimbingContent;
    private string? _saveError;
    private bool _isScanning;

    public SettingsPageViewModel(SettingsService settings, IDialogService dialogs, Action<Action> post)
    {
        _settings = settings;
        _dialogs = dialogs;
        _post = post;
        _saveError = settings.LoadError;

        BrowseMomentumCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _dialogs.PickFolderAsync("Momentum Mod's install folder") is { } folder)
                MomentumDirectory = folder;
        });
        BrowseKreedzClimbingCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _dialogs.PickFolderAsync("Kreedz Climbing's install folder") is { } folder)
                KreedzClimbingDirectory = folder;
        });
        BrowseStudiomdlCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IReadOnlyList<string> files = await _dialogs.PickFilesAsync("studiomdl executable", false, new FileFilter("studiomdl", "studiomdl.exe", "*"));
            if (files.Count > 0)
                StudiomdlPath = files[0];
        });
        BrowseWorkDirectoryCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _dialogs.PickFolderAsync("Work folder") is { } folder)
                WorkDirectory = folder;
        });
        BrowseOutputDirectoryCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _dialogs.PickFolderAsync("Output folder") is { } folder)
                OutputDirectory = folder;
        });
        RescanCommand = ReactiveCommand.CreateFromTask(ScanAsync);
    }

    public override string Title => "Settings";

    public override MaterialIconKind Icon => MaterialIconKind.Cog;

    public PorterSettings Settings => _settings.Settings;

    public string SettingsFile => _settings.FilePath;

    public string? MomentumDirectory
    {
        get => Settings.MomentumDirectory;
        set => Update(() => Settings.MomentumDirectory = Blank(value), nameof(MomentumDirectory), nameof(MomentumStatus), nameof(MomentumMapsDirectory), nameof(DefaultStudiomdlPath), nameof(StudiomdlStatus));
    }

    /// <summary>Momentum found in the Steam libraries, used when there's no override.</summary>
    public string? DetectedMomentum
    {
        get => _detectedMomentum;
        private set
        {
            this.RaiseAndSetIfChanged(ref _detectedMomentum, value);
            this.RaisePropertyChanged(nameof(MomentumStatus));
            this.RaisePropertyChanged(nameof(MomentumMapsDirectory));
            this.RaisePropertyChanged(nameof(DefaultStudiomdlPath));
            this.RaisePropertyChanged(nameof(StudiomdlStatus));
        }
    }

    public string? MomentumMapsDirectory => Settings.MomentumDirectory is null
        ? _detectedMomentumMaps
        : Settings.ResolveMomentumMapsDirectory();

    public string MomentumStatus => MomentumMapsDirectory is { } maps
        ? $"Maps are installed into {maps}"
        : Settings.MomentumDirectory is null
            ? IsScanning ? "Looking for Momentum Mod..." : "Momentum Mod wasn't found in your Steam libraries"
            : "That folder doesn't look like a Momentum Mod install";

    public string? KreedzClimbingDirectory
    {
        get => Settings.KreedzClimbingDirectory;
        set => Update(() => Settings.KreedzClimbingDirectory = Blank(value), nameof(KreedzClimbingDirectory), nameof(KreedzClimbingStatus));
    }

    /// <summary>Kreedz Climbing found in the Steam libraries, used when there's no override.</summary>
    public string? DetectedKreedzClimbing
    {
        get => _detectedKreedzClimbing;
        private set
        {
            this.RaiseAndSetIfChanged(ref _detectedKreedzClimbing, value);
            this.RaisePropertyChanged(nameof(KreedzClimbingStatus));
        }
    }

    public string KreedzClimbingStatus
    {
        get
        {
            if (Settings.KreedzClimbingDirectory is not null)
            {
                return Settings.ResolveKreedzClimbingContentDirectory() is { } content
                    ? $"KZ maps' assets are packed from {content}"
                    : "That folder doesn't look like a Kreedz Climbing install";
            }

            if (_detectedKreedzClimbingContent is { } detected)
                return $"KZ maps' assets are packed from {detected}";

            return IsScanning ? "Looking for Kreedz Climbing..." : "Kreedz Climbing wasn't found in your Steam libraries; it's needed to pack KZ maps' assets";
        }
    }

    public string? StudiomdlPath
    {
        get => Settings.StudiomdlPath;
        set => Update(() => Settings.StudiomdlPath = Blank(value), nameof(StudiomdlPath), nameof(StudiomdlStatus));
    }

    /// <summary>Momentum's studiomdl (through the folder override when there is one), used when there's no override of its own.</summary>
    public string? DefaultStudiomdlPath => new PorterSettings { MomentumDirectory = Settings.MomentumDirectory }.ResolveStudiomdlPath();

    public string StudiomdlStatus
    {
        get
        {
            if (Settings.ResolveStudiomdlPath() is not { } path)
                return "studiomdl wasn't found; it comes with Momentum Mod (bin/win64), so set Momentum's folder or its location to convert GoldSrc and Quake 3 models";

            return File.Exists(path) ? $"Using {path}" : $"{path} doesn't exist";
        }
    }

    public string? WorkDirectory
    {
        get => Settings.WorkDirectory;
        set => Update(() => Settings.WorkDirectory = Blank(value), nameof(WorkDirectory));
    }

    public string DefaultWorkDirectory => Path.Combine(Path.GetTempPath(), "MomentumPorter");

    public string? OutputDirectory
    {
        get => Settings.OutputDirectory;
        set => Update(() => Settings.OutputDirectory = Blank(value), nameof(OutputDirectory));
    }

    public string DefaultOutputDirectory => PorterSettings.DefaultOutputDirectory;

    public ObservableCollection<DetectedGame> Games { get; } = [];

    public bool IsScanning
    {
        get => _isScanning;
        private set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    public string? SaveError
    {
        get => _saveError;
        private set => this.RaiseAndSetIfChanged(ref _saveError, value);
    }

    public ReactiveCommand<Unit, Unit> BrowseMomentumCommand { get; }

    public ReactiveCommand<Unit, Unit> BrowseKreedzClimbingCommand { get; }

    public ReactiveCommand<Unit, Unit> BrowseStudiomdlCommand { get; }

    public ReactiveCommand<Unit, Unit> BrowseWorkDirectoryCommand { get; }

    public ReactiveCommand<Unit, Unit> BrowseOutputDirectoryCommand { get; }

    public ReactiveCommand<Unit, Unit> RescanCommand { get; }

    /// <summary>
    /// Looks for Steam and every known game on a worker thread.
    /// </summary>
    public async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            (List<DetectedGame> games, GameLocation? momentum, GameLocation? kreedzClimbing) = await Task.Run(() =>
            {
                var steam = new SteamLocator();
                var games = KnownGame.All
                    .Select(game => steam.Find(game) is { } location
                        ? new DetectedGame(game.Name, location.ContentDirectory ?? location.InstallDirectory, true)
                        : new DetectedGame(game.Name, null, false))
                    .ToList();
                return (games, steam.FindMomentum(), steam.Find(KnownGame.KreedzClimbing));
            });

            _post(() =>
            {
                Games.Clear();
                foreach (DetectedGame game in games)
                    Games.Add(game);

                _detectedMomentumMaps = momentum?.ContentDirectory is { } content ? Path.Combine(content, "maps") : null;
                DetectedMomentum = momentum?.InstallDirectory;
                _detectedKreedzClimbingContent = kreedzClimbing?.ContentDirectory;
                DetectedKreedzClimbing = kreedzClimbing?.InstallDirectory;
            });
        }
        finally
        {
            _post(() =>
            {
                IsScanning = false;
                this.RaisePropertyChanged(nameof(MomentumStatus));
                this.RaisePropertyChanged(nameof(KreedzClimbingStatus));
            });
        }
    }

    private void Update(Action change, params string[] properties)
    {
        change();
        foreach (string property in properties)
            this.RaisePropertyChanged(property);

        SaveError = _settings.Save();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
