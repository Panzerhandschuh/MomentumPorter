namespace MomentumPorter.Tools;

using System.Text;
using System.Text.RegularExpressions;
using MomentumPorter.Pipeline;

/// <summary>
/// bspPack (CompilePal's packer as a standalone exe), which packs the assets a map uses from its game
/// into the BSP. It reads config.ini (game folder, Steam folder) and ResourceConfig.ini (include/exclude rules) from
/// its own folder, so the game it packs from is set up there, not passed on the command line.
/// </summary>
public sealed class BspPackTool(string executablePath)
{
    public static string ExecutableName => OperatingSystem.IsWindows() ? "bspPack.exe" : "bspPack";

    public string ExecutablePath { get; } = executablePath;

    public string ToolDirectory => Path.GetDirectoryName(Path.GetFullPath(ExecutablePath)) ?? ".";

    public string ConfigPath => Path.Combine(ToolDirectory, "config.ini");

    public string ResourceConfigPath => Path.Combine(ToolDirectory, "ResourceConfig.ini");

    public bool Exists => File.Exists(ExecutablePath);

    /// <summary>
    /// Whether it has a config.ini. Without one, bspPack asks which game to pack from and waits for an answer.
    /// </summary>
    public bool IsConfigured => File.Exists(ConfigPath);

    /// <summary>The copy that comes with the porter (tools/bspPack), next to the app.</summary>
    public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "bspPack", ExecutableName);

    /// <summary>
    /// Whether this is the copy that comes with the porter. The porter writes its config.ini and ResourceConfig.ini
    /// for each map; other copies are set up by hand.
    /// </summary>
    public bool IsBundled => string.Equals(Path.GetFullPath(ExecutablePath), Path.GetFullPath(BundledPath),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Places bspPack is looked for when the settings don't name one: the copy bundled next to the app, then a local
    /// build in Documents/Code/bspPack.
    /// </summary>
    public static IEnumerable<string> DefaultCandidates()
    {
        yield return BundledPath;
        yield return Path.Combine(AppContext.BaseDirectory, "tools", "bspPack", ExecutableName);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.Length > 0)
            yield return Path.Combine(documents, "Code", "bspPack", "bin", "Release", "net9.0", OperatingSystem.IsWindows() ? "win-x64" : "linux-x64", ExecutableName);
    }

    public static string? FindDefault() => DefaultCandidates().FirstOrDefault(File.Exists);

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
            $"# Written by MomentumPorter for {game.Name}, replaced before every pack. Use a pre-pack command to change it.",
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
    /// The game folder bspPack's config.ini points at, to tell the user where assets will come from.
    /// </summary>
    public string? ReadConfiguredGameFolder()
    {
        if (!File.Exists(ConfigPath))
            return null;

        string? section = null;
        foreach (string raw in File.ReadLines(ConfigPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            if (section == "GameFolder")
                return line.Trim('"');
        }

        return null;
    }

    /// <summary>
    /// Packs the map's assets into it in place (<c>-M -C</c>: apply ResourceConfig.ini, compress afterwards).
    /// </summary>
    public async Task PackAsync(string bspPath, IPortLog log, CancellationToken cancellation = default)
    {
        if (!Exists)
            throw new PortStepException($"bspPack wasn't found at {ExecutablePath}. Set its location in Settings.");

        if (!File.Exists(bspPath))
            throw new PortStepException($"{bspPath} doesn't exist, there's nothing to pack");

        ExternalToolResult result = await ExternalTool.RunAsync(
            ExecutablePath,
            ["-M", "-C", Path.GetFullPath(bspPath)],
            log,
            workingDirectory: Path.GetDirectoryName(Path.GetFullPath(bspPath)),
            cancellation).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new PortStepException($"bspPack ({ExecutablePath}) failed with exit code {result.ExitCode}. Check its config.ini ({ConfigPath}) and the log above.");
    }

    /// <summary>
    /// Runs a user command before packing, e.g. a script that regenerates ResourceConfig.ini. The command goes
    /// through the shell (cmd on Windows), after <see cref="ExpandPrePackCommand"/>.
    /// </summary>
    public async Task RunPrePackCommandAsync(string command, string bspPath, string workDirectory, IPortLog log, CancellationToken cancellation = default)
    {
        string expanded = ExpandPrePackCommand(command, bspPath);
        string script;
        string shell;
        if (OperatingSystem.IsWindows())
        {
            script = Path.Combine(workDirectory, "prepack.cmd");
            // UTF-8 code page so paths with non-ASCII characters survive
            await File.WriteAllTextAsync(script, "@echo off\r\nchcp 65001 >nul\r\n" + expanded + "\r\n", new UTF8Encoding(false), cancellation).ConfigureAwait(false);
            shell = Environment.GetEnvironmentVariable("ComSpec") is { } comSpec && File.Exists(comSpec)
                ? comSpec
                : Path.Combine(Environment.SystemDirectory, "cmd.exe");
        }
        else
        {
            script = Path.Combine(workDirectory, "prepack.sh");
            await File.WriteAllTextAsync(script, expanded + "\n", cancellation).ConfigureAwait(false);
            shell = "/bin/sh";
        }

        log.Info($"Running pre-pack command: {expanded}");
        string[] arguments = OperatingSystem.IsWindows() ? ["/c", Path.GetFileName(script)] : [Path.GetFileName(script)];
        ExternalToolResult result = await ExternalTool.RunAsync(shell, arguments, log, workDirectory, cancellation).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new PortStepException($"The pre-pack command failed with exit code {result.ExitCode}: {expanded}");
    }

    /// <summary>
    /// Replaces <c>{bsppack}</c> (the exe), <c>{bsppackdir}</c>, <c>{resourceconfig}</c> (ResourceConfig.ini next to
    /// the exe) and <c>{bsp}</c> (the map being packed) in a pre-pack command. Paths are inserted unquoted.
    /// </summary>
    public string ExpandPrePackCommand(string command, string bspPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bsppack"] = Path.GetFullPath(ExecutablePath),
            ["bsppackdir"] = ToolDirectory,
            ["resourceconfig"] = ResourceConfigPath,
            ["bsp"] = Path.GetFullPath(bspPath),
        };

        return Regex.Replace(command, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out string? value) ? value : m.Value);
    }
}
