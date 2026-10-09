namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Material.Icons;
using MomentumPorter.Assets.Vpk;
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
    private readonly Action<Action> _post;
    private readonly WeaponPackOptions _options = new();
    private CatalogGame? _selectedGame;
    private CatalogWeapon? _selectedWeapon;
    private string? _selectedSlot;
    private WeaponPackBuildViewModel? _selectedBuild;
    private bool _allModes = true;
    private string? _slotAssignments;

    public WeaponsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post, WeaponCatalog? catalog = null)
    {
        _settings = settings;
        _post = post;
        Runner = runner;
        Catalog = catalog ?? WeaponCatalog.Default;
        // The page picks the game, weapon, slot and modes with its own lists
        OptionsForm = new OptionsFormViewModel(_options, exclude: [nameof(WeaponPackOptions.Game), nameof(WeaponPackOptions.Weapon), nameof(WeaponPackOptions.Slot), nameof(WeaponPackOptions.GameModes)]);
        Games = Catalog.Games;
        foreach (GameMode mode in GameModes.All)
        {
            var choice = new GameModeChoice(mode);
            choice.WhenAnyValue(x => x.IsSelected).Subscribe(_ => this.RaisePropertyChanged(nameof(ModesSummary)));
            ModeChoices.Add(choice);
        }

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        IObservable<bool> canBuild = this.WhenAnyValue(x => x.SelectedWeapon, x => x.Runner.IsRunning, (weapon, isRunning) => weapon is not null && !isRunning);
        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, canBuild);
        IObservable<bool> canBuildAll = this.WhenAnyValue(x => x.SelectedGame, x => x.Runner.IsRunning, (game, isRunning) => game is not null && !isRunning);
        BuildAllCommand = ReactiveCommand.CreateFromTask(BuildAllAsync, canBuildAll);
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

    /// <summary>The game modes to tick when the weapon isn't for every mode.</summary>
    public ObservableCollection<GameModeChoice> ModeChoices { get; } = [];

    /// <summary>Whether the weapon is used in every mode (the plain script), or only in the ticked ones.</summary>
    public bool AllModes
    {
        get => _allModes;
        set
        {
            this.RaiseAndSetIfChanged(ref _allModes, value);
            this.RaisePropertyChanged(nameof(ModesSummary));
        }
    }

    /// <summary>The mode picker's face: "Every mode", or the ticked modes' names.</summary>
    public string ModesSummary
    {
        get
        {
            if (AllModes)
                return "Every mode";

            var picked = ModeChoices.Where(x => x.IsSelected).Select(x => x.Name).ToList();
            return picked.Count == 0 ? "No mode: tick some" : string.Join(", ", picked);
        }
    }

    /// <summary>The ticked modes as the option's list, or null for every mode.</summary>
    public string? SelectedModes => AllModes ? null : GameModes.ToList(ModeChoices.Where(x => x.IsSelected).Select(x => x.Mode));

    /// <summary>What the pack folder in Momentum's custom folder holds for the selected slot, per mode.</summary>
    public string? SlotAssignments
    {
        get => _slotAssignments;
        private set => this.RaiseAndSetIfChanged(ref _slotAssignments, value);
    }

    public CatalogGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGame, value);
            this.RaisePropertyChanged(nameof(GameLimitations));
            this.RaisePropertyChanged(nameof(BuildAllTip));
            Weapons.Clear();
            if (value is null)
                return;

            _options.Game = value.Id;
            // Only Quake 3 has the Quake 3 options; GoldSrc world models aren't fitted (nothing marks how they're held)
            bool quake3 = value.Id.Equals("q3", StringComparison.OrdinalIgnoreCase);
            bool goldSrc = SourceGames.Find(value.Id)?.Engine == ContentEngine.GoldSrc;
            OptionsForm.SetFilter(x => (quake3 || x.Info.Attribute.Group != WeaponPackOptions.Quake3Group)
                && (!goldSrc || x.Name != nameof(WeaponPackOptions.FitWorldModel)));
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
            RefreshSlotAssignments();
        }
    }

    /// <summary>Reads the pack folder the builds go to and describes what it has for the selected slot.</summary>
    public void RefreshSlotAssignments()
    {
        SlotAssignments = SelectedSlot is { } slotId && WeaponSlots.Find(slotId) is { } slot
            ? DescribeAssignments(slot, WeaponPackInventory.Read(Path.Combine(OutputDirectory(_settings.Settings), SanitizedPackName())))
            : null;
    }

    private static string? DescribeAssignments(WeaponSlot slot, IReadOnlyList<WeaponPackEntry> inventory)
    {
        var lines = inventory
            .Where(x => x.Slot == slot)
            .OrderBy(x => x.Mode is null ? -1 : GameModes.All.ToList().IndexOf(x.Mode))
            .Select(x => $"{(x.Mode is null ? "Every other mode" : x.Mode.Name)}: {x.Description}")
            .ToList();
        if (lines.Count == 0)
            return null;

        if (!lines[0].StartsWith("Every other mode", StringComparison.Ordinal))
            return "In the pack now: " + string.Join("; ", lines);

        return "In the pack now: " + string.Join("; ", lines.Skip(1).Append(lines.Count == 1 ? lines[0].Replace("Every other mode", "every mode") : lines[0].Replace("Every other mode", "every other mode")));
    }

    private string SanitizedPackName() => string.IsNullOrWhiteSpace(_options.PackName) ? WeaponPackBuilder.SharedPackName : _options.PackName.Trim();

    /// <summary>Where packs go: Momentum's custom folder, or the output folder's custom/ when Momentum isn't found.</summary>
    private static string OutputDirectory(PorterSettings settings)
    {
        string? content = settings.ResolveMomentumContentDirectory();
        return content is not null
            ? Path.Combine(content, "custom")
            : Path.Combine(settings.ResolveOutputDirectory(), "custom");
    }

    /// <summary>What the selected game can't reproduce, from the catalog.</summary>
    public string? GameLimitations => SelectedGame is { Limitations.Count: > 0 } game
        ? string.Join(" ", game.Limitations.Select(x => x.TrimEnd().EndsWith('.') ? x.Trim() : x.Trim() + "."))
        : null;

    /// <summary>What Build all weapons builds for the selected game.</summary>
    public string? BuildAllTip => SelectedGame is { } game
        ? $"Build every {game.Name} weapon into the Momentum weapon it fits best, with these options and modes: "
            + string.Join(", ", game.FullLoadout().Select(x => $"{x.Weapon.Name} as {x.Slot.Name}"))
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

    public ReactiveCommand<Unit, Unit> BuildAllCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenPackFolderCommand { get; }

    public async Task BuildAsync()
    {
        if (SelectedWeapon is null)
            return;

        var build = new WeaponPackBuildViewModel(
            $"{SelectedGame?.Name}: {SelectedWeapon.Name} as {SelectedSlot ?? "best fit"}{ModesSuffix()}",
            SnapshotOptions(SelectedWeapon.Id, SelectedSlot));
        Builds.Insert(0, build);
        SelectedBuild = build;
        await Runner.RunAsync(new List<WeaponPackBuildViewModel> { build }, Prepare);
    }

    /// <summary>
    /// Builds every weapon of the selected game into the Momentum weapon it fits best (<see cref="CatalogGame.FullLoadout"/>),
    /// one after another into the same pack folder.
    /// </summary>
    public async Task BuildAllAsync()
    {
        if (SelectedGame is not { } game)
            return;

        string modes = ModesSuffix();
        var batch = game.FullLoadout()
            .Select(x => new WeaponPackBuildViewModel($"{game.Name}: {x.Weapon.Name} as {x.Slot.Name}{modes}", SnapshotOptions(x.Weapon.Id, x.Slot.Id)))
            .ToList();
        if (batch.Count == 0)
            return;

        // The list is newest first; insert the batch so it reads top to bottom in the order it builds
        for (int i = batch.Count - 1; i >= 0; i--)
            Builds.Insert(0, batch[i]);

        SelectedBuild = batch[0];
        await Runner.RunAsync(batch, Prepare);

        // Cancel leaves the rest queued, and nothing would run them
        foreach (WeaponPackBuildViewModel build in batch.Where(x => x.Status == QueueStatus.Queued))
            build.Finish(QueueStatus.Skipped, "Cancelled");
    }

    private string ModesSuffix() => AllModes ? "" : $" in {GameModes.Describe(GameModes.Parse(SelectedModes))}";

    /// <summary>The page's options for one weapon, copied so later changes on the page don't touch a queued build.</summary>
    private WeaponPackOptions SnapshotOptions(string weapon, string? slot)
    {
        WeaponPackOptions options = OptionsPreset.Clone(_options);
        options.Game = SelectedGame?.Id ?? options.Game;
        options.Weapon = weapon;
        options.Slot = slot;
        options.GameModes = SelectedModes;
        return options;
    }

    /// <summary>
    /// Builds the job with the build's options (the page's current ones if it has none). Packs default to Momentum's
    /// custom folder, which the game loads from.
    /// </summary>
    public PreparedJob Prepare(WeaponPackBuildViewModel build)
    {
        WeaponPackOptions options = build.Options is { } queued
            ? OptionsPreset.Clone(queued)
            : SnapshotOptions(SelectedWeapon?.Id ?? _options.Weapon, SelectedSlot ?? _options.Slot);
        PorterSettings settings = _settings.Settings.Clone();

        if (string.IsNullOrWhiteSpace(options.MomentumDirectory) && !string.IsNullOrWhiteSpace(settings.MomentumDirectory))
            options.MomentumDirectory = settings.MomentumDirectory;

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            options.OutputDirectory = OutputDirectory(settings);

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
                _post(RefreshSlotAssignments);
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

/// <param name="options">What it builds, copied from the page when it was queued; null builds the page's current choice</param>
public sealed class WeaponPackBuildViewModel(string title, WeaponPackOptions? options = null) : QueueItemViewModel
{
    public override string Title { get; } = title;

    public WeaponPackOptions? Options { get; } = options;
}

/// <summary>A game mode in the Weapons page's mode list.</summary>
public sealed class GameModeChoice(GameMode mode) : ReactiveObject
{
    private bool _isSelected;

    public GameMode Mode { get; } = mode;

    public string Name => Mode.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
