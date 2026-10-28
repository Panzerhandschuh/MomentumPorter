namespace MomentumPorter.Settings;

using System.Text.Json;

/// <summary>
/// What someone decided about a map's packed sounds in its Sounds review, by pak path: which are music (moved into
/// sound/music/), which aren't (even if long), and which to take out of the map (e.g. copyrighted music).
/// </summary>
public sealed class MapMusicChoice
{
    public List<string> Music { get; set; } = [];

    public List<string> NotMusic { get; set; } = [];

    public List<string> Removed { get; set; } = [];

    public bool IsMusic(string path) => Contains(Music, path);

    public bool IsNotMusic(string path) => Contains(NotMusic, path);

    public bool IsRemoved(string path) => Contains(Removed, path);

    /// <summary>Whether the review said anything about this sound.</summary>
    public bool Decided(string path) => IsMusic(path) || IsNotMusic(path) || IsRemoved(path);

    public bool IsEmpty => Music.Count == 0 && NotMusic.Count == 0 && Removed.Count == 0;

    private static bool Contains(List<string> paths, string path) => paths.Any(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Every map's <see cref="MapMusicChoice"/>, by map name, kept in music.json next to the settings so porting a map
/// again doesn't mean listening to its sounds again. Shared by the app and the command line.
/// </summary>
public sealed class MusicChoices
{
    public const string FileName = "music.json";

    /// <summary>%AppData%/MomentumPorter/music.json</summary>
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(PorterSettings.DefaultPath)!, FileName);

    public Dictionary<string, MapMusicChoice> Maps
    {
        get => _maps;
        set => _maps = new Dictionary<string, MapMusicChoice>(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, MapMusicChoice> _maps = new(StringComparer.OrdinalIgnoreCase);

    public MapMusicChoice? Find(string mapName) => Maps.GetValueOrDefault(mapName);

    /// <summary>Stores a map's choice, or forgets it when it's empty.</summary>
    public void Set(string mapName, MapMusicChoice choice)
    {
        if (choice.IsEmpty)
            Maps.Remove(mapName);
        else
            Maps[mapName] = choice;
    }

    /// <summary>Reads the file, or returns no choices when there isn't one.</summary>
    public static MusicChoices Load(string path)
    {
        if (!File.Exists(path))
            return new MusicChoices();

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MusicChoices>(stream, PorterSettings.JsonOptions) ?? new MusicChoices();
    }

    /// <summary>Reads the file, or returns no choices when it's missing or unreadable.</summary>
    public static MusicChoices TryLoad(string path)
    {
        try
        {
            return Load(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new MusicChoices();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, PorterSettings.JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
