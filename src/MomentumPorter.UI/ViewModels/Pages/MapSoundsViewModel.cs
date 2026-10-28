namespace MomentumPorter.UI.ViewModels.Pages;

using System.Reactive;
using MomentumPorter.Fixes;
using MomentumPorter.Settings;
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
    private readonly Func<string, byte[]?> _read;
    private readonly Func<byte[], string, string?> _play;
    private readonly Action _stop;
    private readonly Action<double> _setVolume;
    private MapSoundRowViewModel? _playing;
    private int _plays;
    private string? _previewStatus;
    private double _volume;

    /// <param name="read">Reads a packed file's bytes, for the preview</param>
    /// <param name="play">Plays a sound's bytes (with its file name for the format); returns why it couldn't</param>
    /// <param name="ported">The map has been ported already, so the picks only apply when it's ported again</param>
    /// <param name="volume">The preview's volume from 0 to 1, which <paramref name="setVolume"/> applies</param>
    public MapSoundsViewModel(
        string mapName,
        MapSounds sounds,
        MapMusicChoice? choice,
        bool ported,
        Func<string, byte[]?> read,
        Func<byte[], string, string?> play,
        Action stop,
        double volume,
        Action<double> setVolume)
    {
        MapName = mapName;
        _read = read;
        _play = play;
        _stop = stop;
        _setVolume = setVolume;
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
            _setVolume(_volume / 100);
        }
    }

    public string? PortedNote => Ported ? "This map has been ported already: port it again to apply these picks." : null;

    /// <summary>What the preview is playing, or why it couldn't.</summary>
    public string? PreviewStatus
    {
        get => _previewStatus;
        private set => this.RaiseAndSetIfChanged(ref _previewStatus, value);
    }

    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

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
        _stop();
        if (_playing is not null)
            _playing.IsPlaying = false;
        _playing = null;
        PreviewStatus = null;
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

        string? error = _play(bytes, row.Path);
        if (error is not null)
        {
            PreviewStatus = $"{row.Name}: {error}";
            return;
        }

        _playing = row;
        row.IsPlaying = true;
        PreviewStatus = $"Playing {row.Name} ({row.Length})";
        // Not awaited: the command would stay busy (and the button disabled) for the whole sound
        _ = ClearWhenFinishedAsync(row, ++_plays);
    }

    // Nothing says when a sound ends, so its button goes back to Play after its length
    private async Task ClearWhenFinishedAsync(MapSoundRowViewModel row, int play)
    {
        if (row.Sound.Length is not { } length)
            return;

        await Task.Delay(length + TimeSpan.FromMilliseconds(250));
        if (play == _plays && _playing == row)
        {
            row.IsPlaying = false;
            _playing = null;
            PreviewStatus = null;
        }
    }
}
