namespace MomentumPorter.Assets.Common;

using SharpCompress.Archives.Zip;

/// <summary>
/// Reads files from one or more Quake 3 pk3s (zips) as one file system: like the game, an archive opened later
/// overrides files of the same path in earlier ones. Paths are case-insensitive and use forward slashes.
/// </summary>
public sealed class Pk3Reader : IDisposable
{
    private readonly List<ZipArchive> _archives = [];
    private readonly Dictionary<string, (ZipArchiveEntry Entry, string Archive)> _files = new(StringComparer.OrdinalIgnoreCase);

    private Pk3Reader() { }

    /// <summary>The pk3s, in the order they were opened.</summary>
    public IReadOnlyList<string> Archives { get; private set; } = [];

    /// <summary>Every file path, normalised (forward slashes), in no particular order.</summary>
    public IEnumerable<string> Files => _files.Keys;

    public static Pk3Reader Open(params IEnumerable<string> pk3Paths)
    {
        var reader = new Pk3Reader();
        var archives = new List<string>();
        try
        {
            foreach (string path in pk3Paths)
            {
                ZipArchive archive = ZipArchive.Open(path);
                reader._archives.Add(archive);
                archives.Add(path);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
                        continue;

                    reader._files[Normalize(entry.Key)] = (entry, path);
                }
            }
        }
        catch
        {
            reader.Dispose();
            throw;
        }

        reader.Archives = archives;
        return reader;
    }

    /// <summary>
    /// Opens every pk3 in a game folder (e.g. baseq3) in the game's load order (alphabetical, later ones override).
    /// </summary>
    public static Pk3Reader OpenDirectory(string directory)
    {
        string[] pk3s = Directory.GetFiles(directory, "*.pk3")
            .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Open(pk3s);
    }

    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    public bool Exists(string path) => _files.ContainsKey(Normalize(path));

    /// <summary>The pk3 a file comes from, or null if no pk3 has it.</summary>
    public string? ArchiveOf(string path) => _files.TryGetValue(Normalize(path), out var file) ? file.Archive : null;

    /// <summary>
    /// The files under a folder ("sound/player/"), recursively, with their full paths.
    /// </summary>
    public IEnumerable<string> List(string folder)
    {
        string prefix = Normalize(folder).TrimEnd('/') + "/";
        return _files.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public byte[] ReadAllBytes(string path)
    {
        return TryReadAllBytes(path) ?? throw new FileNotFoundException($"No pk3 has {path}", path);
    }

    public byte[]? TryReadAllBytes(string path)
    {
        if (!_files.TryGetValue(Normalize(path), out var file))
            return null;

        using Stream stream = file.Entry.OpenEntryStream();
        using var memory = new MemoryStream(file.Entry.Size > 0 && file.Entry.Size < int.MaxValue ? (int)file.Entry.Size : 0);
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public string? TryReadAllText(string path)
    {
        byte[]? bytes = TryReadAllBytes(path);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Extracts a file to disk, creating its folder.
    /// </summary>
    public void Extract(string path, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, ReadAllBytes(path));
    }

    public void Dispose()
    {
        foreach (ZipArchive archive in _archives)
            archive.Dispose();

        _archives.Clear();
        _files.Clear();
    }
}
