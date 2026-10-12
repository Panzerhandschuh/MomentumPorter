namespace MomentumPorter.BspConvert;

using System.IO.Compression;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// BSPConvert reads Quake 3's base content (the textures, shaders and sounds maps use without shipping them) from a
/// Q3Content folder next to the running program, and packs what a map uses from it into the map. The user is meant to
/// fill it by extracting pak0.pk3 by hand. This fills it from the Quake 3 install instead (the Settings page's, else
/// Steam's), like the KZ VPKs are extracted for bspPack: once, and again when the pak files change.
/// </summary>
public static class Quake3Content
{
    // Left in the folder while extracting, so an interrupted extraction is redone next time
    private const string IncompleteMarker = ".momentumporter-incomplete";

    // The pak files the folder was extracted from (path, size, time), so it's redone when they change. A folder with
    // files but no stamp was filled by hand and is left alone.
    private const string StampFile = ".momentumporter-paks";

    // What BSPConvert ships in the folder, kept when it's re-extracted
    private const string Instructions = "instructions.txt";

    /// <summary>The folder BSPConvert reads, see Q3Converter.q3ContentDir.</summary>
    public static string FolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Q3Content");

    /// <summary>
    /// Whether the folder has no content, by BSPConvert's rule (it ships with a single instructions.txt).
    /// </summary>
    public static bool IsEmpty(string directory)
    {
        if (!Directory.Exists(directory))
            return true;

        if (File.Exists(Path.Combine(directory, IncompleteMarker)))
            return true;

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(2).Count() <= 1;
    }

    /// <summary>
    /// Quake 3's baseq3 folder: the one set for Quake 3 on the Settings page (which may be Quake Live's), else the
    /// Quake 3 install in the Steam libraries. Null when there's neither.
    /// </summary>
    public static string? FindContentDirectory(SteamLocator steam, PorterSettings? settings = null)
    {
        if (settings?.ResolveGameContentDirectory(SourceGames.Quake3) is { } set)
            return set;

        try
        {
            return SourceGames.LocateContentDirectory(SourceGames.Quake3, steam);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder's pak files in load order (pak0.pk3 first; later ones patch earlier ones, as in the game).
    /// </summary>
    public static IReadOnlyList<string> FindPakFiles(string? baseq3)
    {
        if (baseq3 is null || !Directory.Exists(baseq3))
            return [];

        return Directory.GetFiles(baseq3, "pak*.pk3")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts the pak files into the content folder, later paks overwriting earlier ones' files, replacing what an
    /// earlier extraction left there.
    /// </summary>
    public static async Task ExtractAsync(IReadOnlyList<string> pakFiles, string directory, IPortLog log, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, IncompleteMarker);
        await File.WriteAllTextAsync(marker, "MomentumPorter is extracting Quake 3's pak files here", cancellation).ConfigureAwait(false);
        File.Delete(Path.Combine(directory, StampFile));

        foreach (string file in Directory.EnumerateFiles(directory).Where(x => !IsOwnFile(directory, x)))
            File.Delete(file);
        foreach (string folder in Directory.EnumerateDirectories(directory))
            Directory.Delete(folder, recursive: true);

        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        foreach (string pak in pakFiles)
        {
            log.Info($"Extracting {pak} into {directory}");
            using ZipArchive archive = ZipFile.OpenRead(pak);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/'))
                    continue;

                string target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || IsOwnFile(directory, target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        await File.WriteAllTextAsync(Path.Combine(directory, StampFile), Stamp(pakFiles), cancellation).ConfigureAwait(false);
        File.Delete(marker);
    }

    /// <summary>
    /// Fills BSPConvert's Q3Content folder from the Quake 3 install (see <see cref="FindContentDirectory"/>) when it's
    /// empty or was extracted from other pak files. Logs a warning and carries on when Quake 3 isn't found or the folder
    /// can't be written, since BSPConvert still converts without it.
    /// </summary>
    public static Task EnsureAsync(SteamLocator steam, PorterSettings? settings, IPortLog log, CancellationToken cancellation)
    {
        return EnsureAsync(FolderPath, FindContentDirectory(steam, settings), log, cancellation);
    }

    /// <inheritdoc cref="EnsureAsync(SteamLocator, PorterSettings?, IPortLog, CancellationToken)"/>
    /// <param name="baseq3">Quake 3's baseq3 folder, or null when it wasn't found</param>
    public static async Task EnsureAsync(string directory, string? baseq3, IPortLog log, CancellationToken cancellation)
    {
        bool empty = IsEmpty(directory);
        string stampPath = Path.Combine(directory, StampFile);
        if (!empty && !File.Exists(stampPath))
            return;

        IReadOnlyList<string> paks = FindPakFiles(baseq3);
        if (paks.Count == 0)
        {
            if (empty)
            {
                log.Warn(baseq3 is null
                    ? $"Quake 3 wasn't found in your Steam libraries, so maps can't use its base textures, shaders and sounds. Set its folder on the Settings page, or extract baseq3/pak0.pk3 into {directory} by hand."
                    : $"{baseq3} has no pak files, so maps can't use Quake 3's base textures, shaders and sounds. Set Quake 3's baseq3 folder on the Settings page, or extract baseq3/pak0.pk3 into {directory} by hand.");
            }

            return;
        }

        if (!empty && File.ReadAllText(stampPath) == Stamp(paks))
            return;

        try
        {
            log.Info($"Extracting Quake 3's pak files from {baseq3} so maps get its base textures, shaders and sounds packed in. This happens once.");
            await ExtractAsync(paks, directory, log, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warn($"Couldn't set up Quake 3's base content in {directory}: {ex.Message}");
        }
    }

    private static string Stamp(IEnumerable<string> pakFiles)
    {
        return string.Join('\n', pakFiles.Select(x =>
        {
            var info = new FileInfo(x);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }));
    }

    // The files at the top of the folder that aren't Quake 3's
    private static bool IsOwnFile(string directory, string path)
    {
        if (!Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetDirectoryName(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase))
            return false;

        string name = Path.GetFileName(path);
        return name.Equals(IncompleteMarker, StringComparison.OrdinalIgnoreCase)
            || name.Equals(StampFile, StringComparison.OrdinalIgnoreCase)
            || name.Equals(Instructions, StringComparison.OrdinalIgnoreCase);
    }
}
