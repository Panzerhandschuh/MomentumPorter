namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Material.Icons;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Builds weapon packs: a weapon from another game in one of Momentum's weapon slots, written to Momentum's custom folder.
/// </summary>
public sealed class WeaponsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly WeaponPackOptions _options = new();
    private CatalogGame? _selectedGame;
    private CatalogWeapon? _selectedWeapon;
    private string? _selectedSlot;
    private WeaponPackBuildViewModel? _selectedBuild;

    public WeaponsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post, WeaponCatalog? catalog = null)
    {
        _settings = settings;
        Runner = runner;
        Catalog = catalog ?? WeaponCatalog.Default;
        // The page picks the game, weapon and slot with its own lists
        OptionsForm = new OptionsFormViewModel(_options, exclude: [nameof(WeaponPackOptions.Game), nameof(WeaponPackOptions.Weapon), nameof(WeaponPackOptions.Slot)]);
        Games = Catalog.Games;

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        IObservable<bool> canBuild = this.WhenAnyValue(x => x.SelectedWeapon, x => x.Runner.IsRunning, (weapon, isRunning) => weapon is not null && !isRunning);
        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, canBuild);
        CancelCommand = ReactiveCommand.Create(runner.Cancel, running);
        OpenPackFolderCommand = ReactiveCommand.Create(OpenPackFolder);

        SelectedGame = Games.FirstOrDefault(x => x.Id.Equals(_options.Game, StringComparison.OrdinalIgnoreCase)) ?? Games.FirstOrDefault();
    }

    public override string Title => "Weapons";

    public override MaterialIconKind Icon => MaterialIconKind.Pistol;

    public JobRunnerService Runner { get; }

    public WeaponCatalog Catalog { get; }

    public OptionsFormViewModel OptionsForm { get; }

    public IReadOnlyList<CatalogGame> Games { get; }

    public ObservableCollection<CatalogWeapon> Weapons { get; } = [];

    /// <summary>The Momentum weapons the selected weapon fits, best fit first.</summary>
    public ObservableCollection<string> Slots { get; } = [];

    /// <summary>Builds made this session, newest first.</summary>
    public ObservableCollection<WeaponPackBuildViewModel> Builds { get; } = [];

    public CatalogGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGame, value);
            this.RaisePropertyChanged(nameof(GameLimitations));
            Weapons.Clear();
            if (value is null)
                return;

            _options.Game = value.Id;
            foreach (CatalogWeapon weapon in value.Weapons.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                Weapons.Add(weapon);

            SelectedWeapon = Weapons.FirstOrDefault();
            OptionsForm.Reload();
        }
    }

    public CatalogWeapon? SelectedWeapon
    {
        get => _selectedWeapon;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedWeapon, value);
            Slots.Clear();
            _options.Weapon = value?.Id ?? "";
            if (value is not null)
            {
                foreach (string slot in value.Slots)
                    Slots.Add(slot);
            }

            SelectedSlot = Slots.FirstOrDefault();
            OptionsForm.Reload();
        }
    }

    public string? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSlot, value);
            _options.Slot = value;
            OptionsForm.Reload();
        }
    }

    /// <summary>What the selected game can't reproduce, from the catalog.</summary>
    public string? GameLimitations => SelectedGame is { Limitations.Count: > 0 } game
        ? string.Join(" ", game.Limitations.Select(x => x.TrimEnd().EndsWith('.') ? x.Trim() : x.Trim() + "."))
        : null;

    private bool _gameNotesExpanded;

    /// <summary>Whether the game's notes show in full, or clipped to a couple of lines so the weapon list has room.</summary>
    public bool GameNotesExpanded
    {
        get => _gameNotesExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _gameNotesExpanded, value);
            this.RaisePropertyChanged(nameof(GameNotesMaxLines));
        }
    }

    /// <summary>The notes' line limit: 0 (none) when expanded.</summary>
    public int GameNotesMaxLines => GameNotesExpanded ? 0 : 2;

    public WeaponPackBuildViewModel? SelectedBuild
    {
        get => _selectedBuild;
        set => this.RaiseAndSetIfChanged(ref _selectedBuild, value);
    }

    public ReactiveCommand<Unit, Unit> BuildCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenPackFolderCommand { get; }

    public async Task BuildAsync()
    {
        if (SelectedWeapon is null)
            return;

        var build = new WeaponPackBuildViewModel($"{SelectedGame?.Name}: {SelectedWeapon.Name} as {SelectedSlot ?? "best fit"}");
        Builds.Insert(0, build);
        SelectedBuild = build;
        await Runner.RunAsync(new List<WeaponPackBuildViewModel> { build }, Prepare);
    }

    /// <summary>
    /// Builds the job with a snapshot of the options. Packs default to Momentum's custom folder, which the game loads from.
    /// </summary>
    public PreparedJob Prepare(WeaponPackBuildViewModel build)
    {
        WeaponPackOptions options = OptionsPreset.Clone(_options);
        options.Game = SelectedGame?.Id ?? options.Game;
        options.Weapon = SelectedWeapon?.Id ?? options.Weapon;
        options.Slot = SelectedSlot ?? options.Slot;
        PorterSettings settings = _settings.Settings.Clone();

        if (string.IsNullOrWhiteSpace(options.MomentumDirectory) && !string.IsNullOrWhiteSpace(settings.MomentumDirectory))
            options.MomentumDirectory = settings.MomentumDirectory;

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            string? content = settings.ResolveMomentumContentDirectory();
            options.OutputDirectory = content is not null
                ? Path.Combine(content, "custom")
                : Path.Combine(settings.ResolveOutputDirectory(), "custom");
        }

        (PortJob job, WeaponPackState state) = WeaponPackBuilder.CreateJob(options);
        string workDirectory = PortJob.CreateWorkDirectory($"weapon_{options.Game}_{options.Weapon}");

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
                state.Dispose();
                if (!result.Succeeded)
                {
                    return result.Exception is OperationCanceledException
                        ? new JobOutcome(QueueStatus.Skipped, "Cancelled")
                        : new JobOutcome(QueueStatus.Failed, $"{result.FailedStep} failed: {result.Error}");
                }

                string? warnings = context.Report.HasWarnings ? " (see the report)" : null;
                WeaponPackOutput? output = context.TryGet<WeaponPackOutput>();
                return new JobOutcome(QueueStatus.Done, $"Pack written{warnings}", OutputPath: output?.PackDirectory);
            });
    }

    private void OpenPackFolder()
    {
        if (SelectedBuild?.OutputPath is { } output && Directory.Exists(output))
        {
            ShellService.OpenFolder(output);
            return;
        }

        string? content = _settings.Settings.ResolveMomentumContentDirectory();
        if (content is not null)
            ShellService.OpenFolder(Path.Combine(content, "custom"));
    }
}

public sealed class WeaponPackBuildViewModel(string title) : QueueItemViewModel
{
    public override string Title { get; } = title;
}
