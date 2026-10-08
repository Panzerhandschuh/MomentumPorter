namespace MomentumPorter.BspConvert;

using System.IO.Compression;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// BSPConvert reads Quake 3's base content (the textures, shaders and sounds maps use without shipping them) from a
/// Q3Content folder next to the running program, which the user is meant to fill by extracting pak0.pk3 by hand.
/// This fills it from the Quake 3 install Steam knows about instead.
/// </summary>
public static class Quake3Content
{
    // Left in the folder while extracting, so an interrupted extraction is redone next time
    private const string IncompleteMarker = ".momentumporter-incomplete";

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
    /// baseq3's pak files in load order (pak0.pk3 first; later ones patch earlier ones, as in the game).
    /// </summary>
    public static IReadOnlyList<string> FindPakFiles(SteamLocator steam)
    {
        string? baseq3 = steam.Find(KnownGame.Quake3)?.ContentDirectory;
        if (baseq3 is null)
            return [];

        return Directory.GetFiles(baseq3, "pak*.pk3")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts the pak files into the content folder, later paks overwriting earlier ones' files.
    /// </summary>
    public static async Task ExtractAsync(IReadOnlyList<string> pakFiles, string directory, IPortLog log, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, IncompleteMarker);
        await File.WriteAllTextAsync(marker, "MomentumPorter is extracting Quake 3's pak files here", cancellation).ConfigureAwait(false);

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
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        File.Delete(marker);
    }

    /// <summary>
    /// Fills BSPConvert's Q3Content folder from Steam's Quake 3 install if it's empty. Logs a warning and carries on
    /// when Quake 3 isn't installed or the folder can't be written, since BSPConvert still converts without it.
    /// </summary>
    public static async Task EnsureAsync(SteamLocator steam, IPortLog log, CancellationToken cancellation)
    {
        string directory = FolderPath;
        if (!IsEmpty(directory))
            return;

        IReadOnlyList<string> paks = FindPakFiles(steam);
        if (paks.Count == 0)
        {
            log.Warn($"Quake 3 isn't installed through Steam, so its base textures and shaders can't be set up. Extract baseq3/pak0.pk3 into {directory} by hand.");
            return;
        }

        try
        {
            await ExtractAsync(paks, directory, log, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warn($"Couldn't set up Quake 3's base content in {directory}: {ex.Message}");
        }
    }
}
