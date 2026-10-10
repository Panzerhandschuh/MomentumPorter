namespace MomentumPorter.Steps;

using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Tools;

/// <summary>
/// Packs the assets the ported map uses from its source game into it with the bundled bspPack (<c>-M -C</c>), after
/// pointing it at the map's game (see <see cref="BspPackGames"/>). The map is packed in the job's work folder; if the
/// output lies elsewhere, it is copied in and back. Momentum's and the games' folders come from the
/// <see cref="PorterSettings"/> in the context.
/// </summary>
public sealed class BspPackStep : IPortStep
{
    public string Name => "Pack assets";

    public async Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
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
        await tool.PackAsync(target, context.Log, context.Cancellation).ConfigureAwait(false);

        if (!File.Exists(target))
            throw new PortStepException($"bspPack finished but {target} is gone");

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

    internal static bool IsInside(string path, string directory)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(dir, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
