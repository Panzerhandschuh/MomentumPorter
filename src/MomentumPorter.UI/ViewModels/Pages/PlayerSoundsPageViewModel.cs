namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Material.Icons;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Builds packs with another game's movement sounds (Quake 3, CS:S, CS:GO, TF2, HL2, CS 1.6, Half-Life) into
/// Momentum's custom folder. The pack always sounds as much like the game as it can
/// (<see cref="PlayerSoundPackOptions.ApplyGameAccurate"/>): only the game, its character and the folders are picked here.
/// </summary>
public sealed class PlayerSoundsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly Action<Action> _post;
    private readonly PlayerSoundPackOptions _options = new();
    private SourceGameInfo _selectedGame;
    private string? _selectedCharacter;
    private string _characterLabel = "Character";
    private string? _sourceDescription;
    private SoundPackBuildViewModel? _selectedBuild;
    private int _scan;
    private readonly Lock _previewLock = new();
    private PlayerSoundSource? _previewSource;
    private string? _previewKey;
    private string? _previewStatus;
    private int _preview;

    public PlayerSoundsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post)
    {
        _settings = settings;
        _post = post;
        Runner = runner;
        _options.ApplyGameAccurate();
        _selectedGame = PlayerSoundSources.Find(_options.Game) ?? PlayerSoundSources.Games[0];
        _selectedCharacter = _options.ResolveCharacter();

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, running.Select(x => !x));
        CancelCommand = ReactiveCommand.Create(runner.Cancel, running);
        OpenPackFolderCommand = ReactiveCommand.Create(OpenPackFolder);
        RescanCommand = ReactiveCommand.CreateFromTask(ScanAsync, running.Select(x => !x));
    }

    public override string Title => "Player Sounds";

    public override MaterialIconKind Icon => MaterialIconKind.VolumeHigh;

    public JobRunnerService Runner { get; }

    /// <summary>The options the next build uses: the game and character picked here, everything else game accurate.</summary>
    public PlayerSoundPackOptions Options => _options;

    public IReadOnlyList<SourceGameInfo> Games => PlayerSoundSources.Games;

    /// <summary>The game whose sounds the pack uses. Changing it looks for the game and its characters.</summary>
    public SourceGameInfo SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (value is null || value == _selectedGame)
                return;

            this.RaiseAndSetIfChanged(ref _selectedGame, value);
            SoundPreviewPlayer.Stop();
            PreviewSounds.Clear();
            _options.Game = value.Id;
            _options.Character = null;
            Characters.Clear();
            SelectedCharacter = null;
            _ = ScanAsync();
        }
    }

    /// <summary>Quake 3's characters found in the installed game's pk3s, or CS:GO's teams; empty for the other games.</summary>
    public ObservableCollection<string> Characters { get; } = [];

    /// <summary>What the game's characters are called on the page (Character, Team).</summary>
    public string CharacterLabel
    {
        get => _characterLabel;
        private set => this.RaiseAndSetIfChanged(ref _characterLabel, value);
    }

    /// <summary>Builds made this session, newest first.</summary>
    public ObservableCollection<SoundPackBuildViewModel> Builds { get; } = [];

    public SoundPackBuildViewModel? SelectedBuild
    {
        get => _selectedBuild;
        set => this.RaiseAndSetIfChanged(ref _selectedBuild, value);
    }

    /// <summary>Why the game's sounds weren't found or couldn't be read; null when they were.</summary>
    public string? SourceDescription
    {
        get => _sourceDescription;
        private set => this.RaiseAndSetIfChanged(ref _sourceDescription, value);
    }

    public string? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCharacter, value);
            if (value is not null && !string.Equals(_options.Character, value, StringComparison.OrdinalIgnoreCase))
            {
                _options.Character = value;
                // Each Quake 3 character has its own jump, fall and footstep sounds
                _ = RefreshPreviewAsync();
            }
        }
    }

    public ReactiveCommand<Unit, Unit> BuildCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenPackFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> RescanCommand { get; }

    /// <summary>
    /// Looks for the selected game and lists its characters (Quake 3) or teams (CS:GO). Safe to call again after
    /// changing the folder options.
    /// </summary>
    public async Task ScanAsync()
    {
        SourceGameInfo game = SelectedGame;
        string? path = _options.ResolveGameDirectory();
        int scan = ++_scan;
        (List<string> characters, string? defaultCharacter, string kind, string? description) = await Task.Run(() =>
        {
            try
            {
                string? found = PlayerSoundSources.Locate(game, path);
                if (found is null)
                {
                    return ([], null, "character", game.Engine == ContentEngine.Quake3
                        ? "Quake 3 Arena or Quake Live wasn't found in your Steam libraries"
                        : $"{game.Name} wasn't found in your Steam libraries");
                }

                // Only Quake 3's characters come from its files; opening a Source game reads all its soundscripts
                if (game.Engine != ContentEngine.Quake3)
                {
                    IReadOnlyList<string> teams = SourceGameSoundSource.TeamsOf(game);
                    return (teams.ToList(), teams.FirstOrDefault(), "team", (string?)null);
                }

                using PlayerSoundSource source = PlayerSoundSources.Open(game, found, new Steam.SteamLocator());
                return (source.Characters.ToList(), source.DefaultCharacter, source.CharacterKind, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                return ([], null, "character", $"Couldn't read {game.Name}'s files: {ex.Message}");
            }
        });

        _post(() =>
        {
            // A newer scan (another game picked meanwhile) wins
            if (scan != _scan)
                return;

            Characters.Clear();
            foreach (string character in characters)
                Characters.Add(character);

            CharacterLabel = char.ToUpperInvariant(kind[0]) + kind[1..];
            SourceDescription = description;
            if (characters.Count > 0 && (SelectedCharacter is null || !characters.Contains(SelectedCharacter, StringComparer.OrdinalIgnoreCase)))
                SelectedCharacter = defaultCharacter is not null && characters.Contains(defaultCharacter, StringComparer.OrdinalIgnoreCase) ? defaultCharacter : characters[0];

            _ = RefreshPreviewAsync();
        });
    }

    /// <summary>The sounds the pack would use for the selected game and character, to listen to before building.</summary>
    public ObservableCollection<PreviewSoundViewModel> PreviewSounds { get; } = [];

    /// <summary>Why there's nothing to preview, or that it's loading; null when the sounds are listed.</summary>
    public string? PreviewStatus
    {
        get => _previewStatus;
        private set => this.RaiseAndSetIfChanged(ref _previewStatus, value);
    }

    /// <summary>
    /// Lists the selected game's and character's sounds for the preview. The game's files stay open for playing them
    /// until another game (or folder) is picked.
    /// </summary>
    public async Task RefreshPreviewAsync()
    {
        SourceGameInfo game = SelectedGame;
        string? character = SelectedCharacter;
        string? directory = _options.ResolveGameDirectory();
        string? momentumDirectory = _settings.Settings.MomentumDirectory;
        PlayerSoundPackOptions options = OptionsPreset.Clone(_options);
        options.ApplyGameAccurate();
        int preview = ++_preview;
        PreviewSounds.Clear();
        PreviewStatus = $"Reading {game.Name}'s sounds...";

        (IReadOnlyList<PreviewSound> sounds, string? status) = await Task.Run(() =>
        {
            lock (_previewLock)
            {
                try
                {
                    if (OpenPreviewSource(game, directory, momentumDirectory) is not { } source)
                        return ([], $"{game.Name} wasn't found, so there's nothing to preview");

                    IReadOnlyList<PreviewSound> list = PlayerSoundPreview.List(source, character, options);
                    return (list, list.Count == 0 ? $"None of {game.Name}'s movement sounds could be read" : null);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    return ((IReadOnlyList<PreviewSound>)[], $"Couldn't read {game.Name}'s sounds: {ex.Message}");
                }
            }
        });

        _post(() =>
        {
            // A newer refresh (another game or character picked meanwhile) wins
            if (preview != _preview)
                return;

            PreviewSounds.Clear();
            foreach (PreviewSound sound in sounds)
                PreviewSounds.Add(new PreviewSoundViewModel(sound, PlayAsync));

            PreviewStatus = status;
        });
    }

    /// <summary>Plays the sound's next file, reading it from the game on a worker thread.</summary>
    public async Task PlayAsync(PreviewSoundViewModel item)
    {
        (GameSoundFile file, _) = item.TakeNextFile();
        (byte[]? bytes, string? problem) = await Task.Run(() =>
        {
            lock (_previewLock)
            {
                if (_previewSource is null)
                    return (null, "The game's files aren't open; pick the game again");

                try
                {
                    byte[]? read = _previewSource.ReadSound(file.Source, out string? why);
                    return (read, why ?? (read is null ? $"{file.Source} couldn't be read" : null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
                {
                    return ((byte[]?)null, ex.Message);
                }
            }
        });

        // Only a sound that can't be played says anything
        string? error = bytes is null ? problem : SoundPreviewPlayer.Play(bytes);
        item.IsError = error is not null;
        item.Status = error;
    }

    // Opens the game's sounds, or keeps the ones already open for the same game and folders. Call under _previewLock.
    private PlayerSoundSource? OpenPreviewSource(SourceGameInfo game, string? directory, string? momentumDirectory)
    {
        string key = $"{game.Id}|{directory}|{momentumDirectory}";
        if (_previewSource is not null && _previewKey == key)
            return _previewSource;

        _previewSource?.Dispose();
        _previewSource = null;
        _previewKey = null;

        var steam = new Steam.SteamLocator();
        string? found = PlayerSoundSources.Locate(game, directory, steam);
        if (found is null)
            return null;

        // Source games match footsteps through Momentum's surfaces, as the pack does
        SurfaceProperties? surfaces = null;
        if (game.Engine == ContentEngine.Source && PlayerSoundPackJob.FindMomentumContent(momentumDirectory, steam) is { } content)
        {
            surfaces = SurfaceProperties.Load(path =>
            {
                string file = Path.Combine(content, path.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(file) ? File.ReadAllText(file) : null;
            });
        }

        _previewSource = PlayerSoundSources.Open(game, found, steam, surfaces);
        _previewKey = key;
        return _previewSource;
    }

    public async Task BuildAsync()
    {
        var build = new SoundPackBuildViewModel(_options.ResolveCharacter(), _options.ResolvePackName());
        Builds.Insert(0, build);
        SelectedBuild = build;
        await Runner.RunAsync(new List<SoundPackBuildViewModel> { build }, Prepare);
    }

    /// <summary>
    /// Builds the job for a pack with a snapshot of the current options and settings.
    /// </summary>
    public PreparedJob Prepare(SoundPackBuildViewModel build)
    {
        PlayerSoundPackOptions options = OptionsPreset.Clone(_options);
        options.ApplyGameAccurate();
        PorterSettings settings = _settings.Settings.Clone();
        if (string.IsNullOrWhiteSpace(options.MomentumDirectory) && !string.IsNullOrWhiteSpace(settings.MomentumDirectory))
            options.MomentumDirectory = settings.MomentumDirectory;

        (PortJob job, PlayerSoundPackState state) = PlayerSoundPackJob.Create(options);
        string workDirectory = PortJob.CreateWorkDirectory(options.ResolvePackName());

        return new PreparedJob(
            job,
            workDirectory,
            context =>
            {
                context.Set(state);
                context.Set(settings);
            },
            (context, result) =>
            {
                if (!result.Succeeded)
                {
                    return result.Exception is OperationCanceledException
                        ? new JobOutcome(QueueStatus.Skipped, "Cancelled")
                        : new JobOutcome(QueueStatus.Failed, $"{result.FailedStep} failed: {result.Error}");
                }

                string? warnings = context.Report.HasWarnings ? " (see the report)" : null;
                CustomPackOutput? output = context.TryGet<CustomPackOutput>();
                return new JobOutcome(QueueStatus.Done, $"Pack written{warnings}", OutputPath: output?.Path);
            });
    }

    private void OpenPackFolder()
    {
        if (SelectedBuild?.OutputPath is { } output && (File.Exists(output) || Directory.Exists(output)))
        {
            if (File.Exists(output))
                ShellService.RevealFile(output);
            else
                ShellService.OpenFolder(output);

            return;
        }

        string? content = _settings.Settings.ResolveMomentumContentDirectory();
        if (content is not null)
            ShellService.OpenFolder(Path.Combine(content, "custom"));
    }
}

/// <summary>
/// One sound pack build, with its log and report.
/// </summary>
/// <param name="character">The Quake 3 character or CS:GO team, or null for games with one set of sounds</param>
public sealed class SoundPackBuildViewModel(string? character, string packName) : QueueItemViewModel
{
    public string? Character { get; } = character;

    public string PackName { get; } = packName;

    public override string Title => Character is null ? PackName : $"{PackName} ({Character})";
}
