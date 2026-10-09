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
/// Momentum's custom folder.
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

    public PlayerSoundsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post)
    {
        _settings = settings;
        _post = post;
        Runner = runner;
        // The page picks the game, and the character from the installed game's list
        OptionsForm = new OptionsFormViewModel(_options, exclude: [nameof(PlayerSoundPackOptions.Game), nameof(PlayerSoundPackOptions.Character)]);
        _selectedGame = PlayerSoundSources.Find(_options.Game) ?? PlayerSoundSources.Games[0];
        _selectedCharacter = _options.ResolveCharacter();
        FilterOptions();

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, running.Select(x => !x));
        CancelCommand = ReactiveCommand.Create(runner.Cancel, running);
        OpenPackFolderCommand = ReactiveCommand.Create(OpenPackFolder);
        RescanCommand = ReactiveCommand.CreateFromTask(ScanAsync, running.Select(x => !x));
    }

    public override string Title => "Player Sounds";

    public override MaterialIconKind Icon => MaterialIconKind.VolumeHigh;

    public JobRunnerService Runner { get; }

    public OptionsFormViewModel OptionsForm { get; }

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
            _options.Game = value.Id;
            _options.Character = null;
            Characters.Clear();
            SelectedCharacter = null;
            FilterOptions();
            OptionsForm.Reload();
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

    /// <summary>Where the sounds come from, or why none were found.</summary>
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
                OptionsForm.Reload();
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
        (List<string> characters, string? defaultCharacter, string kind, string description) = await Task.Run(() =>
        {
            try
            {
                string? found = PlayerSoundSources.Locate(game, path);
                if (found is null)
                {
                    return ([], null, "character", game.Engine == ContentEngine.Quake3
                        ? "Quake 3 Arena or Quake Live wasn't found in your Steam libraries. Set the Quake 3 folder under Game folders."
                        : $"{game.Name} wasn't found in your Steam libraries. Set the game folder under Game folders.");
                }

                // Only Quake 3's characters come from its files; opening a Source game reads all its soundscripts
                if (game.Engine != ContentEngine.Quake3)
                {
                    IReadOnlyList<string> teams = SourceGameSoundSource.TeamsOf(game);
                    return (teams.ToList(), teams.FirstOrDefault(), "team", $"Sounds from {found}");
                }

                using PlayerSoundSource source = PlayerSoundSources.Open(game, found, new Steam.SteamLocator());
                return (source.Characters.ToList(), source.DefaultCharacter, source.CharacterKind, $"{source.Characters.Count} characters from {source.Path}");
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
        });
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

    // Quake 3's footstep types and folder only for Quake 3, the game folder for the others
    private void FilterOptions()
    {
        bool quake3 = SelectedGame.Engine == ContentEngine.Quake3;
        OptionsForm.SetFilter(x => quake3 ? !PlayerSoundPackOptions.NotQuake3.Contains(x.Name) : !PlayerSoundPackOptions.Quake3Only.Contains(x.Name));
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
