namespace MomentumPorter.UI.Services;

/// <summary>
/// Plays one sound at a time for the Sounds review, with a timeline: where it's got to, and seeking.
/// </summary>
public interface ISoundPreview
{
    /// <summary>How loud it plays, from 0 to 1. Changing it changes the sound that's playing.</summary>
    double Volume { get; set; }

    /// <summary>Starts playing a WAV or MP3, cutting off the last sound. Returns why it can't be played, or null when it started.</summary>
    /// <param name="fileName">The file's name, whose extension says what it is</param>
    string? Play(byte[] bytes, string fileName);

    /// <summary>Where the sound has got to, or null when nothing is playing.</summary>
    PreviewPosition? Position();

    /// <summary>Plays the sound from <paramref name="position"/>, also once it has finished.</summary>
    void Seek(TimeSpan position);

    void Stop();
}

/// <param name="Finished">It played to the end</param>
public readonly record struct PreviewPosition(TimeSpan Position, TimeSpan Length, bool Finished);
