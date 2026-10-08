namespace MomentumPorter.BspConvert;

using MomentumPorter.Pipeline;
using MomentumPorter.Porting;

/// <summary>
/// BSPConvert's logger, forwarding to the job's log: messages starting with "Warning:" or "Error:" become warnings
/// and errors (and go in the report), the rest info. Unchanged otherwise, so the log reads like BSPConv.exe's output.
/// </summary>
public sealed class PortLogAdapter(IPortLog log, PortReport? report = null) : BSPConvert.Lib.ILogger
{
    private readonly Lock _lock = new();
    private readonly List<string> _errors = [];

    /// <summary>"Error: ..." messages, which BSPConvert logs when it gives up on the input.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_lock)
                return _errors.ToList();
        }
    }

    public void Log(string message)
    {
        if (message.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
        {
            log.Warn(message);
            lock (_lock)
                report?.Warn(message["Warning:".Length..].Trim());
        }
        else if (message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
        {
            log.Error(message);
            lock (_lock)
                _errors.Add(message["Error:".Length..].Trim());
        }
        else
        {
            log.Info(message);
        }
    }
}
