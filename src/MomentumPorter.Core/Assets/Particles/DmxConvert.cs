namespace MomentumPorter.Assets.Particles;

using MomentumPorter.Pipeline;
using MomentumPorter.Tools;

/// <summary>
/// Momentum's dmxconvert.exe: turns a keyvalues2 (text) .pcf into the binary encoding the game loads particles from.
/// </summary>
public sealed class DmxConvert(string executable, string gameDirectory, IPortLog log)
{
    public async Task ToBinaryAsync(string input, string output, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        ExternalToolResult result = await ExternalTool.RunAsync(
            executable,
            ["-i", Path.GetFullPath(input), "-o", Path.GetFullPath(output), "-oe", "binary", "-game", gameDirectory],
            log,
            cancellation: cancellation).ConfigureAwait(false);
        if (!result.Succeeded || !File.Exists(output))
            throw new PortStepException($"dmxconvert couldn't convert {Path.GetFileName(input)} (exit code {result.ExitCode}): {result.StandardError.Trim()}{result.StandardOutput.Trim()}");
    }
}
