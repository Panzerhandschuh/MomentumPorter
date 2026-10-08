namespace MomentumPorter.UI.Services;

using Avalonia.Threading;
using MomentumPorter.Pipeline;
using MomentumPorter.UI.ViewModels.Shared;
using ReactiveUI;

/// <summary>
/// A job ready to run for a queue item.
/// </summary>
/// <param name="WorkDirectory">The job's own temp folder; deleted once <paramref name="Finish"/> has run</param>
/// <param name="Configure">Adds the job's state, settings and prompts to its context</param>
/// <param name="Finish">Runs on a worker thread after the job, success or not, to deliver its output and say how it went</param>
public sealed record PreparedJob(
    PortJob Job,
    string WorkDirectory,
    Action<PortJobContext> Configure,
    Func<PortJobContext, PortJobResult, JobOutcome> Finish);

public sealed record JobOutcome(QueueStatus Status, string? Message, string? OutputPath = null, string? InstalledPath = null);

/// <summary>
/// Thrown while preparing a job to skip the item instead of failing it, e.g. for inputs a later phase will handle.
/// </summary>
public sealed class JobSkippedException(string reason) : Exception(reason);

/// <summary>
/// Runs queued items one at a time. Jobs run on a worker thread; their log, progress and results are marshalled
/// back to the UI thread through the poster given to the constructor.
/// </summary>
public sealed class JobRunnerService : ReactiveObject
{
    private readonly Action<Action> _post;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    private QueueItemViewModel? _current;

    /// <param name="post">Runs an action on the UI thread; defaults to Avalonia's dispatcher. Tests pass <c>a => a()</c>.</param>
    public JobRunnerService(Action<Action>? post = null)
    {
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    public QueueItemViewModel? Current
    {
        get => _current;
        private set => this.RaiseAndSetIfChanged(ref _current, value);
    }

    /// <summary>
    /// Runs every queued item in order (including ones queued while running) until none are left or
    /// <see cref="Cancel"/> is called. Call from the UI thread; returns at once if already running.
    /// </summary>
    public async Task RunAsync<T>(IList<T> queue, Func<T, PreparedJob> prepare) where T : QueueItemViewModel
    {
        if (IsRunning)
            return;

        IsRunning = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            while (!_cancellation.IsCancellationRequested && queue.FirstOrDefault(x => x.Status == QueueStatus.Queued) is { } item)
            {
                Current = item;
                await RunItemAsync(item, prepare, _cancellation.Token);
            }
        }
        finally
        {
            Current = null;
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>Stops the running job; items still queued stay queued.</summary>
    public void Cancel()
    {
        _cancellation?.Cancel();
    }

    private async Task RunItemAsync<T>(T item, Func<T, PreparedJob> prepare, CancellationToken cancellation) where T : QueueItemViewModel
    {
        item.Start();

        PreparedJob prepared;
        try
        {
            prepared = prepare(item);
        }
        catch (JobSkippedException ex)
        {
            item.Finish(QueueStatus.Skipped, ex.Message);
            return;
        }
        catch (Exception ex)
        {
            item.Log.Add(LogLevel.Error, ex.ToString());
            item.Finish(QueueStatus.Failed, ex.Message);
            return;
        }

        var context = new PortJobContext
        {
            Log = new DelegateLog((level, message) => _post(() => item.Log.Add(level, message))),
            WorkDirectory = prepared.WorkDirectory,
            Cancellation = cancellation,
            Progress = new PostingProgress(this, item),
        };

        JobOutcome outcome;
        try
        {
            prepared.Configure(context);
            outcome = await Task.Run(async () =>
            {
                PortJobResult result = await prepared.Job.RunAsync(context).ConfigureAwait(false);
                return prepared.Finish(context, result);
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            item.Log.Add(LogLevel.Error, ex.ToString());
            outcome = new JobOutcome(QueueStatus.Failed, ex.Message);
        }
        finally
        {
            await Task.Run(() => DeleteWorkDirectory(prepared.WorkDirectory, item), CancellationToken.None);
        }

        // Let queued log lines land before the item reports it's finished
        var finished = new TaskCompletionSource();
        _post(() =>
        {
            foreach (ReportLine line in ReportLine.FromReport(context.Report))
                item.Report.Add(line);

            item.OutputPath = outcome.OutputPath;
            item.InstalledPath = outcome.InstalledPath;
            item.Finish(outcome.Status, outcome.Message);
            finished.SetResult();
        });
        await finished.Task;
    }

    private void DeleteWorkDirectory(string directory, QueueItemViewModel item)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _post(() => item.Log.Add(LogLevel.Warning, $"Couldn't delete the work folder {directory}: {ex.Message}"));
        }
    }

    private sealed class PostingProgress(JobRunnerService runner, QueueItemViewModel item) : IProgress<StepProgress>
    {
        public void Report(StepProgress value) => runner._post(() => item.ReportProgress(value));
    }
}
