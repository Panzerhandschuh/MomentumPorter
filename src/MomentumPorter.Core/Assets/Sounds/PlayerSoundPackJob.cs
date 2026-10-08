namespace MomentumPorter.Assets.Sounds;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Output;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// A player sound pack job's input and what its steps found and built.
/// </summary>
public sealed class PlayerSoundPackState(PlayerSoundPackOptions options)
{
    public PlayerSoundPackOptions Options { get; } = options;

    /// <summary>Quake 3's baseq3 folder or pk3.</summary>
    public string? Quake3Path { get; set; }

    /// <summary>Momentum's content folder (the one with gameinfo.txt and scripts/).</summary>
    public string? MomentumContentDirectory { get; set; }

    public string? VpkTool { get; set; }

    public string? OutputDirectory { get; set; }

    public SoundscriptFile? Soundscript { get; set; }

    public IReadOnlyList<string> SurfaceFootsteps { get; set; } = [];

    public PlayerSoundPackResult? Result { get; set; }
}

/// <summary>
/// Builds the job that makes a Quake 3 player sound pack: find the games, build the pack, write it.
/// </summary>
public static class PlayerSoundPackJob
{
    /// <summary>
    /// Returns the job and the state the caller adds to the job's context with <see cref="PortJobContext.Set{T}"/>
    /// before running it. After a successful run, the context also has the <see cref="CustomPackOutput"/>.
    /// </summary>
    public static (PortJob Job, PlayerSoundPackState State) Create(PlayerSoundPackOptions options)
    {
        var state = new PlayerSoundPackState(options);
        var steps = new List<IPortStep>
        {
            new FindPlayerSoundInputsStep(),
            new BuildPlayerSoundPackStep(),
            new WriteCustomPackStep(),
        };

        return (new PortJob(options.ResolvePackName(), steps), state);
    }

    /// <summary>
    /// Momentum's content folder from an install folder, the content folder itself, or Steam when null.
    /// </summary>
    public static string? FindMomentumContent(string? path, SteamLocator? locator = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (locator ?? new SteamLocator()).FindMomentum()?.ContentDirectory;

        if (Directory.Exists(Path.Combine(path, "scripts")))
            return path;

        string content = Path.Combine(path, "momentum");
        return Directory.Exists(content) ? content : null;
    }

    /// <summary>The soundscript names surfaceproperties*.txt use for stepleft/stepright.</summary>
    public static IReadOnlyList<string> ReadSurfaceFootsteps(string scriptsDirectory)
    {
        if (!Directory.Exists(scriptsDirectory))
            return [];

        var regex = new Regex("\"step(?:left|right)\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return Directory.GetFiles(scriptsDirectory, "surfaceproperties*.txt")
            .SelectMany(file => regex.Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>
/// Finds Quake 3's pk3s, Momentum's soundscript and vpk.exe, and where the pack goes.
/// </summary>
public sealed class FindPlayerSoundInputsStep : IPortStep
{
    public string Name => "Find game files";

    public Task RunAsync(PortJobContext context)
    {
        PlayerSoundPackState state = context.Get<PlayerSoundPackState>();
        PlayerSoundPackOptions options = state.Options;
        var locator = new Lazy<SteamLocator>(() => new SteamLocator());

        state.Quake3Path = string.IsNullOrWhiteSpace(options.Quake3Path) ? Quake3SoundSource.FindInstalled(locator.Value) : options.Quake3Path;
        if (state.Quake3Path is null)
            throw new PortStepException("Quake 3 (or Quake Live) wasn't found through Steam; set its baseq3 folder");
        if (!File.Exists(state.Quake3Path) && !Directory.Exists(state.Quake3Path))
            throw new PortStepException($"{state.Quake3Path} doesn't exist");

        string? content = PlayerSoundPackJob.FindMomentumContent(options.MomentumDirectory, string.IsNullOrWhiteSpace(options.MomentumDirectory) ? locator.Value : null);
        if (content is null)
            throw new PortStepException(string.IsNullOrWhiteSpace(options.MomentumDirectory)
                ? "Momentum Mod wasn't found through Steam; set its folder"
                : $"{options.MomentumDirectory} isn't Momentum Mod's folder (no scripts folder)");
        state.MomentumContentDirectory = content;

        string soundscriptPath = Path.Combine(content, PlayerSoundPackBuilder.SoundscriptPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(soundscriptPath))
            throw new PortStepException($"{soundscriptPath} doesn't exist; the pack is built from Momentum's own copy");

        try
        {
            state.Soundscript = SoundscriptFile.Load(soundscriptPath);
        }
        catch (FormatException ex)
        {
            throw new PortStepException($"Couldn't read {soundscriptPath}: {ex.Message}", ex);
        }

        state.SurfaceFootsteps = PlayerSoundPackJob.ReadSurfaceFootsteps(Path.Combine(content, "scripts"));
        state.OutputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory) || options.OutputDirectory.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(content, "custom")
            : options.OutputDirectory;

        string vpk = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content))!, "bin", "win64", "vpk.exe");
        state.VpkTool = File.Exists(vpk) ? vpk : null;
        if (options.OutputAsVpk && state.VpkTool is null)
            throw new PortStepException($"Momentum's vpk.exe wasn't found ({vpk}); turn off \"Build a VPK\" to write a folder");

        context.Log.Info($"Quake 3 sounds: {state.Quake3Path}");
        context.Log.Info($"Momentum: {content}");
        WarnAboutShadowingOverrides(context, state);
        return Task.CompletedTask;
    }

    // Another folder in custom/ with its own copy of the soundscript would fight this pack over which one loads
    private static void WarnAboutShadowingOverrides(PortJobContext context, PlayerSoundPackState state)
    {
        string custom = Path.Combine(state.MomentumContentDirectory!, "custom");
        if (!Directory.Exists(custom))
            return;

        string packName = state.Options.ResolvePackName();
        foreach (string folder in Directory.GetDirectories(custom))
        {
            if (Path.GetFileName(folder).Equals(packName, StringComparison.OrdinalIgnoreCase))
                continue;

            string other = Path.Combine(folder, PlayerSoundPackBuilder.SoundscriptPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(other))
                context.Report.Warn($"{other} also replaces Momentum's player sounds and may win over this pack; rename or remove it");
        }
    }
}

/// <summary>
/// Reads the Quake 3 sounds and assembles the pack's files (nothing is written to the output yet).
/// </summary>
public sealed class BuildPlayerSoundPackStep : IPortStep
{
    public string Name => "Build sound pack";

    public Task RunAsync(PortJobContext context)
    {
        PlayerSoundPackState state = context.Get<PlayerSoundPackState>();
        PlayerSoundPackOptions options = state.Options;

        CustomPackWriter pack;
        try
        {
            pack = new CustomPackWriter(options.ResolvePackName());
        }
        catch (ArgumentException ex)
        {
            throw new PortStepException(ex.Message, ex);
        }

        using (Quake3SoundSource quake3 = OpenQuake3(state.Quake3Path!))
        {
            var builder = new PlayerSoundPackBuilder(quake3, context.Log, context.Report);
            state.Result = builder.Build(options, state.Soundscript!, pack, state.SurfaceFootsteps);
        }

        context.Set(pack);
        context.Set(new CustomPackTarget(state.OutputDirectory!, options.OutputAsVpk, state.VpkTool, options.Overwrite));
        return Task.CompletedTask;
    }

    private static Quake3SoundSource OpenQuake3(string path)
    {
        try
        {
            return Quake3SoundSource.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new PortStepException($"Couldn't open Quake 3's pk3s in {path}: {ex.Message}", ex);
        }
    }
}
