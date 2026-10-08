namespace MomentumPorter.UI.Services;

using System.Text.Json;
using MomentumPorter.Settings;

/// <summary>
/// Owns the app's <see cref="PorterSettings"/> and keeps them in a JSON file (%AppData%/MomentumPorter/settings.json).
/// </summary>
public sealed class SettingsService
{
    private SettingsService(string path, PorterSettings settings, string? loadError)
    {
        FilePath = path;
        Settings = settings;
        LoadError = loadError;
    }

    public string FilePath { get; }

    /// <summary>The live settings. Call <see cref="Save"/> after changing them.</summary>
    public PorterSettings Settings { get; }

    /// <summary>Why the settings file couldn't be read, if it couldn't (defaults are used instead).</summary>
    public string? LoadError { get; }

    public event EventHandler? Saved;

    public static SettingsService Load(string? path = null)
    {
        path ??= PorterSettings.DefaultPath;
        try
        {
            return new SettingsService(path, PorterSettings.Load(path), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SettingsService(path, new PorterSettings(), $"Couldn't read {path}, using default settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the settings. Returns an error message instead of throwing, since failing to save shouldn't stop a port.
    /// </summary>
    public string? Save()
    {
        try
        {
            Settings.Save(FilePath);
            Saved?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't save settings to {FilePath}: {ex.Message}";
        }
    }
}
