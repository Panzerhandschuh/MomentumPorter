namespace MomentumPorter.Steps;

using LibBSP;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// The map being ported: where it came from, where it goes, and the loaded BSP once <see cref="LoadBspStep"/> ran.
/// </summary>
public sealed class MapPortState(string inputPath, string outputPath, MapPortOptions options)
{
    public string InputPath { get; } = inputPath;

    /// <summary>Where the ported BSP is written. The install step may copy it elsewhere afterwards.</summary>
    public string OutputPath { get; set; } = outputPath;

    public MapPortOptions Options { get; } = options;

    public string MapName => Path.GetFileNameWithoutExtension(OutputPath);

    public BSP? Bsp { get; set; }

    public string? GameDirectory { get; set; }

    public IGameProfile? Profile { get; set; }

    public PortContext? PortContext { get; set; }
}

/// <summary>
/// Loads a Source engine BSP and finds the game folder it belongs to.
/// </summary>
public sealed class LoadBspStep : IPortStep
{
    public string Name => "Load map";

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();

        if (Path.GetFullPath(state.InputPath).Equals(Path.GetFullPath(state.OutputPath), StringComparison.OrdinalIgnoreCase))
            throw new PortStepException("The output has to be a different file than the input");

        if (!File.Exists(state.InputPath))
            throw new PortStepException($"{state.InputPath} doesn't exist");

        var bsp = new BSP(new FileInfo(state.InputPath));
        if (!bsp.MapType.IsSubtypeOf(MapType.Source))
            throw new PortStepException($"{state.InputPath} isn't a Source engine map ({bsp.MapType})");

        string? gameDirectory = state.Options.GameDirectory ?? FindGameDirectory(state.InputPath);
        if (gameDirectory is not null && !File.Exists(Path.Combine(gameDirectory, "gameinfo.txt")))
            throw new PortStepException($"{gameDirectory} has no gameinfo.txt");

        state.Bsp = bsp;
        state.GameDirectory = gameDirectory;
        return Task.CompletedTask;
    }

    // <game>/maps/<map>.bsp
    public static string? FindGameDirectory(string input)
    {
        string? game = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input)));
        return game is not null && File.Exists(Path.Combine(game, "gameinfo.txt")) ? game : null;
    }
}
