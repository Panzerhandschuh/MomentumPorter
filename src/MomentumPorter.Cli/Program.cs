namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

internal sealed class CliOptions
{
    [Value(0, Required = true, MetaName = "input", HelpText = "BSP to port")]
    public required string Input { get; set; }

    [Value(1, Required = true, MetaName = "output", HelpText = "Where to write the ported BSP")]
    public required string Output { get; set; }

    [Option('g', "game", HelpText = "Game the map was made for (kzmod, ...). Detected from the map's entities if not given.")]
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
        if (args.Length > 0 && args[0].Equals(WeaponCommand.Verb, StringComparison.OrdinalIgnoreCase))
            return WeaponCommand.Run(args);

        return Parser.Default.ParseArguments<CliOptions>(args).MapResult(options => Run(options).GetAwaiter().GetResult(), _ => 1);
    }

    private static async Task<int> Run(CliOptions cli)
    {
        if (cli.Game is not null && GameProfiles.Find(cli.Game) is null)
        {
            Console.Error.WriteLine($"Unknown game \"{cli.Game}\". Known games: {string.Join(", ", GameProfiles.All.Select(x => x.Name))}");
            return 1;
        }

        var options = new MapPortOptions
        {
            Game = cli.Game,
            Compress = cli.Compress,
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

            PortJobResult result = await job.RunAsync(context);
            result.Report.Print(Console.Out);
            return result.Succeeded ? 0 : 1;
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
    private sealed class CliLog : IPortLog
    {
        public void Info(string message) => Console.WriteLine(message);

        public void Warn(string message) => Console.Error.WriteLine(message);

        public void Error(string message) => Console.Error.WriteLine(message);
    }
}
