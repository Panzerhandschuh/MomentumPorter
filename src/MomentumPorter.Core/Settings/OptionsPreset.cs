namespace MomentumPorter.Settings;

using System.Text.Json;

/// <summary>
/// Saves and loads option objects (e.g. <see cref="Options.MapPortOptions"/>) as JSON presets.
/// </summary>
public static class OptionsPreset
{
    public const string Extension = ".json";

    public static void Save<T>(T options, Stream stream) where T : class
    {
        JsonSerializer.Serialize(stream, options, PorterSettings.JsonOptions);
    }

    /// <summary>
    /// Reads a preset; properties missing from the file keep their defaults.
    /// </summary>
    public static T Load<T>(Stream stream) where T : class, new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(stream, PorterSettings.JsonOptions) ?? new T();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a valid preset: {ex.Message}", ex);
        }
    }

    public static T Clone<T>(T options) where T : class, new()
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(options, PorterSettings.JsonOptions), PorterSettings.JsonOptions) ?? new T();
    }

    public static void SaveFile<T>(T options, string path) where T : class
    {
        using FileStream stream = File.Create(path);
        Save(options, stream);
    }

    public static T LoadFile<T>(string path) where T : class, new()
    {
        using FileStream stream = File.OpenRead(path);
        return Load<T>(stream);
    }
}
