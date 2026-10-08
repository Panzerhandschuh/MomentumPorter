namespace MomentumPorter.Steps;

using System.Text;
using LibBSP;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

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

        foreach (ExtraOutput extra in state.PortContext?.ExtraOutputs ?? [])
        {
            string path = GetExtraOutputPath(directory ?? Directory.GetCurrentDirectory(), extra.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, extra.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            context.Log.Info($"Wrote {path}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Where an extra output goes, refusing paths that would leave the output's folder.
    /// </summary>
    public static string GetExtraOutputPath(string outputDirectory, string relativePath)
    {
        string root = Path.GetFullPath(outputDirectory);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (Path.IsPathRooted(relativePath) || !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Extra output {relativePath} isn't inside {root}");

        return path;
    }
}
