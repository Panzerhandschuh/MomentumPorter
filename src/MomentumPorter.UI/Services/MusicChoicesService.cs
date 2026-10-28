namespace MomentumPorter.UI.Services;

using MomentumPorter.Settings;

/// <summary>
/// Owns the maps' Sounds review picks (<see cref="MusicChoices"/>) and keeps them in music.json next to the settings.
/// </summary>
public sealed class MusicChoicesService(string path)
{
    public string FilePath { get; } = path;

    /// <summary>The live picks. Call <see cref="Save"/> after changing them.</summary>
    public MusicChoices Choices { get; } = MusicChoices.TryLoad(path);

    /// <summary>
    /// Writes the picks. Returns an error message instead of throwing.
    /// </summary>
    public string? Save()
    {
        try
        {
            Choices.Save(FilePath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save the sound picks to {FilePath}: {ex.Message}";
        }
    }
}
