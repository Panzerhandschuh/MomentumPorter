namespace MomentumPorter.BspConvert;

using System.IO.Compression;
using BSPConvert.Lib;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// BSPConvert reads Quake 3's base content (the textures, shaders and sounds maps use without shipping them) from a
/// Q3Content folder next to the running program, and mods' (Team Arena's...) from Q3Mods/&lt;mod&gt;, and packs what a
/// map uses from them into the map. The user is meant to fill them by extracting the pk3s by hand. This fills them from
/// the Quake 3 install instead (the Settings page's, else Steam's), like the KZ VPKs are extracted for bspPack: once,
/// and again when the pk3s change.
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

    // Folders next to baseq3 that aren't content maps use: Defrag's pk3s only change its menus and HUD
    private static readonly string[] IgnoredMods = [Q3Mods.BaseGame, "defrag"];

    /// <summary>The folder BSPConvert reads, see Q3Converter.q3ContentDir.</summary>
    public static string FolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Q3Content");

    /// <summary>The folder BSPConvert reads mods' content from, a folder per mod, see <see cref="Q3Mods"/>.</summary>
    public static string ModsFolderPath => Q3Mods.DefaultDirectory;

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
        return FindPk3s(baseq3, "pak*.pk3");
    }

    /// <summary>
    /// The mods installed next to baseq3, like Team Arena's missionpack: the folders beside it with pk3s in them, by
    /// name. Defrag isn't one, since maps don't use its content.
    /// </summary>
    public static IReadOnlyList<string> FindMods(string? baseq3)
    {
        string? install = baseq3 is null ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseq3)));
        if (install is null || !Directory.Exists(install))
            return [];

        return Directory.GetDirectories(install)
            .Where(x => !IgnoredMods.Contains(Path.GetFileName(x), StringComparer.OrdinalIgnoreCase) && FindModPakFiles(x).Count > 0)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A mod's pk3s in load order (by name; later ones patch earlier ones, as in the game).
    /// </summary>
    public static IReadOnlyList<string> FindModPakFiles(string modDirectory)
    {
        return FindPk3s(modDirectory, "*.pk3");
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
    /// Fills BSPConvert's Q3Content folder and a Q3Mods folder per mod from the Quake 3 install (see
    /// <see cref="FindContentDirectory"/> and <see cref="FindMods"/>) where they're empty or were extracted from other
    /// pk3s. Logs a warning and carries on when Quake 3 isn't found or a folder can't be written, since BSPConvert
    /// still converts without them.
    /// </summary>
    public static Task EnsureAsync(SteamLocator steam, PorterSettings? settings, IPortLog log, CancellationToken cancellation)
    {
        return EnsureAsync(FolderPath, ModsFolderPath, FindContentDirectory(steam, settings), log, cancellation);
    }

    /// <inheritdoc cref="EnsureAsync(SteamLocator, PorterSettings?, IPortLog, CancellationToken)"/>
    /// <param name="directory">The Q3Content folder</param>
    /// <param name="modsDirectory">The Q3Mods folder</param>
    /// <param name="baseq3">Quake 3's baseq3 folder, or null when it wasn't found</param>
    public static async Task EnsureAsync(string directory, string modsDirectory, string? baseq3, IPortLog log, CancellationToken cancellation)
    {
        IReadOnlyList<string> paks = FindPakFiles(baseq3);
        if (paks.Count == 0)
        {
            if (IsEmpty(directory))
            {
                log.Warn(baseq3 is null
                    ? $"Quake 3 wasn't found in your Steam libraries, so maps can't use its base textures, shaders and sounds. Set its folder on the Settings page, or extract baseq3/pak0.pk3 into {directory} by hand."
                    : $"{baseq3} has no pak files, so maps can't use Quake 3's base textures, shaders and sounds. Set Quake 3's baseq3 folder on the Settings page, or extract baseq3/pak0.pk3 into {directory} by hand.");
            }

            return;
        }

        await EnsureExtractedAsync(directory, paks, "Quake 3's pak files", log, cancellation).ConfigureAwait(false);
        foreach (string mod in FindMods(baseq3))
        {
            string name = Path.GetFileName(mod);
            await EnsureExtractedAsync(Path.Combine(modsDirectory, name), FindModPakFiles(mod), $"{Q3Mods.GetDisplayName(name)}'s pk3s", log, cancellation).ConfigureAwait(false);
        }
    }

    // Extracts the pk3s into the folder unless it has them already or was filled by hand
    private static async Task EnsureExtractedAsync(string directory, IReadOnlyList<string> pk3s, string what, IPortLog log, CancellationToken cancellation)
    {
        bool empty = IsEmpty(directory);
        string stampPath = Path.Combine(directory, StampFile);
        if (!empty && (!File.Exists(stampPath) || File.ReadAllText(stampPath) == Stamp(pk3s)))
            return;

        try
        {
            log.Info($"Extracting {what} from {Path.GetDirectoryName(pk3s[0])} so maps get the textures, shaders and sounds they use from it packed in. This happens once.");
            await ExtractAsync(pk3s, directory, log, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Warn($"Couldn't extract {what} into {directory}: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> FindPk3s(string? directory, string pattern)
    {
        if (directory is null || !Directory.Exists(directory))
            return [];

        return Directory.GetFiles(directory, pattern)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
