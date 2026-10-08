namespace MomentumPorter.UI.ViewModels.Pages;

using LibBSP;
using MomentumPorter.Detection;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
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

    public MapQueueItemViewModel(string path)
    {
        InputPath = Path.GetFullPath(path);
        Kind = DetectKind(InputPath);
        _game = Kind switch
        {
            InputKind.SourceBsp => "Detecting...",
            InputKind.Quake3Bsp or InputKind.Pk3Archive => "Quake 3",
            InputKind.GoldSrcBsp => "GoldSrc",
            _ => null,
        };
    }

    public string InputPath { get; }

    public override string Title => Path.GetFileName(InputPath);

    public string MapName => Path.GetFileNameWithoutExtension(InputPath);

    public InputKind Kind { get; }

    public string KindText => PortJobFactory.Describe(Kind);

    /// <summary>Only Source maps can be ported in this phase.</summary>
    /// <summary>Source maps go through a game profile, Quake 3 and GoldSrc ones through BSPConvert.</summary>
    public bool IsSupported => InputDetector.IsMap(Kind);

    public bool UsesBspConvert => IsSupported && Kind != InputKind.SourceBsp;

    /// <summary>Shown under the item when it can't be ported yet.</summary>
    public string? Note => IsSupported ? null : "Not a map";

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
    /// Reads a Source map's entities on a worker thread to tell which game it's for.
    /// </summary>
    public async Task DetectGameAsync(Action<Action> post)
    {
        if (Kind != InputKind.SourceBsp)
            return;

        (string? profile, string text) = await Task.Run(() =>
        {
            try
            {
                var bsp = new BSP(new FileInfo(InputPath));
                SourceGameSniffer.Detection? detection = SourceGameSniffer.Detect(bsp, LoadBspStep.FindGameDirectory(InputPath), MapName);
                if (detection is null)
                    return (null, "Unknown game, pick one in the options");

                string name = GameProfiles.Find(detection.Profile)?.Description ?? detection.Profile;
                return (detection.Profile, detection.Certain ? name : $"{name}? (guessed from {detection.Reason})");
            }
            catch (Exception ex)
            {
                return ((string?)null, $"Couldn't read the map: {ex.Message}");
            }
        });

        post(() =>
        {
            DetectedProfile = profile;
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
