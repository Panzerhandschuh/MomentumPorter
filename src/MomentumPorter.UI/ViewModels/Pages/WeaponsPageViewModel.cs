namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using Material.Icons;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Builds weapon packs, game mode first: pick a Momentum game mode and one of its weapons, then the game and the
/// weapon of it that replaces it. The pack is written to Momentum's custom folder with the porter's defaults.
/// </summary>
public sealed class WeaponsPageViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly Action<Action> _post;
    private GameModeGroup _selectedMode;
    private MomentumWeaponViewModel? _selectedSlot;
    private CatalogGame? _selectedGame;
    private CatalogWeapon? _selectedWeapon;
    private WeaponPackBuildViewModel? _selectedBuild;

    public WeaponsPageViewModel(SettingsService settings, JobRunnerService runner, Action<Action> post, WeaponCatalog? catalog = null)
    {
        _settings = settings;
        _post = post;
        Runner = runner;
        Catalog = catalog ?? WeaponCatalog.Default;
        Games = Catalog.Games;
        Modes = GameModes.Groups;
        _selectedMode = Modes[0];
        _selectedGame = DefaultGameFor(_selectedMode);

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        IObservable<bool> canBuild = this.WhenAnyValue(x => x.SelectedWeapon, x => x.SelectedSlot, x => x.Runner.IsRunning, (weapon, slot, isRunning) => weapon is not null && slot is not null && !isRunning);
        BuildCommand = ReactiveCommand.CreateFromTask(BuildAsync, canBuild);
        IObservable<bool> canBuildAll = this.WhenAnyValue(x => x.SelectedGame, x => x.SelectedMode, x => x.Runner.IsRunning, (game, mode, isRunning) => game is not null && !isRunning && game.LoadoutFor(mode.Slots, mode.PicksFor(game.Id)).Count > 0);
        BuildAllCommand = ReactiveCommand.CreateFromTask(BuildAllAsync, canBuildAll);
        CancelCommand = ReactiveCommand.Create(runner.Cancel, running);
        OpenPackFolderCommand = ReactiveCommand.Create(OpenPackFolder);

        RefreshSlots();
    }

    public override string Title => "Weapons";

    public override MaterialIconKind Icon => MaterialIconKind.Pistol;

    public JobRunnerService Runner { get; }

    public WeaponCatalog Catalog { get; }

    /// <summary>Step 1: the game modes a pack can be built for (the Defrag modes as one).</summary>
    public IReadOnlyList<GameModeGroup> Modes { get; }

    public GameModeGroup SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || value == _selectedMode)
                return;

            this.RaiseAndSetIfChanged(ref _selectedMode, value);
            // Each mode starts on the game it comes from (TF2 for Rocket and Sticky Jump, Quake 3 for Defrag...)
            _selectedGame = DefaultGameFor(value);
            this.RaisePropertyChanged(nameof(SelectedGame));
            RefreshSlots();
            this.RaisePropertyChanged(nameof(BuildAllTip));
        }
    }

    private CatalogGame? DefaultGameFor(GameModeGroup mode) =>
        Games.FirstOrDefault(x => x.Id.Equals(mode.DefaultGame, StringComparison.OrdinalIgnoreCase)) ?? Games.FirstOrDefault();

    /// <summary>The game's weapons for some of the mode's Momentum weapons, with the mode's own picks.</summary>
    private IReadOnlyList<LoadoutEntry> LoadoutFor(CatalogGame game, IEnumerable<WeaponSlot> slots) =>
        game.LoadoutFor(slots, SelectedMode.PicksFor(game.Id));

    /// <summary>Step 2: the mode's Momentum weapons, with what the pack holds for each.</summary>
    public ObservableCollection<MomentumWeaponViewModel> Slots { get; } = [];

    public MomentumWeaponViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSlot, value);
            RefreshWeapons();
        }
    }

    /// <summary>Step 3: the games the catalog has weapons from.</summary>
    public IReadOnlyList<CatalogGame> Games { get; }

    public CatalogGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGame, value);
            this.RaisePropertyChanged(nameof(BuildAllTip));
            RefreshWeapons();
        }
    }

    /// <summary>Step 4: the game's weapons that fit the selected Momentum weapon.</summary>
    public ObservableCollection<CatalogWeapon> Weapons { get; } = [];

    public CatalogWeapon? SelectedWeapon
    {
        get => _selectedWeapon;
        set => this.RaiseAndSetIfChanged(ref _selectedWeapon, value);
    }

    /// <summary>Shown in place of the weapon list when the game has nothing for the selected Momentum weapon.</summary>
    public string NoWeaponsMessage => SelectedGame is { } game && SelectedSlot is { } slot
        ? $"No {game.Name} weapon fits the {slot.Name.ToLowerInvariant()}"
        : "Pick a Momentum weapon and a game";

    /// <summary>Builds made this session, newest first.</summary>
    public ObservableCollection<WeaponPackBuildViewModel> Builds { get; } = [];

    public WeaponPackBuildViewModel? SelectedBuild
    {
        get => _selectedBuild;
        set => this.RaiseAndSetIfChanged(ref _selectedBuild, value);
    }

    /// <summary>What Build all weapons builds for the selected mode and game.</summary>
    public string? BuildAllTip
    {
        get
        {
            if (SelectedGame is not { } game)
                return null;

            IReadOnlyList<LoadoutEntry> loadout = LoadoutFor(game, SelectedMode.Slots);
            return loadout.Count == 0
                ? $"No {game.Name} weapon fits {SelectedMode.Name}'s weapons"
                : $"Build every {game.Name} weapon that fits a {SelectedMode.Name} weapon, into the one it fits best: "
                    + string.Join(", ", loadout.Select(x => $"{x.Weapon.Name} as {x.Slot.Name}"));
        }
    }

    public ReactiveCommand<Unit, Unit> BuildCommand { get; }

    public ReactiveCommand<Unit, Unit> BuildAllCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenPackFolderCommand { get; }

    /// <summary>
    /// Lists the selected mode's weapons and reads the pack folder for what each holds in that mode. Called again
    /// after every build. Keeps the selected weapon where the new mode has it too.
    /// </summary>
    public void RefreshSlots()
    {
        string? keep = SelectedSlot?.Slot.Id;
        IReadOnlyList<WeaponPackEntry> inventory = WeaponPackInventory.Read(Path.Combine(OutputDirectory(_settings.Settings), WeaponPackBuilder.SharedPackName));
        Slots.Clear();
        foreach (WeaponSlot slot in SelectedMode.Slots)
            Slots.Add(new MomentumWeaponViewModel(slot, DescribeInPack(slot, SelectedMode.Modes, inventory)));

        SelectedSlot = Slots.FirstOrDefault(x => x.Slot.Id == keep) ?? Slots.FirstOrDefault();
    }

    /// <summary>
    /// What the pack holds for a slot in the given modes: a mode's own script, else the one for every mode. One
    /// description when the modes agree, else one per mode. Null when the pack has nothing for it.
    /// </summary>
    public static string? DescribeInPack(WeaponSlot slot, IReadOnlyList<GameMode> modes, IReadOnlyList<WeaponPackEntry> inventory)
    {
        WeaponPackEntry? shared = inventory.FirstOrDefault(x => x.Slot == slot && x.Mode is null);
        var perMode = modes
            .Select(mode => (Mode: mode, Entry: inventory.FirstOrDefault(x => x.Slot == slot && x.Mode == mode) ?? shared))
            .ToList();
        if (perMode.All(x => x.Entry is null))
            return null;

        var descriptions = perMode.Select(x => x.Entry?.Description ?? "Momentum's").Distinct().ToList();
        return descriptions.Count == 1
            ? descriptions[0]
            : string.Join("; ", perMode.Select(x => $"{x.Mode.Name}: {x.Entry?.Description ?? "Momentum's"}"));
    }

    /// <summary>
    /// Lists the game's weapons that fit the selected Momentum weapon, best fit first, and picks the one the game's
    /// loadout would (the catalog's choice among several that fit equally).
    /// </summary>
    private void RefreshWeapons()
    {
        Weapons.Clear();
        if (SelectedGame is { } game && SelectedSlot is { } slot)
        {
            foreach (CatalogWeapon weapon in game.Weapons
                .Where(x => x.Slots.Contains(slot.Slot.Id, StringComparer.OrdinalIgnoreCase))
                .OrderBy(x => x.Slots.FindIndex(s => s.Equals(slot.Slot.Id, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                Weapons.Add(weapon);
            }

            SelectedWeapon = LoadoutFor(game, [slot.Slot]).FirstOrDefault()?.Weapon ?? Weapons.FirstOrDefault();
        }
        else
        {
            SelectedWeapon = null;
        }

        this.RaisePropertyChanged(nameof(NoWeaponsMessage));
    }

    /// <summary>Where packs go: Momentum's custom folder, or the output folder's custom/ when Momentum isn't found.</summary>
    private static string OutputDirectory(PorterSettings settings)
    {
        string? content = settings.ResolveMomentumContentDirectory();
        return content is not null
            ? Path.Combine(content, "custom")
            : Path.Combine(settings.ResolveOutputDirectory(), "custom");
    }

    public async Task BuildAsync()
    {
        if (SelectedWeapon is null || SelectedSlot is null || SelectedGame is null)
            return;

        var build = new WeaponPackBuildViewModel(BuildTitle(SelectedGame, SelectedWeapon, SelectedSlot.Slot), SnapshotOptions(SelectedWeapon.Id, SelectedSlot.Slot.Id));
        Builds.Insert(0, build);
        SelectedBuild = build;
        await Runner.RunAsync(new List<WeaponPackBuildViewModel> { build }, Prepare);
    }

    /// <summary>
    /// Builds, for every weapon of the selected mode, the selected game's weapon that fits it best
    /// (<see cref="CatalogGame.LoadoutFor"/>), one after another into the same pack folder.
    /// </summary>
    public async Task BuildAllAsync()
    {
        if (SelectedGame is not { } game)
            return;

        var batch = LoadoutFor(game, SelectedMode.Slots)
            .Select(x => new WeaponPackBuildViewModel(BuildTitle(game, x.Weapon, x.Slot), SnapshotOptions(x.Weapon.Id, x.Slot.Id)))
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

    private string BuildTitle(CatalogGame game, CatalogWeapon weapon, WeaponSlot slot) => $"{SelectedMode.Name}: {game.Name} {weapon.Name} as {slot.Name}";

    /// <summary>The page's choice for one weapon as options, copied so later changes on the page don't touch a queued build.</summary>
    private WeaponPackOptions SnapshotOptions(string weapon, string? slot)
    {
        return new WeaponPackOptions
        {
            Game = SelectedGame?.Id ?? "",
            Weapon = weapon,
            Slot = slot,
            GameModes = SelectedMode.ModeList,
        };
    }

    /// <summary>
    /// Builds the job with the build's options (the page's current choice if it has none). Packs go to Momentum's
    /// custom folder, which the game loads from, and models compile with the Settings page's studiomdl if one is set.
    /// </summary>
    public PreparedJob Prepare(WeaponPackBuildViewModel build)
    {
        WeaponPackOptions options = build.Options is { } queued
            ? OptionsPreset.Clone(queued)
            : SnapshotOptions(SelectedWeapon?.Id ?? "", SelectedSlot?.Slot.Id);
        PorterSettings settings = _settings.Settings.Clone();

        if (string.IsNullOrWhiteSpace(options.MomentumDirectory) && !string.IsNullOrWhiteSpace(settings.MomentumDirectory))
            options.MomentumDirectory = settings.MomentumDirectory;

        if (string.IsNullOrWhiteSpace(options.StudioMdlPath) && !string.IsNullOrWhiteSpace(settings.StudiomdlPath))
            options.StudioMdlPath = settings.StudiomdlPath;

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
                _post(RefreshSlots);
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

/// <summary>A Momentum weapon of the selected mode in the Weapons page's list, with what the pack holds for it there.</summary>
/// <param name="InPack">The weapon the pack puts in it in the selected mode(s), or null for Momentum's own</param>
public sealed record MomentumWeaponViewModel(WeaponSlot Slot, string? InPack)
{
    public string Name => Slot.Name;
}
