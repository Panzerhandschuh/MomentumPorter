namespace MomentumPorter.UI.ViewModels.Pages;

using LibBSP;
using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Fixes;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steps;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// A map file in the Maps page's queue.
/// </summary>
public sealed class MapQueueItemViewModel : QueueItemViewModel
{
    private string? _game;
    private string? _detectedProfile;
    private IReadOnlySet<InputKind> _mapKinds;
    private MapSounds? _sounds;
    private string? _soundsSummary;

    public MapQueueItemViewModel(string path)
    {
        InputPath = Path.GetFullPath(path);
        Kind = DetectKind(InputPath);
        _game = Kind switch
        {
            InputKind.SourceBsp => "Detecting...",
            InputKind.Quake3Bsp or InputKind.Pk3Archive => "Quake 3",
            InputKind.GoldSrcBsp => "GoldSrc",
            InputKind.ZipArchive => "Reading...",
            _ => null,
        };

        // A zip could hold either until DetectGameAsync looks inside
        _mapKinds = Kind switch
        {
            InputKind.SourceBsp => new HashSet<InputKind> { InputKind.SourceBsp },
            InputKind.Quake3Bsp or InputKind.Pk3Archive => new HashSet<InputKind> { InputKind.Quake3Bsp },
            InputKind.GoldSrcBsp => new HashSet<InputKind> { InputKind.GoldSrcBsp },
            InputKind.ZipArchive => new HashSet<InputKind> { InputKind.Quake3Bsp, InputKind.GoldSrcBsp },
            _ => new HashSet<InputKind>(),
        };
    }

    public string InputPath { get; }

    public override string Title => Path.GetFileName(InputPath);

    public string MapName => Path.GetFileNameWithoutExtension(InputPath);

    public InputKind Kind { get; }

    public string KindText => PortJobFactory.Describe(Kind);

    /// <summary>Source maps go through a game profile, Quake 3 and GoldSrc ones through BSPConvert.</summary>
    public bool IsSupported => InputDetector.IsMap(Kind);

    public bool UsesBspConvert => IsSupported && Kind != InputKind.SourceBsp;

    /// <summary>Shown under the item when it can't be ported yet.</summary>
    public string? Note => IsSupported ? null : "Not a map";

    /// <summary>
    /// Source maps can be checked against Momentum's entity tools without porting them, e.g. a ported map saved from
    /// Lumper after the in-game review, to see whether anything was left out.
    /// </summary>
    public bool CanReview => Kind == InputKind.SourceBsp;

    /// <summary>
    /// Reviews the map file as it is (not a port of it) on a worker thread and shows the result in the item's log,
    /// report and next steps.
    /// </summary>
    /// <param name="music">The map's saved Sounds review picks, so settled sounds aren't brought up again</param>
    public async Task ReviewAsync(Action<Action> post, MapMusicChoice? music = null)
    {
        if (!CanReview || IsRunning)
            return;

        post(() =>
        {
            Log.Clear();
            Report.Clear();
            Log.Add(LogLevel.Info, $"Checking {Title} against Momentum's entity tools...");
        });

        (Review.EntityReview? review, string? error) = await Task.Run(() =>
        {
            try
            {
                return (Review.EntityReview.AnalyzeFile(InputPath, music), (string?)null);
            }
            catch (Exception ex)
            {
                return ((Review.EntityReview?)null, $"Couldn't review the map: {ex.Message}");
            }
        });

        post(() =>
        {
            if (review is null)
            {
                Log.Add(LogLevel.Error, error ?? "Couldn't review the map");
                return;
            }

            foreach (string line in review.Lines())
                Log.Add(LogLevel.Info, line);

            var report = new PortReport();
            review.Report(report);
            foreach (ReportLine line in ReportLine.FromReport(report))
                Report.Add(line);

            Attention = review.Summary ?? (review.HasMissingRequiredFixes ? review.Verdict : null);
            IReadOnlyList<Review.NextStep> steps = Review.SubmissionSteps.For(MapName, review, zoneFileWritten: false, mountedGame: null);
            SetNextSteps(steps);
            StatusMessage = review.Verdict;
        });
    }

    /// <summary>The game the map was detected as, for display.</summary>
    public string? Game
    {
        get => _game;
        private set => this.RaiseAndSetIfChanged(ref _game, value);
    }

    /// <summary>The game profile detection picked, if any.</summary>
    public string? DetectedProfile
    {
        get => _detectedProfile;
        private set => this.RaiseAndSetIfChanged(ref _detectedProfile, value);
    }

    /// <summary>
    /// The kinds of map the input holds (<see cref="InputKind.SourceBsp"/>, <see cref="InputKind.Quake3Bsp"/>,
    /// <see cref="InputKind.GoldSrcBsp"/>), which decide the options the Maps page shows.
    /// </summary>
    public IReadOnlySet<InputKind> MapKinds
    {
        get => _mapKinds;
        private set => this.RaiseAndSetIfChanged(ref _mapKinds, value);
    }

    /// <summary>
    /// Reads a Source map's entities on a worker thread to tell which game it's for, or lists the maps in a zip.
    /// </summary>
    public async Task DetectGameAsync(Action<Action> post)
    {
        if (Kind == InputKind.ZipArchive)
        {
            await DetectArchiveAsync(post);
            return;
        }

        if (Kind != InputKind.SourceBsp)
            return;

        (string? profile, string text, MapSounds? sounds) = await Task.Run(() =>
        {
            try
            {
                var bsp = new BSP(new FileInfo(InputPath));
                MapSounds? sounds = ScanSounds(bsp);
                SourceGameSniffer.Detection? detection = SourceGameSniffer.Detect(bsp, LoadBspStep.FindGameDirectory(InputPath), MapName);
                if (detection is null)
                    return (null, "Unknown game, pick one in the options", sounds);

                string name = GameProfiles.Find(detection.Profile)?.Description ?? detection.Profile;
                return (detection.Profile, detection.Certain ? name : $"{name}? (guessed from {detection.Reason})", sounds);
            }
            catch (Exception ex)
            {
                return ((string?)null, $"Couldn't read the map: {ex.Message}", (MapSounds?)null);
            }
        });

        post(() =>
        {
            DetectedProfile = profile;
            Game = text;
            Sounds = sounds;
        });
    }

    private MapSounds? ScanSounds(BSP bsp)
    {
        try
        {
            return MapMusic.Scan(new PortContext(bsp, MapName, null));
        }
        catch (Exception)
        {
            // A pak file that can't be read doesn't stop the map being detected; it has no Sounds review
            return null;
        }
    }

    /// <summary>The map's packed sounds outside sound/music/, read when it was queued; null until then or for other maps.</summary>
    public MapSounds? Sounds
    {
        get => _sounds;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sounds, value);
            this.RaisePropertyChanged(nameof(HasSounds));
        }
    }

    public bool HasSounds => _sounds is { Sounds.Count: > 0 };

    /// <summary>"Sounds: 12 music, 3 other" for the button that opens the map's Sounds review.</summary>
    public string? SoundsSummary
    {
        get => _soundsSummary;
        private set => this.RaiseAndSetIfChanged(ref _soundsSummary, value);
    }

    /// <summary>
    /// Counts what porting the map will do with its sounds, from its saved picks.
    /// </summary>
    public void UpdateSoundsSummary(MapMusicChoice? choice)
    {
        if (_sounds is not { Sounds.Count: > 0 } sounds)
        {
            SoundsSummary = null;
            return;
        }

        (List<string> move, List<string> remove) = MapMusic.Decide(sounds, choice);
        int other = sounds.Sounds.Count - move.Count - remove.Count;
        var parts = new List<string>();
        if (move.Count > 0)
            parts.Add($"{move.Count} music");
        if (other > 0)
            parts.Add(move.Count == 0 && remove.Count == 0 ? $"{other}, none ticked as music" : $"{other} other");
        if (remove.Count > 0)
            parts.Add($"{remove.Count} removed");

        SoundsSummary = "Sounds: " + string.Join(", ", parts);
    }

    private async Task DetectArchiveAsync(Action<Action> post)
    {
        (IReadOnlySet<InputKind>? kinds, string text) = await Task.Run(() =>
        {
            try
            {
                var found = Pk3Inspector.ListMaps(InputPath).Where(x => x.CanConvert).Select(x => x.Kind).ToHashSet();
                string description = found.Count switch
                {
                    0 => "No Quake 3 or GoldSrc maps in it",
                    1 => found.Single() == InputKind.Quake3Bsp ? "Quake 3" : "GoldSrc",
                    _ => "Quake 3 and GoldSrc",
                };
                return ((IReadOnlySet<InputKind>?)found, description);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return (null, $"Couldn't read the archive: {ex.Message}");
            }
        });

        post(() =>
        {
            if (kinds is not null)
                MapKinds = kinds;
            Game = text;
        });
    }

    private static InputKind DetectKind(string path)
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
