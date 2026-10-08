namespace MomentumPorter.Assets.Output;

using System.Text;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;
using MomentumPorter.Tools;

/// <summary>
/// Assembles a pack for Momentum's <c>custom</c> folder (files laid out as in the game folder, e.g.
/// <c>scripts/x.txt</c>, <c>sound/player/x.wav</c>) and writes it as a folder or a VPK. Files in
/// <c>momentum/custom/&lt;pack&gt;/</c> or <c>&lt;pack&gt;.vpk</c> override the game's own, so a pack never edits base files.
/// </summary>
public sealed class CustomPackWriter
{
    /// <summary>
    /// Written into folder packs so a later run knows it may replace the folder. Folders without it are never deleted.
    /// </summary>
    public const string MarkerFileName = "momentumporter_pack.txt";

    private readonly SortedDictionary<string, FileSource> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="name">Pack name: the folder name, or the VPK's name without extension</param>
    public CustomPackWriter(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException($"\"{name}\" isn't a valid pack name", nameof(name));

        Name = name;
    }

    public string Name { get; }

    /// <summary>The pack's files (relative, forward slashes, lower case), sorted.</summary>
    public IReadOnlyCollection<string> Files => _files.Keys;

    public bool Contains(string relativePath) => _files.ContainsKey(NormalizePath(relativePath));

    /// <summary>Adds or replaces a file with the given contents.</summary>
    public void AddFile(string relativePath, byte[] bytes)
    {
        _files[NormalizePath(relativePath)] = new FileSource(bytes, null);
    }

    /// <summary>Adds or replaces a file, copied from disk when the pack is written.</summary>
    public void AddFile(string relativePath, string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"{sourcePath} doesn't exist", sourcePath);

        _files[NormalizePath(relativePath)] = new FileSource(null, sourcePath);
    }

    /// <summary>Adds or replaces a text file (UTF-8 without a BOM, which Source's parsers don't skip everywhere).</summary>
    public void AddText(string relativePath, string text)
    {
        AddFile(relativePath, new UTF8Encoding(false).GetBytes(text));
    }

    public byte[] ReadFile(string relativePath)
    {
        FileSource source = _files[NormalizePath(relativePath)];
        return source.Bytes ?? File.ReadAllBytes(source.Path!);
    }

    /// <summary>
    /// Momentum's custom folder (<c>momentum/custom</c>) for an install found by <see cref="SteamLocator"/>.
    /// </summary>
    public static string? FindCustomDirectory(GameLocation? momentum)
    {
        return momentum?.ContentDirectory is null ? null : Path.Combine(momentum.ContentDirectory, "custom");
    }

    /// <summary>Momentum's vpk.exe (bin/win64), or null if it's missing.</summary>
    public static string? FindVpkTool(GameLocation? momentum)
    {
        if (momentum is null)
            return null;

        string path = Path.Combine(momentum.InstallDirectory, "bin", "win64", "vpk.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The path <see cref="WriteAsync"/> writes to in <paramref name="outputDirectory"/>.
    /// </summary>
    public string GetOutputPath(string outputDirectory, bool asVpk)
    {
        return Path.Combine(outputDirectory, asVpk ? Name + ".vpk" : Name);
    }

    /// <summary>
    /// Writes the pack into <paramref name="outputDirectory"/> (Momentum's custom folder, or any folder for a dry
    /// run) as <c>&lt;name&gt;/</c> or <c>&lt;name&gt;.vpk</c>. The pack is staged in <paramref name="stagingDirectory"/>
    /// first, so a failure leaves the output untouched. Returns the written folder or VPK.
    /// </summary>
    /// <param name="vpkTool">vpk.exe, needed when <paramref name="asVpk"/> is set</param>
    /// <param name="overwrite">Replace an existing VPK, or a folder an earlier run wrote. Other folders are never replaced.</param>
    public async Task<string> WriteAsync(
        string outputDirectory,
        bool asVpk,
        string stagingDirectory,
        IPortLog log,
        string? vpkTool = null,
        bool overwrite = true,
        CancellationToken cancellation = default)
    {
        if (_files.Count == 0)
            throw new PortStepException($"Pack {Name} has no files");

        if (asVpk && (vpkTool is null || !File.Exists(vpkTool)))
            throw new PortStepException($"Can't build {Name}.vpk without vpk.exe (Momentum's bin/win64/vpk.exe{(vpkTool is null ? "" : $", looked for {vpkTool}")}). Write a folder instead or set Momentum's folder.");

        string target = GetOutputPath(outputDirectory, asVpk);
        CheckTarget(target, asVpk, overwrite);

        // Stage as <staging>/<guid>/<name>/ so vpk.exe names the VPK after the pack and nothing else is in the way
        string stagingRoot = Path.Combine(stagingDirectory, "pack_" + Guid.NewGuid().ToString("N")[..8]);
        string staged = Path.Combine(stagingRoot, Name);
        Directory.CreateDirectory(staged);
        try
        {
            foreach ((string relativePath, FileSource source) in _files)
            {
                cancellation.ThrowIfCancellationRequested();
                string path = Path.Combine(staged, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (source.Bytes is not null)
                    await File.WriteAllBytesAsync(path, source.Bytes, cancellation).ConfigureAwait(false);
                else
                    File.Copy(source.Path!, path, overwrite: true);
            }

            Directory.CreateDirectory(outputDirectory);
            if (asVpk)
            {
                string builtVpk = Path.Combine(stagingRoot, Name + ".vpk");
                ExternalToolResult result = await ExternalTool.RunAsync(vpkTool!, [staged], log, stagingRoot, cancellation).ConfigureAwait(false);
                if (!result.Succeeded || !File.Exists(builtVpk))
                    throw new PortStepException($"vpk.exe failed to pack {Name} (exit code {result.ExitCode})");

                File.Copy(builtVpk, target, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(staged, MarkerFileName), MarkerText(), cancellation).ConfigureAwait(false);
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);

                CopyDirectory(staged, target);
            }
        }
        finally
        {
            TryDelete(stagingRoot);
        }

        log.Info($"Wrote {_files.Count} files to {target}");

        // The game mounts both, preferring VPKs; a leftover of the other kind would shadow or mix with this one
        string other = GetOutputPath(outputDirectory, !asVpk);
        if (File.Exists(other) || Directory.Exists(other))
            log.Warn($"{other} also exists and overrides the same files; remove whichever you don't want");

        return target;
    }

    private static void CheckTarget(string target, bool asVpk, bool overwrite)
    {
        if (asVpk)
        {
            if (File.Exists(target) && !overwrite)
                throw new PortStepException($"{target} already exists");
            return;
        }

        if (File.Exists(target))
            throw new PortStepException($"{target} is a file, not a pack folder");

        if (!Directory.Exists(target))
            return;

        if (!File.Exists(Path.Combine(target, MarkerFileName)))
            throw new PortStepException($"{target} already exists and wasn't made by MomentumPorter, so it won't be replaced. Pick another pack name.");

        if (!overwrite)
            throw new PortStepException($"{target} already exists");
    }

    private string MarkerText()
    {
        var text = new StringBuilder();
        text.AppendLine($"Made by MomentumPorter. Delete this folder to remove the pack; the next build of \"{Name}\" replaces it.");
        text.AppendLine();
        foreach (string file in _files.Keys)
            text.AppendLine(file);
        return text.ToString();
    }

    private static string NormalizePath(string relativePath)
    {
        string path = relativePath.Replace('\\', '/').Trim('/').ToLowerInvariant();
        if (path.Length == 0 || Path.IsPathRooted(relativePath) || path.Split('/').Any(x => x is "" or "." or ".."))
            throw new ArgumentException($"\"{relativePath}\" isn't a relative path inside the pack", nameof(relativePath));

        return path;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Left in the job's work folder, which gets cleaned up with it
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record FileSource(byte[]? Bytes, string? Path);
}
