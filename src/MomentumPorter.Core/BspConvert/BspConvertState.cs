namespace MomentumPorter.BspConvert;

/// <summary>
/// A BSPConvert conversion: its input and options, and once <see cref="BspConvertStep"/> ran, the files it wrote.
/// </summary>
public sealed class BspConvertState(string inputPath, BspConvertOptions options)
{
    public string InputPath { get; } = inputPath;

    public BspConvertOptions Options { get; } = options;

    /// <summary>
    /// Game-folder-shaped folder in the job's work directory that BSPConvert wrote into
    /// (maps/, maps/zones/local/, and with --nopak materials/, sound/, models/).
    /// </summary>
    public string? StagingDirectory { get; set; }

    /// <summary>Every file BSPConvert wrote, as full paths under <see cref="StagingDirectory"/>.</summary>
    public IReadOnlyList<string> ProducedFiles { get; set; } = [];

    /// <summary>The converted BSPs (maps/*.bsp), full paths under <see cref="StagingDirectory"/>.</summary>
    public IReadOnlyList<string> MapPaths => ProducedFiles.Where(x => IsIn(x, "maps") && x.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The timer zone files (maps/zones/local/*.json), full paths under <see cref="StagingDirectory"/>.</summary>
    public IReadOnlyList<string> ZonePaths => ProducedFiles.Where(x => IsIn(x, Path.Combine("maps", "zones", "local"))).ToList();

    /// <summary>Where each produced file was copied to by <see cref="CopyConvertedFilesStep"/>.</summary>
    public IReadOnlyList<string> InstalledFiles { get; set; } = [];

    /// <summary>A produced file's path relative to the staging folder, which is also its path in the game folder.</summary>
    public string GetGamePath(string producedFile)
    {
        return Path.GetRelativePath(StagingDirectory ?? throw new InvalidOperationException("Nothing has been converted yet"), producedFile);
    }

    private bool IsIn(string file, string folder)
    {
        return StagingDirectory is not null
            && string.Equals(Path.GetDirectoryName(GetGamePath(file)), folder, StringComparison.OrdinalIgnoreCase);
    }
}
