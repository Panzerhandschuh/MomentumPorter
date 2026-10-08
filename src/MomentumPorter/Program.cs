namespace MomentumPorter;

using CommandLine;
using LibBSP;
using MomentumPorter.Porting;

internal sealed class Options
{
    [Value(0, Required = true, MetaName = "input", HelpText = "BSP to port")]
    public required string Input { get; set; }

    [Value(1, Required = true, MetaName = "output", HelpText = "Where to write the ported BSP")]
    public required string Output { get; set; }

    [Option('g', "game", Default = "kzmod", HelpText = "Game the map was made for")]
    public required string Game { get; set; }

    [Option('c', "compress", HelpText = "LZMA compress the output")]
    public bool Compress { get; set; }

    [Option("game-dir", HelpText = "The game's folder holding gameinfo.txt, for game files the map uses (e.g. shared soundscapes). Defaults to the folder above the input's maps folder.")]
    public string? GameDirectory { get; set; }
}

internal static class Program
{
    public static int Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args).MapResult(Run, _ => 1);
    }

    private static int Run(Options options)
    {
        IGameProfile? profile = GameProfiles.Find(options.Game);
        if (profile is null)
        {
            Console.Error.WriteLine($"Unknown game \"{options.Game}\". Known games: {string.Join(", ", GameProfiles.All.Select(x => x.Name))}");
            return 1;
        }

        if (Path.GetFullPath(options.Input).Equals(Path.GetFullPath(options.Output), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("The output has to be a different file than the input");
            return 1;
        }

        var bsp = new BSP(new FileInfo(options.Input));
        if (!bsp.MapType.IsSubtypeOf(MapType.Source))
        {
            Console.Error.WriteLine($"{options.Input} isn't a Source engine map ({bsp.MapType})");
            return 1;
        }

        string? gameDirectory = options.GameDirectory ?? FindGameDirectory(options.Input);
        if (gameDirectory is not null && !File.Exists(Path.Combine(gameDirectory, "gameinfo.txt")))
        {
            Console.Error.WriteLine($"{gameDirectory} has no gameinfo.txt");
            return 1;
        }

        var context = new PortContext(bsp, Path.GetFileNameWithoutExtension(options.Output), gameDirectory);
        profile.Apply(context);
        context.CheckLimits();

        if (!Path.GetFileNameWithoutExtension(options.Input).Equals(Path.GetFileNameWithoutExtension(options.Output), StringComparison.OrdinalIgnoreCase))
            context.Report.Warn("packed files named after the map (e.g. its cubemap materials) keep the old map name");

        new BSPWriter(bsp).WriteBSP(options.Output, options.Compress);

        Console.WriteLine($"Ported {Path.GetFileName(options.Input)} from {profile.Description} to {options.Output}");
        context.Report.Print(Console.Out);
        return 0;
    }

    // <game>/maps/<map>.bsp
    private static string? FindGameDirectory(string input)
    {
        string? game = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input)));
        return game is not null && File.Exists(Path.Combine(game, "gameinfo.txt")) ? game : null;
    }
}
