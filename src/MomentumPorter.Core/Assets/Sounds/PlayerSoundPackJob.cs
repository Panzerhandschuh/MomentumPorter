namespace MomentumPorter.Assets.Sounds;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// A player sound pack job's input and what its steps found and built.
/// </summary>
public sealed class PlayerSoundPackState(PlayerSoundPackOptions options)
{
    public PlayerSoundPackOptions Options { get; } = options;

    public SourceGameInfo? Game { get; set; }

    /// <summary>The game's folder (Quake 3's baseq3 folder or a pk3, the content folder of the others).</summary>
    public string? GamePath { get; set; }

    public SteamLocator? Steam { get; set; }

    /// <summary>Momentum's content folder (the one with gameinfo.txt and scripts/).</summary>
    public string? MomentumContentDirectory { get; set; }

    public string? VpkTool { get; set; }

    public string? OutputDirectory { get; set; }

    public SoundscriptFile? Soundscript { get; set; }

    /// <summary>The modes the pack is for, or empty for every mode.</summary>
    public IReadOnlyList<GameMode> GameModes { get; set; } = [];

    public IReadOnlyList<string> SurfaceFootsteps { get; set; } = [];

    /// <summary>Momentum's surfaces, to match footsteps through for Source games.</summary>
    public SurfaceProperties? MomentumSurfaces { get; set; }

    public PlayerSoundPackResult? Result { get; set; }
}

/// <summary>
/// Builds the job that makes a player sound pack: find the games, build the pack, write it.
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
/// Finds the game's sounds, Momentum's soundscript and vpk.exe, and where the pack goes.
/// </summary>
public sealed class FindPlayerSoundInputsStep : IPortStep
{
    public string Name => "Find game files";

    public Task RunAsync(PortJobContext context)
    {
        PlayerSoundPackState state = context.Get<PlayerSoundPackState>();
        PlayerSoundPackOptions options = state.Options;
        var locator = new Lazy<SteamLocator>(() => new SteamLocator());

        state.Game = PlayerSoundSources.Find(options.Game)
            ?? throw new PortStepException($"Unknown game \"{options.Game}\"; use {string.Join(", ", PlayerSoundSources.Games.Select(x => x.Id))}");
        try
        {
            state.GameModes = options.ResolveGameModes();
        }
        catch (FormatException ex)
        {
            throw new PortStepException(ex.Message, ex);
        }

        string? directory = options.ResolveGameDirectory();
        state.GamePath = PlayerSoundSources.Locate(state.Game, directory, directory is null ? locator.Value : null);
        if (state.GamePath is null)
        {
            throw new PortStepException(state.Game.Engine == ContentEngine.Quake3
                ? "Quake 3 (or Quake Live) wasn't found through Steam; set its baseq3 folder"
                : $"{state.Game.Name} wasn't found through Steam; set its folder");
        }

        if (!File.Exists(state.GamePath) && !Directory.Exists(state.GamePath))
            throw new PortStepException($"{state.GamePath} doesn't exist");

        if (state.Game.Engine != ContentEngine.Quake3)
            state.Steam = locator.Value;

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
        state.MomentumSurfaces = SurfaceProperties.Load(path =>
        {
            string file = Path.Combine(content, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(file) ? File.ReadAllText(file) : null;
        });
        state.OutputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory) || options.OutputDirectory.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(content, "custom")
            : options.OutputDirectory;

        string vpk = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(content))!, "bin", "win64", "vpk.exe");
        state.VpkTool = File.Exists(vpk) ? vpk : null;
        if (options.OutputAsVpk && state.VpkTool is null)
            throw new PortStepException($"Momentum's vpk.exe wasn't found ({vpk}); turn off \"Build a VPK\" to write a folder");

        context.Log.Info($"{state.Game.Name} sounds: {state.GamePath}");
        context.Log.Info($"Momentum: {content}");
        if (state.GameModes.Count > 0)
        {
            context.Report.Warn($"The sounds are only for {GameModes.Describe(state.GameModes)}, which needs a Momentum build with the gamemode-weapon-scripts engine change in docs/engine-changes.md (other builds ignore scripts/<mode>/ and keep Momentum's sounds)");
        }

        WarnAboutShadowingOverrides(context, state);
        return Task.CompletedTask;
    }

    // Another folder in custom/ with its own copy of the soundscript (or of a mode's overrides) would fight this pack
    // over which one loads
    private static void WarnAboutShadowingOverrides(PortJobContext context, PlayerSoundPackState state)
    {
        string custom = Path.Combine(state.MomentumContentDirectory!, "custom");
        if (!Directory.Exists(custom))
            return;

        string packName = state.Options.ResolvePackName();
        IEnumerable<string> paths = state.GameModes.Count == 0
            ? [PlayerSoundPackBuilder.SoundscriptPath]
            : state.GameModes.Select(PlayerSoundPackBuilder.ModeSoundscriptPath);
        foreach (string folder in Directory.GetDirectories(custom))
        {
            if (Path.GetFileName(folder).Equals(packName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string path in paths)
            {
                string other = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(other))
                    context.Report.Warn($"{other} also replaces Momentum's player sounds{(state.GameModes.Count == 0 ? "" : " in that mode")} and may win over this pack; rename or remove it");
            }
        }
    }
}

/// <summary>
/// Reads the game's sounds and assembles the pack's files (nothing is written to the output yet).
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

        using (PlayerSoundSource source = OpenSource(state))
        using (PlayerSoundSource? halfLife2 = OpenHalfLife2(state, source))
        {
            var builder = new PlayerSoundPackBuilder(source, context.Log, context.Report);
            state.Result = builder.Build(options, state.Soundscript!, pack, state.SurfaceFootsteps, halfLife2);
        }

        context.Set(pack);
        context.Set(new CustomPackTarget(state.OutputDirectory!, options.OutputAsVpk, state.VpkTool, options.Overwrite));
        return Task.CompletedTask;
    }

    // Momentum's own copy of HL2, for HL2's sprint sound with another game's sounds
    private static PlayerSoundSource? OpenHalfLife2(PlayerSoundPackState state, PlayerSoundSource source)
    {
        if (state.Options.SprintSound != SprintSoundMode.HalfLife2 || source.Game.Id == SourceGames.HalfLife2.Id)
            return null;

        string install = Path.GetDirectoryName(Path.GetFullPath(state.MomentumContentDirectory!))!;
        try
        {
            return SourceGameSoundSource.OpenMomentumHalfLife2(install);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PortStepException($"Couldn't open Momentum's Half-Life 2 files in {install}: {ex.Message}", ex);
        }
    }

    private static PlayerSoundSource OpenSource(PlayerSoundPackState state)
    {
        SourceGameInfo game = state.Game!;
        try
        {
            return PlayerSoundSources.Open(game, state.GamePath!, state.Steam ?? new SteamLocator(), state.MomentumSurfaces);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new PortStepException(game.Engine == ContentEngine.Quake3
                ? $"Couldn't open Quake 3's pk3s in {state.GamePath}: {ex.Message}"
                : $"Couldn't open {game.Name}'s files in {state.GamePath}: {ex.Message}", ex);
        }
    }
}
