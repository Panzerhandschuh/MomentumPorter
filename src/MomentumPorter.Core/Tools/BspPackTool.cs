namespace MomentumPorter.Tools;

using MomentumPorter.Pipeline;

/// <summary>
/// bspPack (CompilePal's packer as a standalone exe), which packs the assets a map uses from its game
/// into the BSP. It reads config.ini (game folder, Steam folder) and ResourceConfig.ini (include/exclude rules) from
/// its own folder, so the game it packs from is set up there, not passed on the command line. The porter comes with
/// its own copy (<see cref="BundledPath"/>) and writes both files for each map's game (see <see cref="BspPackGames"/>).
/// </summary>
public sealed class BspPackTool(string executablePath)
{
    public static string ExecutableName => OperatingSystem.IsWindows() ? "bspPack.exe" : "bspPack";

    /// <summary>The copy that comes with the porter (tools/bspPack), next to the app.</summary>
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "bspPack", ExecutableName);

    public string ExecutablePath { get; } = executablePath;

    public string ToolDirectory => Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)) ?? ".";

    public string ConfigPath => Path.Combine(ToolDirectory, "config.ini");

    public string ResourceConfigPath => Path.Combine(ToolDirectory, "ResourceConfig.ini");

    public bool Exists => File.Exists(ExecutablePath);

    /// <summary>
    /// Points bspPack at the game to pack from: config.ini names its game folder (and Steam, which bspPack checks),
    /// ResourceConfig.ini the game's files to leave out. Both are replaced.
    /// </summary>
    public void WriteConfig(BspPackGame game, string? steamRoot)
    {
        // bspPack reads every line in a section as a path, blank ones too
        var config = new List<string>
        {
            "[GameFolder]",
            $"# Written by MomentumPorter for {game.Name}, replaced before every pack",
            game.GameFolder,
        };
        if (steamRoot is not null && Directory.Exists(Path.Combine(steamRoot, "steamapps")))
            config.AddRange(["[SteamPath]", steamRoot]);
        File.WriteAllLines(ConfigPath, config);

        var resources = new List<string>
        {
            $"# Written by MomentumPorter for {game.Name}, replaced before every pack",
            "[IncludeFiles]",
            "[IncludeFileLists]",
            "[IncludeDirs]",
            "[IncludeSourceDirectories]",
            "[ExcludeFiles]",
        };
        if (game.ExcludeFiles.Count > 0)
            resources.Add($"# {game.Name}'s loose files that it never loads, since a VPK searched before them has the same file");
        resources.AddRange(game.ExcludeFiles);
        resources.AddRange(["[ExcludeDirs]", "[ExcludeVpks]", "[AddonInfo]"]);
        File.WriteAllLines(ResourceConfigPath, resources);
    }

    /// <summary>
    /// Packs the map's assets into it in place (<c>-M -C</c>: apply ResourceConfig.ini, compress afterwards).
    /// </summary>
    public async Task PackAsync(string bspPath, IPortLog log, CancellationToken cancellation = default)
    {
        if (!Exists)
            throw new PortStepException($"bspPack wasn't found at {ExecutablePath}; it comes with the porter, so the install is incomplete");

        if (!File.Exists(bspPath))
            throw new PortStepException($"{bspPath} doesn't exist, there's nothing to pack");

        ExternalToolResult result = await ExternalTool.RunAsync(
            ExecutablePath,
            ["-M", "-C", Path.GetFullPath(bspPath)],
            log,
            workingDirectory: Path.GetDirectoryName(Path.GetFullPath(bspPath)),
            cancellation).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new PortStepException($"bspPack ({ExecutablePath}) failed with exit code {result.ExitCode}, see the log above");
    }
}
