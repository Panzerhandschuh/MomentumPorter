namespace MomentumPorter.UI.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Material.Icons;
using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Ports a queue of maps with one set of options: the desktop version of pack_for_momentum.bat. Every map is
/// installed into Momentum, replacing a map of the same name; the game is detected from the map.
/// </summary>
public sealed class MapsPageViewModel : PageViewModel
{
    public static readonly FileFilter MapFilter = new("Maps", "*.bsp", "*.pk3", "*.zip");
    public static readonly FileFilter PresetFilter = new("Momentum Porter preset", "*" + OptionsPreset.Extension);

    /// <summary>BSPConvert options the page fills in itself (from the map options, the settings or Momentum's install), so they aren't in its form.</summary>
    private static readonly string[] SharedBspConvertOptions = [nameof(BspConvertOptions.OutputDirectory), nameof(BspConvertOptions.Compress), nameof(BspConvertOptions.Overwrite), nameof(BspConvertOptions.Studiomdl)];

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _post;
    private MapPortOptions _options = new();
    private OptionsFormViewModel _optionsForm;
    private BspConvertOptions _bspConvertOptions = new();
    private OptionsFormViewModel _bspConvertOptionsForm;
    private MapQueueItemViewModel? _selectedItem;
    private string? _message;
    private bool _hasQueued;
    private bool _showBspConvertOptions = true;
    private bool _showAdvanced;

    public MapsPageViewModel(SettingsService settings, IDialogService dialogs, JobRunnerService runner, Action<Action> post)
    {
        _settings = settings;
        _dialogs = dialogs;
        _post = post;
        Runner = runner;
        _optionsForm = new OptionsFormViewModel(_options);
        _bspConvertOptionsForm = new OptionsFormViewModel(_bspConvertOptions, exclude: SharedBspConvertOptions);

        IObservable<bool> running = runner.WhenAnyValue(x => x.IsRunning);
        Queue.CollectionChanged += (_, _) => UpdateQueueState();

        RunCommand = ReactiveCommand.CreateFromTask(RunAsync, this.WhenAnyValue(x => x.HasQueued, x => x.Runner.IsRunning, (queued, isRunning) => queued && !isRunning));
        CancelCommand = ReactiveCommand.Create(Runner.Cancel, running);
        AddFilesCommand = ReactiveCommand.CreateFromTask(PickFilesAsync);
        RemoveCommand = ReactiveCommand.Create<MapQueueItemViewModel>(Remove);
        RequeueCommand = ReactiveCommand.Create<MapQueueItemViewModel>(Requeue);
        RequeueAllCommand = ReactiveCommand.Create(RequeueAll, running.Select(x => !x));
        ClearFinishedCommand = ReactiveCommand.Create(ClearFinished);
        SavePresetCommand = ReactiveCommand.CreateFromTask(SavePresetAsync);
        LoadPresetCommand = ReactiveCommand.CreateFromTask(LoadPresetAsync, running.Select(x => !x));
        OpenMomentumMapsCommand = ReactiveCommand.Create(OpenMomentumMaps);
        ReviewCommand = ReactiveCommand.CreateFromTask<MapQueueItemViewModel>(ReviewAsync);
    }

    public override string Title => "Maps";

    public override MaterialIconKind Icon => MaterialIconKind.Map;

    public JobRunnerService Runner { get; }

    public ObservableCollection<MapQueueItemViewModel> Queue { get; } = [];

    public MapQueueItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => this.RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    /// <summary>The options the next jobs use. Each job gets its own copy when it starts.</summary>
    public MapPortOptions Options
    {
        get => _options;
        private set => this.RaiseAndSetIfChanged(ref _options, value);
    }

    public OptionsFormViewModel OptionsForm
    {
        get => _optionsForm;
        private set => this.RaiseAndSetIfChanged(ref _optionsForm, value);
    }

    /// <summary>Options for Quake 3 and GoldSrc maps, which go through BSPConvert instead of a game profile.</summary>
    public BspConvertOptions BspConvertOptions
    {
        get => _bspConvertOptions;
        private set => this.RaiseAndSetIfChanged(ref _bspConvertOptions, value);
    }

    public OptionsFormViewModel BspConvertOptionsForm
    {
        get => _bspConvertOptionsForm;
        private set => this.RaiseAndSetIfChanged(ref _bspConvertOptionsForm, value);
    }

    /// <summary>Whether the queue has Quake 3 or GoldSrc maps (or is empty), so the BSPConvert options are shown.</summary>
    public bool ShowBspConvertOptions
    {
        get => _showBspConvertOptions;
        private set => this.RaiseAndSetIfChanged(ref _showBspConvertOptions, value);
    }

    /// <summary>Whether both forms show their advanced options too.</summary>
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set
        {
            this.RaiseAndSetIfChanged(ref _showAdvanced, value);
            OptionsForm.ShowAdvanced = value;
            BspConvertOptionsForm.ShowAdvanced = value;
        }
    }

    /// <summary>Feedback for the last action (files added, preset saved...).</summary>
    public string? Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public bool IsEmpty => Queue.Count == 0;

    /// <summary>Whether any item is waiting to run.</summary>
    public bool HasQueued
    {
        get => _hasQueued;
        private set => this.RaiseAndSetIfChanged(ref _hasQueued, value);
    }

    public ReactiveCommand<Unit, Unit> RunCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> AddFilesCommand { get; }

    public ReactiveCommand<MapQueueItemViewModel, Unit> RemoveCommand { get; }

    public ReactiveCommand<MapQueueItemViewModel, Unit> RequeueCommand { get; }

    public ReactiveCommand<Unit, Unit> RequeueAllCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearFinishedCommand { get; }

    public ReactiveCommand<Unit, Unit> SavePresetCommand { get; }

    public ReactiveCommand<Unit, Unit> LoadPresetCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenMomentumMapsCommand { get; }

    /// <summary>Checks a Source map against Momentum's entity tools as it is, without porting it.</summary>
    public ReactiveCommand<MapQueueItemViewModel, Unit> ReviewCommand { get; }

    /// <summary>
    /// Adds map files to the queue, skipping ones already waiting in it. Returns the items added.
    /// </summary>
    public IReadOnlyList<MapQueueItemViewModel> AddFiles(IEnumerable<string> paths)
    {
        var added = new List<MapQueueItemViewModel>();
        foreach (string path in paths)
        {
            string full = Path.GetFullPath(path);
            if (Queue.Any(x => x.Status == QueueStatus.Queued && string.Equals(x.InputPath, full, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new MapQueueItemViewModel(full);
            item.WhenAnyValue(x => x.Status).Subscribe(_ => UpdateQueueState());
            item.WhenAnyValue(x => x.MapKinds).Subscribe(_ => UpdateVisibleOptions());
            Queue.Add(item);
            added.Add(item);
            _ = item.DetectGameAsync(_post);
        }

        if (added.Count > 0)
        {
            SelectedItem ??= added[0];
            Message = added.Count == 1 ? $"Added {added[0].Title}" : $"Added {added.Count} maps";
        }

        return added;
    }

    public async Task RunAsync()
    {
        // Follow the running item so its log is the one on screen
        using IDisposable selectRunning = Runner
            .WhenAnyValue(x => x.Current)
            .Subscribe(x =>
            {
                if (x is MapQueueItemViewModel item)
                    SelectedItem = item;
            });

        await Runner.RunAsync(Queue, Prepare);
    }

    /// <summary>
    /// Builds the job for an item: port into a fresh work folder, then pack and install as the options say.
    /// Maps that aren't installed (the options' Install off, which the page itself never does) are moved to the output folder.
    /// </summary>
    public PreparedJob Prepare(MapQueueItemViewModel item)
    {
        if (!item.IsSupported)
            throw new JobSkippedException("Skipped, not a map");

        MapPortOptions options = OptionsPreset.Clone(Options);
        PorterSettings settings = _settings.Settings.Clone();

        if (item.UsesBspConvert)
            return PrepareBspConvert(item, options, settings);

        string workDirectory = CreateWorkDirectory(settings, item.MapName);
        string workOutput = Path.Combine(workDirectory, item.MapName + ".bsp");
        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(item.InputPath, workOutput, options);

        return new PreparedJob(
            job,
            workDirectory,
            context =>
            {
                context.Set(state);
                context.Set(settings);
                context.Set<IUserPrompts>(new DialogUserPrompts(_dialogs));
            },
            (context, result) => Deliver(context, result, state, settings));
    }

    private static JobOutcome Deliver(PortJobContext context, PortJobResult result, MapPortState state, PorterSettings settings)
    {
        if (!result.Succeeded)
        {
            return result.Exception is OperationCanceledException
                ? new JobOutcome(QueueStatus.Skipped, "Cancelled")
                : new JobOutcome(QueueStatus.Failed, $"{result.FailedStep} failed: {result.Error}");
        }

        // What's left to do by hand: entities the tools have to change in game, zoning, screenshots, the form
        EntityReview? review = context.TryGet<EntityReview>();
        bool zoned = state.PortContext?.ExtraOutputs.Any(x => x.RelativePath.StartsWith("zones/", StringComparison.OrdinalIgnoreCase)) == true;
        IReadOnlyList<NextStep> steps = SubmissionSteps.For(state.MapName, review, zoned, state.Profile?.MountedGame);
        string? attention = review?.Summary;
        if (review is not null)
        {
            foreach (string line in review.Lines())
                context.Log.Info(line);
        }
        SubmissionSteps.Print(context.Log, state.MapName, steps);

        string? warnings = context.Report.HasWarnings ? " (with warnings, see the report)" : null;
        if (context.TryGet<InstallResult>() is { } install)
        {
            return install.Skipped
                ? new JobOutcome(QueueStatus.Skipped, "Already in Momentum's maps folder, kept the existing map")
                : new JobOutcome(QueueStatus.Done, $"Installed into Momentum{warnings}", InstalledPath: install.InstalledPath, Attention: attention, NextSteps: steps);
        }

        string outputDirectory = settings.ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);
        string output = Path.Combine(outputDirectory, Path.GetFileName(state.OutputPath));
        File.Move(state.OutputPath, output, overwrite: true);
        context.Log.Info($"Saved {output}");

        // Files written next to the map, e.g. a GOKZ zone file in zones/local, keep their layout
        string workDirectory = Path.GetDirectoryName(Path.GetFullPath(state.OutputPath))!;
        foreach (ExtraOutput extra in state.PortContext?.ExtraOutputs ?? [])
        {
            string source = WriteBspStep.GetExtraOutputPath(workDirectory, extra.RelativePath);
            string target = WriteBspStep.GetExtraOutputPath(outputDirectory, extra.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, overwrite: true);
            context.Log.Info($"Saved {target}");
        }

        return new JobOutcome(QueueStatus.Done, $"Saved to the output folder{warnings}", OutputPath: output, Attention: attention, NextSteps: steps);
    }

    private async Task ReviewAsync(MapQueueItemViewModel item)
    {
        SelectedItem = item;
        await item.ReviewAsync(_post);
    }

    /// <summary>
    /// Quake 3 and GoldSrc maps: BSPConvert converts into the work folder, then the files are copied into Momentum's
    /// game folder (or the output folder, laid out like a game folder, when the options' Install is off).
    /// </summary>
    private PreparedJob PrepareBspConvert(MapQueueItemViewModel item, MapPortOptions mapOptions, PorterSettings settings)
    {
        BspConvertOptions options = BspConvertOptions.Clone();
        options.Overwrite = mapOptions.Overwrite || !mapOptions.Install;
        options.Compress = mapOptions.Compress;

        string? target = mapOptions.Install ? settings.ResolveMomentumContentDirectory() : settings.ResolveOutputDirectory();
        if (target is null)
            throw new InvalidOperationException("Momentum Mod wasn't found in your Steam libraries. Set its folder in Settings.");

        options.OutputDirectory = target;

        var steam = new SteamLocator();
        // The Settings page's studiomdl (or Momentum's own, through its folder override) before BSPConvert's own search
        if (settings.ResolveStudiomdlPath(steam) is { } studiomdl)
            options.Studiomdl = studiomdl;

        foreach (string filled in BspConvertDefaults.Fill(options, steam, item.InputPath, output: false, settings))
            item.Log.Add(LogLevel.Info, filled);

        (PortJob job, BspConvertState state) = BspConvertJobs.Create(item.InputPath, options, copyToOutput: true, steam);
        string workDirectory = CreateWorkDirectory(settings, item.MapName);

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

                string? warnings = context.Report.HasWarnings ? " (with warnings, see the report)" : null;
                string? firstMap = state.InstalledFiles.FirstOrDefault(x => x.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase));
                int maps = state.InstalledFiles.Count(x => x.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase));
                string what = maps == 1 ? "1 map" : $"{maps} maps";

                // The same road to the website as Source maps, minus the entity tools (Quake 3 maps get their zones from BSPConvert)
                string mapName = firstMap is null ? item.MapName : Path.GetFileNameWithoutExtension(firstMap);
                bool zoned = state.InstalledFiles.Any(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<NextStep> steps = SubmissionSteps.For(mapName, review: null, zoned, mountedGame: null);
                SubmissionSteps.Print(context.Log, mapName, steps);

                return mapOptions.Install
                    ? new JobOutcome(QueueStatus.Done, $"Installed {what} into Momentum{warnings}", InstalledPath: firstMap, NextSteps: steps)
                    : new JobOutcome(QueueStatus.Done, $"Saved {what} to the output folder{warnings}", OutputPath: firstMap, NextSteps: steps);
            });
    }

    private static string CreateWorkDirectory(PorterSettings settings, string mapName)
    {
        if (string.IsNullOrWhiteSpace(settings.WorkDirectory))
            return PortJob.CreateWorkDirectory(mapName);

        string directory = Path.Combine(settings.WorkDirectory, $"{mapName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Remove(MapQueueItemViewModel item)
    {
        if (item.IsRunning)
            return;

        Queue.Remove(item);
        if (SelectedItem == item)
            SelectedItem = Queue.FirstOrDefault();
    }

    private void UpdateQueueState()
    {
        HasQueued = Queue.Any(x => x.Status == QueueStatus.Queued);
        this.RaisePropertyChanged(nameof(IsEmpty));
        UpdateVisibleOptions();
    }

    /// <summary>
    /// Shows only the options for the kinds of map in the queue, or all of them while it's empty.
    /// </summary>
    private void UpdateVisibleOptions()
    {
        HashSet<InputKind> kinds = Queue.Count == 0
            ? [InputKind.SourceBsp, InputKind.Quake3Bsp, InputKind.GoldSrcBsp]
            : Queue.SelectMany(x => x.MapKinds).ToHashSet();
        bool quake3 = kinds.Contains(InputKind.Quake3Bsp);
        bool goldSrc = kinds.Contains(InputKind.GoldSrcBsp);

        // The map options (compress) apply to every kind of map; only BSPConvert's are per kind
        BspConvertOptionsForm.SetFilter(x => x.Info.Attribute.Group switch
        {
            BspConvertOptions.Quake3Group or BspConvertOptions.ExperimentalGroup or BspConvertOptions.AnimatedTexturesGroup => quake3,
            BspConvertOptions.GoldSrcGroup => goldSrc,
            _ => true,
        });
        ShowBspConvertOptions = quake3 || goldSrc;
    }

    public void Requeue(MapQueueItemViewModel item)
    {
        if (item.IsFinished)
            item.Requeue();
    }

    public void RequeueAll()
    {
        foreach (MapQueueItemViewModel item in Queue.Where(x => x.IsFinished))
            item.Requeue();
    }

    public void ClearFinished()
    {
        foreach (MapQueueItemViewModel item in Queue.Where(x => x.Status is QueueStatus.Done or QueueStatus.Skipped).ToList())
            Remove(item);
    }

    /// <summary>
    /// Replaces the options, e.g. from a preset, and rebuilds the form. The page always packs KZ maps (they aren't
    /// playable otherwise), installs into Momentum and replaces what's there, whatever a preset says.
    /// </summary>
    public void SetOptions(MapPortOptions options)
    {
        options.Pack = true;
        options.Install = true;
        options.Overwrite = true;
        Options = options;
        OptionsForm = new OptionsFormViewModel(options) { ShowAdvanced = ShowAdvanced };
        UpdateVisibleOptions();
    }

    public void SavePreset(Stream stream) => OptionsPreset.Save(Options, stream);

    public void LoadPreset(Stream stream) => SetOptions(OptionsPreset.Load<MapPortOptions>(stream));

    private async Task PickFilesAsync()
    {
        IReadOnlyList<string> files = await _dialogs.PickFilesAsync("Add maps", allowMultiple: true, MapFilter);
        AddFiles(files);
    }

    private async Task SavePresetAsync()
    {
        string? path = await _dialogs.PickSaveFileAsync("Save map options preset", "map preset" + OptionsPreset.Extension, PresetFilter);
        if (path is null)
            return;

        try
        {
            OptionsPreset.SaveFile(Options, path);
            Message = $"Saved preset {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Couldn't save the preset", ex.Message);
        }
    }

    private async Task LoadPresetAsync()
    {
        IReadOnlyList<string> paths = await _dialogs.PickFilesAsync("Load map options preset", allowMultiple: false, PresetFilter);
        if (paths.Count == 0)
            return;

        try
        {
            SetOptions(OptionsPreset.LoadFile<MapPortOptions>(paths[0]));
            Message = $"Loaded preset {Path.GetFileName(paths[0])}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _dialogs.ShowMessageAsync("Couldn't load the preset", ex.Message);
        }
    }

    private void OpenMomentumMaps()
    {
        if (SelectedItem?.InstalledPath is { } installed && File.Exists(installed))
        {
            ShellService.RevealFile(installed);
            return;
        }

        if (_settings.Settings.ResolveMomentumMapsDirectory() is { } maps)
            ShellService.OpenFolder(maps);
        else
            _ = _dialogs.ShowMessageAsync("Momentum Mod not found", "Momentum Mod wasn't found in your Steam libraries. Set its folder in Settings.");
    }

    public static bool IsMapFile(string path)
    {
        try
        {
            return InputDetector.IsMap(InputDetector.Detect(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
