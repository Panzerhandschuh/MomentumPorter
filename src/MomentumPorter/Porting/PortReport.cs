namespace MomentumPorter.Porting;

/// <summary>
/// Counts what each conversion did and what it couldn't convert.
/// </summary>
internal sealed class PortReport
{
    private readonly Dictionary<string, int> _changes = [];
    private readonly Dictionary<string, int> _warnings = [];

    public void Change(string message, int count = 1)
    {
        _changes[message] = _changes.GetValueOrDefault(message) + count;
    }

    public void Warn(string message)
    {
        _warnings[message] = _warnings.GetValueOrDefault(message) + 1;
    }

    public bool HasWarnings => _warnings.Count > 0;

    public void Print(TextWriter writer)
    {
        foreach ((string message, int count) in _changes)
            writer.WriteLine($"  {count,5} x {message}");

        foreach ((string message, int count) in _warnings)
            writer.WriteLine($"  {count,5} x warning: {message}");
    }
}
