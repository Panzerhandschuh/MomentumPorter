namespace MomentumPorter.Assets.Vpk;

/// <summary>
/// A game's files: its VPKs, paks and loose folders, searched in order like the game's search paths.
/// </summary>
public sealed class GameContent : IDisposable
{
    private readonly List<IContentSource> _sources;

    public GameContent(string name, IEnumerable<IContentSource> sources)
    {
        Name = name;
        _sources = sources.ToList();
    }

    public string Name { get; }

    public IReadOnlyList<IContentSource> Sources => _sources;

    public bool Exists(string path) => _sources.Any(x => x.Exists(path));

    /// <summary>
    /// The file from the first source that has it, or null.
    /// </summary>
    public byte[]? Read(string path)
    {
        foreach (IContentSource source in _sources)
        {
            if (source.Read(path) is { } data)
                return data;
        }

        return null;
    }

    /// <summary>
    /// The source the game would load the file from, or null.
    /// </summary>
    public IContentSource? FindSource(string path) => _sources.FirstOrDefault(x => x.Exists(path));

    /// <summary>
    /// Every file under the prefix, once each.
    /// </summary>
    public IEnumerable<string> List(string prefix = "")
    {
        return _sources.SelectMany(x => x.List(prefix)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies a file out to disk. Returns false if no source has it.
    /// </summary>
    public bool Extract(string path, string destination)
    {
        byte[]? data = Read(path);
        if (data is null)
            return false;

        string? directory = System.IO.Path.GetDirectoryName(destination);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(destination, data);
        return true;
    }

    public void Dispose()
    {
        foreach (IContentSource source in _sources)
            source.Dispose();
    }
}
