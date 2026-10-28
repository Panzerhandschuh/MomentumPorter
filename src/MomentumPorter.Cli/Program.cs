namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

// BSPConvert's options (Quake 3 and GoldSrc maps) are in QuakeGoldSrcOptions.cs, listed after these in --help
internal sealed partial class CliOptions
{
    [Value(0, Required = true, MetaName = "input", HelpText = "BSP to port: a Source map, or a Quake 3 or GoldSrc (Half-Life, CS 1.6) .bsp/.pk3/.zip")]
    public required string Input { get; set; }

    [Value(1, Required = true, MetaName = "output", HelpText = "Where to write the ported BSP. For Quake 3 and GoldSrc maps, the game folder (e.g. Momentum's momentum folder) the map goes into, as maps/<name>.bsp with its zone file.")]
    public required string Output { get; set; }

    [Option('g', "game", HelpText = "Game a Source map was made for: kzmod (Kreedz Climbing), gokz (CS:GO with the GOKZ mapping API) or css (Counter-Strike: Source). Detected from the map's entities if not given.")]
    public string? Game { get; set; }

    [Option('c', "compress", HelpText = "LZMA compress the output")]
    public bool Compress { get; set; }

    [Option("game-dir", HelpText = "The game's folder holding gameinfo.txt, for game files the map uses (e.g. shared soundscapes). Defaults to the folder above the input's maps folder.")]
    public string? GameDirectory { get; set; }
}

internal static class Program
{
    public static int Main(string[] args)
    {
        // Verbs come first; anything else is the original "<input.bsp> <output.bsp>" map port
        if (args.Length > 0 && args[0].Equals(SoundsCommand.Verb, StringComparison.OrdinalIgnoreCase))
            return SoundsCommand.Run(args);
        if (args.Length > 0 && args[0].Equals(WeaponCommand.Verb, StringComparison.OrdinalIgnoreCase))
            return WeaponCommand.Run(args);
        if (args.Length > 0 && args[0].Equals(ReviewCommand.Verb, StringComparison.OrdinalIgnoreCase))
            return ReviewCommand.Run(args);

        return Parser.Default.ParseArguments<CliOptions>(args).MapResult(options => Run(options).GetAwaiter().GetResult(), _ => 1);
    }

    private static async Task<int> Run(CliOptions cli)
    {
        if (BspConvertCommand.Handles(cli))
            return await BspConvertCommand.RunAsync(cli);

        if (cli.Game is not null && GameProfiles.Find(cli.Game) is null)
        {
            Console.Error.WriteLine($"Unknown game \"{cli.Game}\". Known games: {string.Join(", ", GameProfiles.All.Select(x => x.Name))}");
            return 1;
        }

        var options = new MapPortOptions
        {
            Game = cli.Game,
            Compress = cli.Compress,
            ConvertZones = !cli.NoZones,
            GameDirectory = cli.GameDirectory,
            Pack = false,
            Install = false,
        };

        (PortJob job, object state) = PortJobFactory.CreateMapJob(cli.Input, cli.Output, options);

        string workDirectory = PortJob.CreateWorkDirectory(job.Name);
        try
        {
            var context = new PortJobContext { Log = new CliLog(), WorkDirectory = workDirectory };
            context.Set(state);
            // Nobody to ask on the command line: steps that would prompt (e.g. overwriting an installed map) decline
            context.Set<IUserPrompts>(new DeclinePrompts());
            // The map's Sounds review from the app, if it had one
            if (Settings.MusicChoices.TryLoad(Settings.MusicChoices.DefaultPath).Find(Path.GetFileNameWithoutExtension(cli.Input)) is { } music)
                context.Set(music);

            PortJobResult result = await job.RunAsync(context);
            result.Report.Print(Console.Out);
            if (!result.Succeeded)
                return 1;

            // What's left to do by hand: the entity tools, zoning, screenshots, the submission form
            if (state is Steps.MapPortState mapState)
            {
                Review.EntityReview? review = context.TryGet<Review.EntityReview>();
                if (review is not null)
                {
                    Console.WriteLine();
                    review.Print(Console.Out);
                }

                Console.WriteLine();
                bool zoned = mapState.PortContext?.ExtraOutputs.Any(x => x.RelativePath.StartsWith("zones/", StringComparison.OrdinalIgnoreCase)) == true;
                Review.SubmissionSteps.Print(new CliLog(), mapState.MapName, Review.SubmissionSteps.For(mapState.MapName, review, zoned, mapState.Profile?.MountedGame));
            }

            return 0;
        }
        catch (PortStepException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Leave it for the OS to clean up
            }
        }
    }

    /// <summary>
    /// Prints progress to stdout and problems to stderr, with no decoration, as the old CLI did.
    /// </summary>
    internal sealed class CliLog : IPortLog
    {
        public void Info(string message) => Console.WriteLine(message);

        public void Warn(string message) => Console.Error.WriteLine(message);

        public void Error(string message) => Console.Error.WriteLine(message);
    }
}
