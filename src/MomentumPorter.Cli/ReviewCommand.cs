namespace MomentumPorter.Cli;

using CommandLine;
using MomentumPorter.Detection;
using MomentumPorter.Review;

[Verb("review", HelpText = "Check a Source map against Momentum's entity tools: which entities still need changing by hand in game, and whether a map that has been through the tools has anything left")]
internal sealed class ReviewCliOptions
{
    [Value(0, Required = true, MetaName = "map", HelpText = "The map to check (.bsp), e.g. one ported into Momentum's maps folder and saved from Lumper")]
    public required string Map { get; set; }

    [Option("steps", HelpText = "Also print the steps to submit the map to Momentum Mod's website")]
    public bool Steps { get; set; }
}

/// <summary>
/// <c>MomentumPorter review &lt;map.bsp&gt;</c>: the entity review on its own, for maps already ported. Exits with 1
/// when a fix the docs say to always apply is missing.
/// </summary>
internal static class ReviewCommand
{
    public const string Verb = "review";

    public static int Run(string[] args)
    {
        // The verb form, so "review" itself isn't taken for the map
        return new Parser(with =>
            {
                with.HelpWriter = Console.Error;
                with.CaseSensitive = false;
            })
            .ParseArguments(args, typeof(ReviewCliOptions))
            .MapResult((ReviewCliOptions options) => Run(options), _ => 1);
    }

    private static int Run(ReviewCliOptions options)
    {
        if (!File.Exists(options.Map))
        {
            Console.Error.WriteLine($"{options.Map} doesn't exist");
            return 1;
        }

        if (InputDetector.Detect(options.Map) != InputKind.SourceBsp)
        {
            Console.Error.WriteLine($"{Path.GetFileName(options.Map)} isn't a Source engine map; the entity review is for those");
            return 1;
        }

        string mapName = Path.GetFileNameWithoutExtension(options.Map);
        EntityReview review = EntityReview.AnalyzeFile(options.Map);
        review.Print(Console.Out);

        if (options.Steps)
        {
            Console.WriteLine();
            SubmissionSteps.Print(new Program.CliLog(), mapName, SubmissionSteps.For(mapName, review, zoneFileWritten: false, mountedGame: null));
        }

        return review.HasMissingRequiredFixes ? 1 : 0;
    }
}
