namespace MomentumPorter.Pipeline;

using MomentumPorter.Detection;
using MomentumPorter.Options;
using MomentumPorter.Steps;

/// <summary>
/// Builds the job for an input. Each phase of the tool adds the step lists it knows how to run.
/// </summary>
public static class PortJobFactory
{
    /// <summary>
    /// Ports a Source engine map (KZ Mod, GOKZ...) with a game profile. Returns the job and the state the caller
    /// has to add to the job's context with <see cref="PortJobContext.Set{T}"/> before running it.
    /// </summary>
    public static (PortJob Job, MapPortState State) CreateSourceMapJob(string inputPath, string outputPath, MapPortOptions options)
    {
        var state = new MapPortState(inputPath, outputPath, options);
        var steps = new List<IPortStep>
        {
            new LoadBspStep(),
            new ApplyProfileStep(),
            new WriteBspStep(),
        };

        // Both read their tool and game locations from the PorterSettings in the job's context
        if (options.Pack)
            steps.Add(new BspPackStep());

        if (options.Install)
            steps.Add(new InstallStep());

        return (new PortJob(Path.GetFileName(inputPath), steps), state);
    }

    /// <summary>
    /// Builds the right map job for whatever kind of map file this is.
    /// </summary>
    public static (PortJob Job, object State) CreateMapJob(string inputPath, string outputPath, MapPortOptions options)
    {
        InputKind kind = InputDetector.Detect(inputPath);
        switch (kind)
        {
            case InputKind.SourceBsp:
            {
                (PortJob job, MapPortState state) = CreateSourceMapJob(inputPath, outputPath, options);
                return (job, state);
            }
            case InputKind.Quake3Bsp:
            case InputKind.GoldSrcBsp:
            case InputKind.Pk3Archive:
            case InputKind.ZipArchive:
                throw new PortStepException($"{Path.GetFileName(inputPath)} is a {Describe(kind)}; converting those (BSPConvert) isn't wired up yet");
            default:
                throw new PortStepException($"{Path.GetFileName(inputPath)} isn't a map file the porter understands");
        }
    }

    public static string Describe(InputKind kind)
    {
        return kind switch
        {
            InputKind.SourceBsp => "Source engine map",
            InputKind.GoldSrcBsp => "GoldSrc (Half-Life) map",
            InputKind.Quake3Bsp => "Quake 3 map",
            InputKind.Pk3Archive => "Quake 3 archive",
            InputKind.ZipArchive => "zip archive",
            InputKind.Vpk => "VPK",
            InputKind.StudioModel => "model",
            InputKind.Wav => "sound",
            _ => "unknown file",
        };
    }
}
