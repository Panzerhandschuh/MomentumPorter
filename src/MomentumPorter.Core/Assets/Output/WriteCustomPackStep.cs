namespace MomentumPorter.Assets.Output;

using MomentumPorter.Pipeline;

/// <summary>
/// Where a job's <see cref="CustomPackWriter"/> goes. Put both in the job's context before <see cref="WriteCustomPackStep"/> runs.
/// </summary>
/// <param name="OutputDirectory">Momentum's custom folder, or any folder for a dry run</param>
/// <param name="VpkTool">Momentum's vpk.exe, needed for VPKs</param>
public sealed record CustomPackTarget(string OutputDirectory, bool AsVpk, string? VpkTool, bool Overwrite = true);

/// <summary>
/// What <see cref="WriteCustomPackStep"/> wrote: the pack's folder or VPK.
/// </summary>
public sealed record CustomPackOutput(string Path, IReadOnlyCollection<string> Files);

/// <summary>
/// Writes the job's <see cref="CustomPackWriter"/> to its <see cref="CustomPackTarget"/>, as a folder or a VPK.
/// The last step of asset jobs; nothing before it touches the output folder.
/// </summary>
public sealed class WriteCustomPackStep : IPortStep
{
    public string Name => "Write pack";

    public async Task RunAsync(PortJobContext context)
    {
        CustomPackWriter pack = context.Get<CustomPackWriter>();
        CustomPackTarget target = context.Get<CustomPackTarget>();

        string path = await pack.WriteAsync(target.OutputDirectory, target.AsVpk, context.WorkDirectory, context.Log, target.VpkTool, target.Overwrite, context.Cancellation)
            .ConfigureAwait(false);

        context.Set(new CustomPackOutput(path, pack.Files.ToList()));
    }
}
