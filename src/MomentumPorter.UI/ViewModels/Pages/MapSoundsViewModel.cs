namespace MomentumPorter.UI.ViewModels.Pages;

using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using MomentumPorter.Fixes;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using ReactiveUI;

/// <summary>
/// A packed sound in a map's Sounds review: whether it's music (moved into sound/music/ when the map is ported) or
/// taken out of the map.
/// </summary>
public sealed class MapSoundRowViewModel : ReactiveObject
{
    private bool _isMusic;
    private bool _isRemoved;
    private bool _isPlaying;

    public MapSoundRowViewModel(PackedSound sound, Func<MapSoundRowViewModel, Task> play)
    {
        Sound = sound;
        PlayCommand = ReactiveCommand.CreateFromTask(() => play(this));
    }

    public PackedSound Sound { get; }

    public string Path => Sound.Path;

    public string Name => Sound.Name;

    public string Length => Sound.LengthText;

    /// <summary>What plays it, on one line.</summary>
    public string Uses => Sound.Uses.Count switch
    {
        0 => "nothing in the map plays it",
        1 => Sound.Uses[0],
        _ => $"{Sound.Uses[0]} (+{Sound.Uses.Count - 1} more)",
    };

    public string UsesTip => Sound.Uses.Count == 0 ? "Nothing in the map's entities or packed scripts plays it." : string.Join("\n", Sound.Uses);

    public bool IsMusic
    {
        get => _isMusic;
        set
        {
            this.RaiseAndSetIfChanged(ref _isMusic, value);
            if (value)
                IsRemoved = false;
        }
    }

    /// <summary>Taken out of the map when it's ported, e.g. copyrighted music.</summary>
    public bool IsRemoved
    {
        get => _isRemoved;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRemoved, value);
            if (value)
                IsMusic = false;
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
}

/// <summary>
/// A map's Sounds review: its packed sounds outside sound/music/ in one list, longest first, each with a preview and a
/// Music box. The ones that look like music (2 minutes or longer) start ticked; porting the map moves what's ticked.
/// Saving keeps the picks as the map's <see cref="MapMusicChoice"/>, which the next port applies.
/// </summary>
public sealed class MapSoundsViewModel : ReactiveObject
{
    // How often the timeline follows the sound, and how long it leaves the slider alone after a seek
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(200);
    private const long SeekSettleMs = 400;

    private readonly Func<string, byte[]?> _read;
    private readonly ISoundPreview _preview;
    private readonly IScheduler _scheduler;
    private MapSoundRowViewModel? _playing;
    private IDisposable? _ticks;
    private long _seekedAt;
    private string? _previewStatus;
    // Where unmuting goes when the volume wasn't muted from anywhere
    private const double DefaultVolume = 50;

    private double _volume;
    private double? _unmutedVolume;
    private double _position;
    private double _duration;

    /// <param name="read">Reads a packed file's bytes, for the preview</param>
    /// <param name="ported">The map has been ported already, so the picks only apply when it's ported again</param>
    /// <param name="volume">The preview's volume from 0 to 1</param>
    /// <param name="scheduler">Where the timeline's updates run; the UI thread unless a test drives them</param>
    public MapSoundsViewModel(
        string mapName,
        MapSounds sounds,
        MapMusicChoice? choice,
        bool ported,
        Func<string, byte[]?> read,
        ISoundPreview preview,
        double volume,
        IScheduler? scheduler = null)
    {
        MapName = mapName;
        _read = read;
        _preview = preview;
        _scheduler = scheduler ?? RxApp.MainThreadScheduler;
        Ported = ported;
        InMusicFolder = sounds.InMusicFolder;
        Volume = volume * 100;

        // One list, longest first: the music is at the top whatever was guessed
        Rows = sounds.Sounds
            .OrderByDescending(x => x.Length ?? TimeSpan.MinValue)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MapSoundRowViewModel(x, PlayAsync))
            .ToList();
        Apply(choice);

        ResetCommand = ReactiveCommand.Create(() => Apply(null));
        StopCommand = ReactiveCommand.Create(Stop);
        MuteCommand = ReactiveCommand.Create(ToggleMute);
    }

    public string MapName { get; }

    public string Title => $"Sounds - {MapName}";

    public bool Ported { get; }

    public int InMusicFolder { get; }

    public IReadOnlyList<MapSoundRowViewModel> Rows { get; }

    public string Intro =>
        "Momentum's music volume slider only controls sounds in sound/music/. Tick the map's music and the porter moves it there when it ports the map, updating the paths the map plays it by. "
        + "Sounds that look like music (2 minutes or longer) start ticked; listen for shorter music loops. "
        + "The docs also ask for copyrighted music to be removed."
        + (InMusicFolder > 0 ? $" {InMusicFolder} sound{(InMusicFolder == 1 ? " is" : "s are")} already in a music folder." : "");

    /// <summary>The preview's volume, 0 to 100; changes the sound that's playing.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, Math.Clamp(value, 0, 100));
            _preview.Volume = _volume / 100;
            // Any other change (e.g. the slider) forgets the volume muting kept
            _unmutedVolume = null;
            this.RaisePropertyChanged(nameof(IsMuted));
            this.RaisePropertyChanged(nameof(VolumeText));
        }
    }

    public bool IsMuted => _volume < 0.5;

    public string VolumeText => $"{_volume:0}";

    public string? PortedNote => Ported ? "This map has been ported already: port it again to apply these picks." : null;

    /// <summary>Why the preview couldn't play a sound.</summary>
    public string? PreviewStatus
    {
        get => _previewStatus;
        private set => this.RaiseAndSetIfChanged(ref _previewStatus, value);
    }

    /// <summary>The sound the timeline is for, or null when nothing is playing.</summary>
    public string? NowPlaying => _playing?.Name;

    public bool IsPreviewing => _playing is not null;

    /// <summary>Where the playing sound is, in seconds; setting it seeks, e.g. past a long fade-in.</summary>
    public double Position
    {
        get => _position;
        set
        {
            if (_playing is null)
                return;

            SetPosition(Math.Clamp(value, 0, _duration));
            _seekedAt = Environment.TickCount64;
            _preview.Seek(TimeSpan.FromSeconds(_position));
        }
    }

    /// <summary>The playing sound's length in seconds, the timeline's end.</summary>
    public double Duration
    {
        get => _duration;
        private set => this.RaiseAndSetIfChanged(ref _duration, value);
    }

    /// <summary>"0:42 / 3:15", in minutes and seconds even for a short sound, so it reads like a player's clock.</summary>
    public string PositionText => $"{Clock(_position)} / {Clock(_duration)}";

    private static string Clock(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Floor(seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>Mutes the preview, or puts the volume back to what it was before.</summary>
    public ReactiveCommand<Unit, Unit> MuteCommand { get; }

    /// <summary>The picks to save, with the sounds that might have been music saved as not, so they count as listened to.</summary>
    public MapMusicChoice ToChoice()
    {
        var choice = new MapMusicChoice();
        foreach (MapSoundRowViewModel row in Rows)
        {
            if (row.IsRemoved)
                choice.Removed.Add(row.Path);
            else if (row.IsMusic)
                choice.Music.Add(row.Path);
            else if (row.Sound.Guess != MusicGuess.Effect)
                choice.NotMusic.Add(row.Path);
        }

        return choice;
    }

    public void Stop()
    {
        _preview.Stop();
        _ticks?.Dispose();
        _ticks = null;
        PreviewStatus = null;
        SetPlaying(null);
    }

    // Muted is just no volume, so the slider shows it. Unmuting puts back the volume it was muted at, or the default
    // when the slider was dragged down instead
    private void ToggleMute()
    {
        if (IsMuted)
        {
            Volume = _unmutedVolume ?? DefaultVolume;
            return;
        }

        double volume = _volume;
        Volume = 0;
        _unmutedVolume = volume;
    }

    // The saved picks, or (with none) the suggestion: long sounds ticked
    private void Apply(MapMusicChoice? choice)
    {
        foreach (MapSoundRowViewModel row in Rows)
        {
            row.IsRemoved = choice?.IsRemoved(row.Path) == true;
            row.IsMusic = !row.IsRemoved
                && MapMusic.IsTicked(row.Sound, choice);
        }
    }

    private async Task PlayAsync(MapSoundRowViewModel row)
    {
        // Pressing a playing sound's button stops it
        if (row.IsPlaying)
        {
            Stop();
            return;
        }

        Stop();
        byte[]? bytes = await Task.Run(() => _read(row.Path));
        if (bytes is null)
        {
            PreviewStatus = $"Couldn't read {row.Name} from the map";
            return;
        }

        string? error = _preview.Play(bytes, row.Path);
        if (error is not null)
        {
            PreviewStatus = $"{row.Name}: {error}";
            return;
        }

        Duration = (row.Sound.Length ?? TimeSpan.Zero).TotalSeconds;
        SetPlaying(row);
        _ticks = Observable.Interval(Tick, _scheduler).Subscribe(_ => Follow());
    }

    // Moves the timeline along with the sound, and puts its button back to Play when it ends
    private void Follow()
    {
        if (_playing is null)
            return;

        if (_preview.Position() is not { } now || now.Finished)
        {
            Stop();
            return;
        }

        // The player's length is the decoded one, which beats the header's estimate for some MP3s
        if (now.Length > TimeSpan.Zero)
            Duration = now.Length.TotalSeconds;
        if (Environment.TickCount64 - _seekedAt > SeekSettleMs)
            SetPosition(now.Position.TotalSeconds);
    }

    private void SetPlaying(MapSoundRowViewModel? row)
    {
        if (_playing is not null)
            _playing.IsPlaying = false;
        _playing = row;
        if (row is not null)
            row.IsPlaying = true;

        SetPosition(0);
        this.RaisePropertyChanged(nameof(NowPlaying));
        this.RaisePropertyChanged(nameof(IsPreviewing));
    }

    private void SetPosition(double seconds)
    {
        this.RaiseAndSetIfChanged(ref _position, seconds, nameof(Position));
        this.RaisePropertyChanged(nameof(PositionText));
    }
}
