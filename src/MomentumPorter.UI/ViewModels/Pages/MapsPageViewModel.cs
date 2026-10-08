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
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// Ports a queue of maps with one set of options: the desktop version of pack_for_momentum.bat.
/// </summary>
public sealed class MapsPageViewModel : PageViewModel
{
    public static readonly FileFilter MapFilter = new("Maps", "*.bsp", "*.pk3", "*.zip");
    public static readonly FileFilter PresetFilter = new("Momentum Porter preset", "*" + OptionsPreset.Extension);

    /// <summary>Map options only Source maps use; the rest (compress, install, overwrite) apply to every map.</summary>
    private static readonly HashSet<string> SourceOnlyOptions = [nameof(MapPortOptions.Game), nameof(MapPortOptions.GameDirectory), nameof(MapPortOptions.Pack)];

    /// <summary>BSPConvert options the page sets from the map options, so they aren't in its form.</summary>
    private static readonly string[] SharedBspConvertOptions = [nameof(BspConvertOptions.OutputDirectory), nameof(BspConvertOptions.Compress), nameof(BspConvertOptions.Overwrite)];

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _post;
    private MapPortOptions _options;
    private OptionsFormViewModel _optionsForm;
    private BspConvertOptions _bspConvertOptions = new();
    private OptionsFormViewModel _bspConvertOptionsForm;
    private MapQueueItemViewModel? _selectedItem;
    private string? _message;
    private bool _hasQueued;
    private bool _showBspConvertOptions = true;

    public MapsPageViewModel(SettingsService settings, IDialogService dialogs, JobRunnerService runner, Action<Action> post)
    {
        _settings = settings;
        _dialogs = dialogs;
        _post = post;
        Runner = runner;
        _options = new MapPortOptions { Install = settings.Settings.InstallByDefault };
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
        OpenOutputFolderCommand = ReactiveCommand.Create(OpenOutputFolder);
        OpenMomentumMapsCommand = ReactiveCommand.Create(OpenMomentumMaps);
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

    public ReactiveCommand<Unit, Unit> OpenOutputFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenMomentumMapsCommand { get; }

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
    /// Maps that aren't installed are moved to the output folder.
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

        string? warnings = context.Report.HasWarnings ? " (with warnings, see the report)" : null;
        if (context.TryGet<InstallResult>() is { } install)
        {
            return install.Skipped
                ? new JobOutcome(QueueStatus.Skipped, "Already in Momentum's maps folder, kept the existing map")
                : new JobOutcome(QueueStatus.Done, $"Installed into Momentum{warnings}", InstalledPath: install.InstalledPath);
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

        return new JobOutcome(QueueStatus.Done, $"Saved to the output folder{warnings}", OutputPath: output);
    }

    /// <summary>
    /// Quake 3 and GoldSrc maps: BSPConvert converts into the work folder, then the files are copied into Momentum's
    /// game folder (Install) or the output folder, which is laid out like a game folder (maps/, maps/zones/local/...).
    /// </summary>
    private PreparedJob PrepareBspConvert(MapQueueItemViewModel item, MapPortOptions mapOptions, PorterSettings settings)
    {
        BspConvertOptions options = BspConvertOptions.Clone();
        options.Overwrite = mapOptions.Overwrite || !mapOptions.Install;
        options.Compress = mapOptions.Compress;

        string? target = mapOptions.Install ? settings.ResolveMomentumContentDirectory() : settings.ResolveOutputDirectory();
        if (target is null)
            throw new InvalidOperationException("Momentum Mod wasn't found in your Steam libraries. Set its folder in Settings, or turn off Install into Momentum.");

        options.OutputDirectory = target;

        var steam = new SteamLocator();
        foreach (string filled in BspConvertDefaults.Fill(options, steam, item.InputPath, output: false))
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
                return mapOptions.Install
                    ? new JobOutcome(QueueStatus.Done, $"Installed {what} into Momentum{warnings}", InstalledPath: firstMap)
                    : new JobOutcome(QueueStatus.Done, $"Saved {what} to the output folder{warnings}", OutputPath: firstMap);
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
        bool source = kinds.Contains(InputKind.SourceBsp);
        bool quake3 = kinds.Contains(InputKind.Quake3Bsp);
        bool goldSrc = kinds.Contains(InputKind.GoldSrcBsp);

        OptionsForm.SetFilter(x => source || !SourceOnlyOptions.Contains(x.Name));
        BspConvertOptionsForm.SetFilter(x => x.Info.Attribute.Group switch
        {
            BspConvertOptions.Quake3Group or BspConvertOptions.AnimatedTexturesGroup => quake3,
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

    /// <summary>Replaces the options, e.g. from a preset, and rebuilds the form.</summary>
    public void SetOptions(MapPortOptions options)
    {
        Options = options;
        OptionsForm = new OptionsFormViewModel(options);
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

    private void OpenOutputFolder()
    {
        if (SelectedItem?.OutputPath is { } output && File.Exists(output))
            ShellService.RevealFile(output);
        else
            ShellService.OpenFolder(_settings.Settings.ResolveOutputDirectory());
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
