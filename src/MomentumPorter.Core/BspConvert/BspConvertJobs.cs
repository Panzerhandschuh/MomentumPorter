namespace MomentumPorter.BspConvert;

using MomentumPorter.Pipeline;
using MomentumPorter.Steam;

/// <summary>
/// Builds the jobs that convert Quake 3 and GoldSrc maps with BSPConvert.
/// </summary>
public static class BspConvertJobs
{
    /// <summary>
    /// Converts a Quake 3 / GoldSrc .bsp, .pk3 or .zip. Returns the job and the state the caller has to add to the
    /// job's context with <see cref="PortJobContext.Set{T}"/> before running it.
    /// </summary>
    /// <param name="copyToOutput">
    /// Copy the result into <see cref="BspConvertOptions.OutputDirectory"/>. Without it the files stay in the work
    /// directory, listed on the state, for a later step to install.
    /// </param>
    public static (PortJob Job, BspConvertState State) Create(string inputPath, BspConvertOptions options, bool copyToOutput = true, SteamLocator? steam = null)
    {
        var state = new BspConvertState(inputPath, options);
        var steps = new List<IPortStep> { new BspConvertStep(steam) };
        if (copyToOutput)
            steps.Add(new CopyConvertedFilesStep());

        return (new PortJob(Path.GetFileName(inputPath), steps), state);
    }
}
