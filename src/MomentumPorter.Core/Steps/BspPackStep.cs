namespace MomentumPorter.Steps;

using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Tools;

/// <summary>
/// Packs the assets the ported map uses from its source game into it with the bundled bspPack (<c>-M</c>, and <c>-C</c>
/// when compressing; the map is written uncompressed for it, see <see cref="MapPortState.PackedAfterWriting"/>), after
/// pointing it at the map's game (see <see cref="BspPackGames"/>). Only Kreedz Climbing maps are packed
/// (<see cref="IGameProfile.PacksAssets"/>); Momentum mounts the other games. The map is packed in the job's work
/// folder; if the output lies elsewhere, it is copied in and back. Momentum's and the games' folders come from the
/// <see cref="PorterSettings"/> in the context.
/// </summary>
public sealed class BspPackStep : IPortStep
{
    public string Name => "Pack assets";

    public async Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        IGameProfile profile = state.Profile ?? throw new InvalidOperationException("The map's game profile hasn't been applied");
        if (!profile.PacksAssets)
        {
            context.Log.Info($"Nothing to pack: Momentum mounts {profile.MountedGame ?? "the map's game"}, so the map keeps the assets its author packed");
            return;
        }

        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();
        BspPackTool tool = context.TryGet<BspPackTool>() ?? new BspPackTool(BspPackTool.BundledPath);
        if (!tool.Exists)
            throw new PortStepException($"bspPack wasn't found at {tool.ExecutablePath}; it comes with the porter, so the install is incomplete");

        if (!File.Exists(state.OutputPath))
            throw new PortStepException($"{state.OutputPath} doesn't exist, there's nothing to pack");

        string output = Path.GetFullPath(state.OutputPath);
        bool inWorkDirectory = IsInside(output, context.WorkDirectory);
        // Keep the map's file name, bspPack goes by it (e.g. for cubemap materials)
        string target = inWorkDirectory ? output : Path.Combine(context.WorkDirectory, "pack", Path.GetFileName(output));
        if (!inWorkDirectory)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(output, target, overwrite: true);
        }

        BspPackGame game = PrepareGame(context, state, settings, tool);
        context.Log.Info($"bspPack packs assets from {game.Name} ({game.GameFolder})");

        long sizeBefore = new FileInfo(target).Length;
        List<int> lumpsBefore = ReadLumpsWithData(target);
        await tool.PackAsync(target, state.Options.Compress, context.Log, context.Cancellation).ConfigureAwait(false);

        if (!File.Exists(target))
            throw new PortStepException($"bspPack finished but {target} is gone");

        // bspzip reports success even when it couldn't read the lumps it rewrote, leaving a map the game can't load
        List<int> lost = lumpsBefore.Except(ReadLumpsWithData(target)).ToList();
        if (lost.Count > 0)
            throw new PortStepException($"bspPack finished but the packed map lost lumps {string.Join(", ", lost)}, so the game couldn't load it; see the log above");

        context.Log.Info($"Packed {Path.GetFileName(target)}: {sizeBefore / 1024.0 / 1024.0:0.0} MB -> {new FileInfo(target).Length / 1024.0 / 1024.0:0.0} MB");

        if (!inWorkDirectory)
            File.Copy(target, output, overwrite: true);
    }

    /// <summary>
    /// Sets up the game the map's profile packs from and points the bundled bspPack at it.
    /// </summary>
    private static BspPackGame PrepareGame(PortJobContext context, MapPortState state, PorterSettings settings, BspPackTool tool)
    {
        IGameProfile profile = state.Profile ?? throw new InvalidOperationException("The map's game profile hasn't been applied");
        SteamLocator steam = context.TryGet<SteamLocator>() ?? new SteamLocator();
        string momentumInstall = settings.ResolveMomentumContentDirectory(steam) is { } content
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(content)))!
            : throw new PortStepException("Momentum Mod wasn't found; bspPack needs its bspzip. Set Momentum's folder in Settings.");

        BspPackGames games = context.TryGet<BspPackGames>() ?? new BspPackGames(BspPackGames.DefaultRootDirectory);
        BspPackGame game = games.Prepare(profile.Name, state.GameDirectory, momentumInstall, settings, steam, context.Log, context.Cancellation);
        tool.WriteConfig(game, steam.SteamRoot);
        return game;
    }

    /// <summary>
    /// The lumps of a Source BSP that hold data, read from its header.
    /// </summary>
    public static List<int> ReadLumpsWithData(string bspPath)
    {
        using var reader = new BinaryReader(File.OpenRead(bspPath));
        // Ident and version, then 64 lumps of offset, length, version and uncompressed size
        reader.BaseStream.Position = 8;
        var lumps = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            reader.ReadInt32();
            if (reader.ReadInt32() > 0)
                lumps.Add(i);
            reader.ReadInt64();
        }

        return lumps;
    }

    internal static bool IsInside(string path, string directory)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(dir, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
