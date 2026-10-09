namespace MomentumPorter.UI.ViewModels.Pages;

using System.Reactive;
using MomentumPorter.Assets.Sounds;
using ReactiveUI;

/// <summary>
/// A sound in the Player Sounds preview. Each play takes the next of its files, so a footstep set is heard in turn.
/// </summary>
public sealed class PreviewSoundViewModel : ReactiveObject
{
    private int _next;
    private string? _status;
    private bool _isError;

    public PreviewSoundViewModel(PreviewSound sound, Func<PreviewSoundViewModel, Task> play)
    {
        Sound = sound;
        PlayCommand = ReactiveCommand.CreateFromTask(() => play(this));
    }

    public PreviewSound Sound { get; }

    public string Name => Sound.Name;

    public string? Detail => Sound.Detail;

    /// <summary>The game's name for the sound, and how many files it plays between.</summary>
    public string Label => Sound.Sound.Files.Count > 1 ? $"{Sound.Sound.Label} ({Sound.Sound.Files.Count} files)" : Sound.Sound.Label;

    /// <summary>Why the last file couldn't be played, or null.</summary>
    public string? Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsError
    {
        get => _isError;
        set => this.RaiseAndSetIfChanged(ref _isError, value);
    }

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }

    /// <summary>The file to play next, and its number among the sound's files (from 1).</summary>
    public (GameSoundFile File, int Number) TakeNextFile()
    {
        int index = _next % Sound.Sound.Files.Count;
        _next = index + 1;
        return (Sound.Sound.Files[index], index + 1);
    }
}
