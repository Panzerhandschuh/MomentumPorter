namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Assets.Vpk;

/// <summary>
/// Where a pack's files go. Paths are game paths (<c>scripts/weapon_momentum_pistol.txt</c>); a pack folder or VPK
/// in <c>momentum/custom/</c> holds them as they are.
/// </summary>
public interface IPackSink
{
    void AddFile(string relativePath, byte[] data);

    /// <summary>Copies a file from disk into the pack.</summary>
    void AddFile(string relativePath, string sourcePath);

    /// <summary>Every game path added so far, in order.</summary>
    IReadOnlyList<string> Files { get; }

    /// <summary>A file already in the pack (from this run or an earlier one), or null.</summary>
    byte[]? ReadFile(string relativePath);

    /// <summary>The game paths of the files in a folder of the pack, from this run or earlier ones.</summary>
    IReadOnlyList<string> ListFiles(string folder);

    /// <summary>Removes a file of the pack, from this run or an earlier one.</summary>
    void DeleteFile(string relativePath);
}

/// <summary>
/// Writes a pack as a plain folder (e.g. <c>momentum/custom/&lt;pack&gt;/</c>, or a temp folder to be packed later).
/// </summary>
public sealed class FolderPackSink : IPackSink
{
    private readonly List<string> _files = [];

    public FolderPackSink(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public IReadOnlyList<string> Files => _files;

    public void AddFile(string relativePath, byte[] data)
    {
        string target = Resolve(relativePath);
        File.WriteAllBytes(target, data);
        Track(relativePath);
    }

    public void AddFile(string relativePath, string sourcePath)
    {
        string target = Resolve(relativePath);
        if (!string.Equals(Path.GetFullPath(sourcePath), target, StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, overwrite: true);

        Track(relativePath);
    }

    public byte[]? ReadFile(string relativePath)
    {
        string target = Resolve(relativePath);
        return File.Exists(target) ? File.ReadAllBytes(target) : null;
    }

    public void DeleteFile(string relativePath)
    {
        string target = Resolve(relativePath);
        if (File.Exists(target))
            File.Delete(target);

        _files.RemoveAll(x => x.Equals(ContentPath.Normalize(relativePath), StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> ListFiles(string folder)
    {
        string normalized = ContentPath.Normalize(folder).TrimEnd('/');
        string directory = Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
            return [];

        return Directory.GetFiles(directory)
            .Select(x => normalized + "/" + Path.GetFileName(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string Resolve(string relativePath)
    {
        string normalized = ContentPath.Normalize(relativePath);
        string target = Path.GetFullPath(Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{relativePath} is outside the pack", nameof(relativePath));

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return target;
    }

    private void Track(string relativePath)
    {
        string normalized = ContentPath.Normalize(relativePath);
        if (!_files.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            _files.Add(normalized);
    }
}
