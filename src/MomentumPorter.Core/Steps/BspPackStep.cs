namespace MomentumPorter.Steps;

using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Tools;

/// <summary>
/// Packs the assets the ported map uses from its source game into it with bspPack (<c>-M -C</c>).
/// The map is packed in the job's work folder; if the output lies elsewhere, it is copied in and back.
/// bspPack's location and pre-pack command come from the <see cref="PorterSettings"/> in the context.
/// </summary>
public sealed class BspPackStep : IPortStep
{
    public string Name => "Pack assets";

    public async Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();

        string? executable = settings.ResolveBspPackPath();
        if (executable is null)
        {
            throw new PortStepException("bspPack wasn't found. Set its location in Settings (looked in: "
                + string.Join(", ", BspPackTool.DefaultCandidates()) + ").");
        }

        var tool = new BspPackTool(executable);
        if (!tool.Exists)
            throw new PortStepException($"bspPack wasn't found at {executable}. Set its location in Settings.");

        if (!File.Exists(state.OutputPath))
            throw new PortStepException($"{state.OutputPath} doesn't exist, there's nothing to pack");

        string output = Path.GetFullPath(state.OutputPath);
        bool inWorkDirectory = IsInside(output, context.WorkDirectory);
        // Keep the map's file name, bspPack goes by it (e.g. for cubemap materials)
        string target = inWorkDirectory ? output : Path.Combine(context.WorkDirectory, "pack", Path.GetFileName(output));
        if (!inWorkDirectory)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(output, target, overwrite: true);
        }

        if (tool.ReadConfiguredGameFolder() is { } gameFolder)
            context.Log.Info($"bspPack packs assets from {gameFolder} (set in {tool.ConfigPath})");

        if (!string.IsNullOrWhiteSpace(settings.PrePackCommand))
            await tool.RunPrePackCommandAsync(settings.PrePackCommand, target, context.WorkDirectory, context.Log, context.Cancellation).ConfigureAwait(false);

        long sizeBefore = new FileInfo(target).Length;
        await tool.PackAsync(target, context.Log, context.Cancellation).ConfigureAwait(false);

        if (!File.Exists(target))
            throw new PortStepException($"bspPack finished but {target} is gone");

        context.Log.Info($"Packed {Path.GetFileName(target)}: {sizeBefore / 1024.0 / 1024.0:0.0} MB -> {new FileInfo(target).Length / 1024.0 / 1024.0:0.0} MB");

        if (!inWorkDirectory)
            File.Copy(target, output, overwrite: true);
    }

    internal static bool IsInside(string path, string directory)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(dir, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
