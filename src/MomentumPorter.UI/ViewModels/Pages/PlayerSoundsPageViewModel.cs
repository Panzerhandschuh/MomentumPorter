namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Material.Icons;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Builds Quake 3 movement sound packs into Momentum's custom folder.
/// </summary>
public sealed class PlayerSoundsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly Action<Action> _post;
    private readonly PlayerSoundPackOptions _options = new();
    private string? _selectedCharacter;
    private string? _sourceDescription;
    private SoundPackBuildViewModel? _selectedBuild;

    public PlayerSoundsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post)
    {
        _settings = settings;
        _post = post;
        Runner = runner;
        // The page picks the character from the installed game's list
        OptionsForm = new OptionsFormViewModel(_options, exclude: [nameof(PlayerSoundPackOptions.Character)]);
        _selectedCharacter = _options.Character;

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

    /// <summary>Quake 3 characters found in the installed game's pk3s.</summary>
    public ObservableCollection<string> Characters { get; } = [];

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
    /// Looks for Quake 3 (or Quake Live) and lists its characters. Safe to call again after changing the folder option.
    /// </summary>
    public async Task ScanAsync()
    {
        string? path = string.IsNullOrWhiteSpace(_options.Quake3Path) ? null : _options.Quake3Path;
        (List<string> characters, string description) = await Task.Run(() =>
        {
            try
            {
                string? source = path ?? Quake3SoundSource.FindInstalled();
                if (source is null)
                    return ([], "Quake 3 Arena or Quake Live wasn't found in your Steam libraries. Set the Quake 3 folder under Game folders.");

                using Quake3SoundSource q3 = Quake3SoundSource.Open(source);
                return (q3.Characters.ToList(), $"{q3.Characters.Count} characters from {q3.Path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return ([], $"Couldn't read Quake 3's files: {ex.Message}");
            }
        });

        _post(() =>
        {
            Characters.Clear();
            foreach (string character in characters)
                Characters.Add(character);

            SourceDescription = description;
            if (characters.Count > 0 && (SelectedCharacter is null || !characters.Contains(SelectedCharacter, StringComparer.OrdinalIgnoreCase)))
                SelectedCharacter = characters.Contains("ranger", StringComparer.OrdinalIgnoreCase) ? "ranger" : characters[0];
        });
    }

    public async Task BuildAsync()
    {
        var build = new SoundPackBuildViewModel(_options.Character, _options.ResolvePackName());
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
public sealed class SoundPackBuildViewModel(string character, string packName) : QueueItemViewModel
{
    public string Character { get; } = character;

    public string PackName { get; } = packName;

    public override string Title => $"{PackName} ({Character})";
}
