namespace MomentumPorter.Assets.Vpk;

using System.IO.Compression;
using System.Text;

/// <summary>
/// Somewhere a game's files can come from: a VPK, a loose folder, a pk3 or a pak.
/// </summary>
public interface IContentSource : IDisposable
{
    /// <summary>Short name for logs and reports, e.g. <c>cstrike_pak_dir.vpk</c>.</summary>
    string Description { get; }

    bool Exists(string path);

    /// <summary>The file's bytes, or null if this source doesn't have it.</summary>
    byte[]? Read(string path);

    /// <summary>Every file whose game path starts with the prefix.</summary>
    IEnumerable<string> List(string prefix = "");
}

/// <summary>
/// Game paths: forward slashes, no leading slash.
/// </summary>
public static class ContentPath
{
    public static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// The file a soundscript wave refers to, under sound/, without the leading characters that set how it's played
    /// (<c>)weapons/x.wav</c> -> <c>sound/weapons/x.wav</c>).
    /// </summary>
    public static string SoundFile(string wave)
    {
        string trimmed = Normalize(wave.TrimStart('*', '#', '@', '>', '<', '^', ')', '(', '}', '$', '!', '?', '&', '~', '`', '+', '%'));
        return trimmed.StartsWith("sound/", StringComparison.OrdinalIgnoreCase) ? trimmed : "sound/" + trimmed;
    }
}

/// <summary>
/// Loose files under a folder.
/// </summary>
public sealed class FolderSource(string root) : IContentSource
{
    public string Root { get; } = root;

    public string Description => Root;

    public bool Exists(string path) => Find(path) is not null;

    public byte[]? Read(string path)
    {
        string? file = Find(path);
        return file is null ? null : File.ReadAllBytes(file);
    }

    /// <summary>
    /// The file on disk, or null. Matches case-insensitively, as GoldSrc and Quake do on any OS.
    /// </summary>
    public string? Find(string path)
    {
        string full = System.IO.Path.Combine(Root, ContentPath.Normalize(path).Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (File.Exists(full))
            return full;

        if (OperatingSystem.IsWindows())
            return null;

        // Case-insensitive lookup, one folder at a time
        string current = Root;
        foreach (string part in ContentPath.Normalize(path).Split('/'))
        {
            if (!Directory.Exists(current))
                return null;

            string? match = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(x => string.Equals(System.IO.Path.GetFileName(x), part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return null;

            current = match;
        }

        return File.Exists(current) ? current : null;
    }

    public IEnumerable<string> List(string prefix = "")
    {
        if (!Directory.Exists(Root))
            return [];

        string normalized = ContentPath.Normalize(prefix);
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(x => System.IO.Path.GetRelativePath(Root, x).Replace('\\', '/'))
            .Where(x => x.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() { }
}

/// <summary>
/// A zip archive, such as a Quake 3 pk3.
/// </summary>
public sealed class ZipSource : IContentSource
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ZipSource(string path)
    {
        Path = path;
        _archive = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry entry in _archive.Entries)
        {
            if (!entry.FullName.EndsWith('/'))
                _entries.TryAdd(ContentPath.Normalize(entry.FullName), entry);
        }
    }

    public string Path { get; }

    public string Description => System.IO.Path.GetFileName(Path);

    public bool Exists(string path) => _entries.ContainsKey(ContentPath.Normalize(path));

    public byte[]? Read(string path)
    {
        if (!_entries.TryGetValue(ContentPath.Normalize(path), out ZipArchiveEntry? entry))
            return null;

        using Stream stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public IEnumerable<string> List(string prefix = "")
    {
        string normalized = ContentPath.Normalize(prefix);
        return _entries.Keys.Where(x => x.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _archive.Dispose();
    }
}

/// <summary>
/// A Quake/GoldSrc pak file ("PACK": a directory of 56 byte names with offsets and lengths).
/// </summary>
public sealed class PakSource : IContentSource
{
    private readonly Dictionary<string, (int Offset, int Length)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public PakSource(string path)
    {
        Path = path;
        using FileStream stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "PACK")
            throw new InvalidDataException($"{path} isn't a pak file");

        int directoryOffset = reader.ReadInt32();
        int directoryLength = reader.ReadInt32();
        stream.Position = directoryOffset;
        for (int i = 0; i < directoryLength / 64; i++)
        {
            byte[] name = reader.ReadBytes(56);
            int end = Array.IndexOf(name, (byte)0);
            string entryName = Encoding.ASCII.GetString(name, 0, end < 0 ? name.Length : end);
            int offset = reader.ReadInt32();
            int length = reader.ReadInt32();
            _entries.TryAdd(ContentPath.Normalize(entryName), (offset, length));
        }
    }

    public string Path { get; }

    public string Description => System.IO.Path.GetFileName(Path);

    public bool Exists(string path) => _entries.ContainsKey(ContentPath.Normalize(path));

    public byte[]? Read(string path)
    {
        if (!_entries.TryGetValue(ContentPath.Normalize(path), out var entry))
            return null;

        using FileStream stream = File.OpenRead(Path);
        stream.Position = entry.Offset;
        byte[] data = new byte[entry.Length];
        stream.ReadExactly(data);
        return data;
    }

    public IEnumerable<string> List(string prefix = "")
    {
        string normalized = ContentPath.Normalize(prefix);
        return _entries.Keys.Where(x => x.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() { }
}
