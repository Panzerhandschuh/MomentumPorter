namespace MomentumPorter.UI.ViewModels.Shared;

using System.Collections.ObjectModel;
using MomentumPorter.Pipeline;

public sealed record LogLine(LogLevel Level, string Text)
{
    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsError => Level == LogLevel.Error;
}

/// <summary>
/// A job's log. Only touch it from the UI thread; <see cref="Services.JobRunnerService"/> marshals job output here.
/// </summary>
public sealed class LogViewModel : ViewModel
{
    /// <summary>Oldest lines are dropped past this, bspPack can be chatty with -V.</summary>
    public const int MaxLines = 20000;

    public ObservableCollection<LogLine> Lines { get; } = [];

    public void Add(LogLevel level, string text)
    {
        // Tools sometimes print several lines at once
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            Lines.Add(new LogLine(level, line));

        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);
    }

    public void Clear() => Lines.Clear();

    public override string ToString() => string.Join(Environment.NewLine, Lines.Select(x => x.Text));
}

/// <summary>
/// One line of a <see cref="Porting.PortReport"/>.
/// </summary>
public sealed record ReportLine(string Text, bool IsWarning)
{
    public static IReadOnlyList<ReportLine> FromReport(Porting.PortReport report)
    {
        using var writer = new StringWriter();
        report.Print(writer);
        return writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.TrimEnd('\r'))
            .Where(x => x.Length > 0)
            .Select(x => new ReportLine(x.Trim(), x.Contains(" x warning: ", StringComparison.Ordinal)))
            .ToList();
    }
}
