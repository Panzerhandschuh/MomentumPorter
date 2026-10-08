namespace MomentumPorter.BspConvert;

using MomentumPorter.Pipeline;

/// <summary>
/// Copies what <see cref="BspConvertStep"/> wrote into the output game folder (<see cref="BspConvertOptions.OutputDirectory"/>),
/// keeping the folder layout (maps/, maps/zones/local/, materials/...). Checks for existing files before copying any.
/// </summary>
public sealed class CopyConvertedFilesStep : IPortStep
{
    public string Name => "Copy to game folder";

    public Task RunAsync(PortJobContext context)
    {
        BspConvertState state = context.Get<BspConvertState>();
        string output = state.Options.OutputDirectory is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : throw new PortStepException("No output game folder was given");

        List<(string Source, string Target)> files = state.ProducedFiles
            .Select(x => (x, Path.Combine(output, state.GetGamePath(x))))
            .ToList();

        if (!state.Options.Overwrite)
        {
            List<string> existing = files.Where(x => File.Exists(x.Target)).Select(x => Path.GetRelativePath(output, x.Target)).ToList();
            if (existing.Count > 0)
            {
                string list = string.Join(", ", existing.Take(5)) + (existing.Count > 5 ? $" and {existing.Count - 5} more" : "");
                throw new PortStepException($"{list} {(existing.Count == 1 ? "is" : "are")} already in {output}. Allow overwriting to replace {(existing.Count == 1 ? "it" : "them")}.");
            }
        }

        var installed = new List<string>();
        foreach ((string source, string target) in files)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            installed.Add(target);
        }

        state.InstalledFiles = installed;
        foreach (string map in state.MapPaths)
            context.Log.Info($"Installed {Path.Combine(output, state.GetGamePath(map))}");
        if (files.Count > state.MapPaths.Count)
            context.Log.Info($"Copied {files.Count} files to {output}");

        return Task.CompletedTask;
    }
}
