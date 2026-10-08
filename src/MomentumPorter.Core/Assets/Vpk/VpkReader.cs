namespace MomentumPorter.Assets.Vpk;

using ValvePak;

/// <summary>
/// Reads a Source VPK (the <c>_dir.vpk</c> of a multi-part package, or a single-file one) through ValvePak.
/// Paths are game paths such as <c>models/weapons/v_rif_ak47.mdl</c>, matched case-insensitively.
/// </summary>
public sealed class VpkReader : IContentSource
{
    private readonly Package _package;

    public VpkReader(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{path} doesn't exist", path);

        Path = path;
        _package = new Package();
        try
        {
            // Sorts the entries as they're read, for case-insensitive binary search lookups
            _package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            _package.Read(path);
        }
        catch
        {
            _package.Dispose();
            throw;
        }
    }

    public string Path { get; }

    public string Description => System.IO.Path.GetFileName(Path);

    public bool Exists(string path)
    {
        return _package.FindEntry(ContentPath.Normalize(path)) is not null;
    }

    /// <summary>
    /// The file's bytes, or null if the VPK doesn't have it.
    /// </summary>
    public byte[]? Read(string path)
    {
        PackageEntry? entry = _package.FindEntry(ContentPath.Normalize(path));
        if (entry is null)
            return null;

        _package.ReadEntry(entry, out byte[] data);
        return data;
    }

    /// <summary>
    /// Every file whose path starts with the prefix (e.g. <c>models/weapons/</c>), as stored in the VPK.
    /// </summary>
    public IEnumerable<string> List(string prefix = "")
    {
        string normalized = ContentPath.Normalize(prefix);
        return _package.Entries is null
            ? []
            : _package.Entries.Values
                .SelectMany(x => x)
                .Select(x => x.GetFullPath())
                .Where(x => x.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _package.Dispose();
    }
}
