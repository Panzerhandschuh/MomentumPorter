namespace MomentumPorter.UI.ViewModels.Shared;

using System.Collections.ObjectModel;
using MomentumPorter.Pipeline;
using ReactiveUI;

public enum QueueStatus
{
    Queued,
    Running,
    Done,
    Failed,
    Skipped,
}

/// <summary>
/// An input waiting for, running or finished with its job, with that job's log and report.
/// Pages subclass it with what they know about the input.
/// </summary>
public abstract class QueueItemViewModel : ViewModel
{
    private QueueStatus _status = QueueStatus.Queued;
    private string? _statusMessage;
    private string? _stepText;
    private double _progress;
    private string? _outputPath;
    private string? _installedPath;

    protected QueueItemViewModel()
    {
        Report.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasReport));
    }

    public abstract string Title { get; }

    public QueueStatus Status
    {
        get => _status;
        private set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(IsRunning));
            this.RaisePropertyChanged(nameof(IsFinished));
        }
    }

    /// <summary>Why it failed or was skipped, or where the result went.</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        protected set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>The running step, e.g. "2/5 Convert entities".</summary>
    public string? StepText
    {
        get => _stepText;
        private set => this.RaiseAndSetIfChanged(ref _stepText, value);
    }

    /// <summary>0..100</summary>
    public double Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    /// <summary>The finished file when it was kept outside Momentum (output folder).</summary>
    public string? OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    /// <summary>The finished file inside Momentum's folders.</summary>
    public string? InstalledPath
    {
        get => _installedPath;
        set => this.RaiseAndSetIfChanged(ref _installedPath, value);
    }

    public bool IsRunning => Status == QueueStatus.Running;

    public bool IsFinished => Status is QueueStatus.Done or QueueStatus.Failed or QueueStatus.Skipped;

    public LogViewModel Log { get; } = new();

    public ObservableCollection<ReportLine> Report { get; } = [];

    public bool HasReport => Report.Count > 0;

    public void Start()
    {
        if (Status != QueueStatus.Queued)
            throw new InvalidOperationException($"{Title} is {Status}, not queued");

        Log.Clear();
        Report.Clear();
        OutputPath = null;
        InstalledPath = null;
        StatusMessage = null;
        Progress = 0;
        StepText = "Starting";
        Status = QueueStatus.Running;
    }

    public void ReportProgress(StepProgress progress)
    {
        if (Status != QueueStatus.Running)
            return;

        double fraction = progress.JobFraction
            ?? (progress.StepCount == 0 ? 1 : (progress.StepIndex + (progress.Fraction ?? 0)) / progress.StepCount);
        Progress = Math.Clamp(fraction * 100, 0, 100);
        string step = progress.Message is null ? progress.Step : $"{progress.Step}: {progress.Message}";
        StepText = progress.StepIndex < progress.StepCount
            ? $"{progress.StepIndex + 1}/{progress.StepCount} {step}"
            : step;
    }

    public void Finish(QueueStatus status, string? message)
    {
        if (status is QueueStatus.Queued or QueueStatus.Running)
            throw new ArgumentOutOfRangeException(nameof(status), status, "Not a finished state");

        StatusMessage = message;
        StepText = null;
        Progress = status == QueueStatus.Done ? 100 : Progress;
        Status = status;
    }

    /// <summary>Puts a finished item back in the queue to run again.</summary>
    public void Requeue()
    {
        if (Status == QueueStatus.Running)
            throw new InvalidOperationException($"{Title} is running");

        StatusMessage = null;
        StepText = null;
        Progress = 0;
        Status = QueueStatus.Queued;
    }
}
