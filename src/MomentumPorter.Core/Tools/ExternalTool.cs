namespace MomentumPorter.Tools;

using System.Diagnostics;
using System.Text;
using MomentumPorter.Pipeline;

public sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Runs a command line tool (bspPack, studiomdl, vpk...) with its output streamed to the job log as it happens.
/// </summary>
public static class ExternalTool
{
    public static async Task<ExternalToolResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        IPortLog log,
        string? workingDirectory = null,
        CancellationToken cancellation = default,
        Action<string>? onOutputLine = null)
    {
        if (!File.Exists(executable))
            throw new PortStepException($"{executable} doesn't exist");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        string name = Path.GetFileName(executable);
        log.Info($"Running {name} {string.Join(' ', startInfo.ArgumentList.Select(Quote))}");

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            stdout.AppendLine(e.Data);
            log.Info($"[{name}] {e.Data}");
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;

            stderr.AppendLine(e.Data);
            log.Warn($"[{name}] {e.Data}");
            onOutputLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new PortStepException($"Couldn't start {executable}: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited
            }

            throw;
        }

        // WaitForExitAsync can return before the redirected streams are drained
        process.WaitForExit();

        return new ExternalToolResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string Quote(string argument)
    {
        return argument.Contains(' ') ? $"\"{argument}\"" : argument;
    }
}
