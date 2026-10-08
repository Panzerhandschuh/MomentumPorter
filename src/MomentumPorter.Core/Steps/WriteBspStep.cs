namespace MomentumPorter.Steps;

using LibBSP;
using MomentumPorter.Pipeline;

/// <summary>
/// Writes the converted BSP to the job's output path.
/// </summary>
public sealed class WriteBspStep : IPortStep
{
    public string Name => "Write map";

    public Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        BSP bsp = state.Bsp ?? throw new InvalidOperationException("The map hasn't been loaded");

        string? directory = Path.GetDirectoryName(Path.GetFullPath(state.OutputPath));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        new BSPWriter(bsp).WriteBSP(state.OutputPath, state.Options.Compress);
        context.Log.Info($"Ported {Path.GetFileName(state.InputPath)} from {state.Profile?.Description} to {state.OutputPath}");
        return Task.CompletedTask;
    }
}
