namespace MomentumPorter.Pipeline;

using System.Diagnostics;
using MomentumPorter.Porting;

/// <summary>
/// Receives the messages a job prints while it runs (tool output, what each step is doing).
/// What a conversion changed and couldn't convert goes in the <see cref="PortReport"/> instead.
/// </summary>
public interface IPortLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

public sealed class ConsoleLog : IPortLog
{
    public static readonly ConsoleLog Instance = new();

    public void Info(string message) => Console.WriteLine(message);

    public void Warn(string message) => Console.Error.WriteLine($"warning: {message}");

    public void Error(string message) => Console.Error.WriteLine(message);
}

public sealed class NullLog : IPortLog
{
    public static readonly NullLog Instance = new();

    public void Info(string message) { }

    public void Warn(string message) { }

    public void Error(string message) { }
}

/// <summary>
/// Forwards each message to a callback, for UIs.
/// </summary>
public sealed class DelegateLog(Action<LogLevel, string> write) : IPortLog
{
    public void Info(string message) => write(LogLevel.Info, message);

    public void Warn(string message) => write(LogLevel.Warning, message);

    public void Error(string message) => write(LogLevel.Error, message);
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <param name="StepIndex">0-based index of the running step</param>
/// <param name="Fraction">How far the step is, 0..1, or null if the step can't tell</param>
/// <param name="Message">What the step is doing, e.g. "Converting textures 12/67"</param>
/// <param name="JobFraction">How far the whole job is, 0..1, with its steps weighted by <see cref="IPortStep.Weight"/></param>
public sealed record StepProgress(string Step, int StepIndex, int StepCount, double? Fraction, string? Message = null, double? JobFraction = null);

/// <summary>
/// A step failed in a way the user can act on (bad input, missing tool). Other exceptions are bugs.
/// </summary>
public sealed class PortStepException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything a job's steps share. Steps leave their results here for later steps.
/// </summary>
public sealed class PortJobContext
{
    private readonly Dictionary<Type, object> _items = [];

    public required IPortLog Log { get; init; }

    /// <summary>
    /// Folder for intermediate files. Only the final step should write anywhere else.
    /// </summary>
    public required string WorkDirectory { get; init; }

    public PortReport Report { get; } = new();

    public CancellationToken Cancellation { get; init; }

    public IProgress<StepProgress>? Progress { get; init; }

    // Set by PortJob while a step runs, see ReportStepProgress
    internal Action<double, string?>? StepProgressSink { get; set; }

    /// <summary>
    /// Reports how far the running step is (0..1) and what it's doing, for steps long enough to show their progress.
    /// </summary>
    public void ReportStepProgress(double fraction, string? message = null)
    {
        StepProgressSink?.Invoke(fraction, message);
    }

    /// <summary>
    /// Stores a result for later steps, replacing any earlier one of the same type.
    /// </summary>
    public void Set<T>(T item) where T : class
    {
        _items[typeof(T)] = item;
    }

    /// <summary>
    /// Stores a result under its runtime type, for callers that only have it as an object.
    /// </summary>
    public void Set(object item)
    {
        _items[item.GetType()] = item;
    }

    public T? TryGet<T>() where T : class
    {
        return _items.TryGetValue(typeof(T), out object? item) ? (T)item : null;
    }

    /// <summary>
    /// Gets a result an earlier step left, or throws if there is none (a bug in the job's step order).
    /// </summary>
    public T Get<T>() where T : class
    {
        return TryGet<T>() ?? throw new InvalidOperationException($"No step has produced a {typeof(T).Name} yet");
    }
}

public interface IPortStep
{
    string Name { get; }

    /// <summary>
    /// How long the step takes compared to the job's other steps, which divides the job's progress between them.
    /// </summary>
    double Weight => 1;

    Task RunAsync(PortJobContext context);
}

public sealed record PortJobResult(bool Succeeded, string? FailedStep, string? Error, Exception? Exception, PortReport Report, TimeSpan Duration)
{
    public static PortJobResult Success(PortReport report, TimeSpan duration) => new(true, null, null, null, report, duration);
}

/// <summary>
/// An ordered list of steps run against one input. The UI and the CLI both just build one of these and run it.
/// </summary>
public sealed class PortJob(string name, IReadOnlyList<IPortStep> steps)
{
    public string Name { get; } = name;

    public IReadOnlyList<IPortStep> Steps { get; } = steps;

    public async Task<PortJobResult> RunAsync(PortJobContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        double totalWeight = Steps.Sum(x => Math.Max(x.Weight, 0));
        double weightBefore = 0;
        for (int i = 0; i < Steps.Count; i++)
        {
            IPortStep step = Steps[i];
            int index = i;
            double start = weightBefore;
            double weight = Math.Max(step.Weight, 0);
            weightBefore += weight;

            double JobFraction(double stepFraction) => totalWeight > 0 ? (start + weight * Math.Clamp(stepFraction, 0, 1)) / totalWeight : 0;
            context.Progress?.Report(new StepProgress(step.Name, index, Steps.Count, null, JobFraction: JobFraction(0)));
            context.StepProgressSink = context.Progress is null
                ? null
                : (fraction, message) => context.Progress.Report(new StepProgress(step.Name, index, Steps.Count, fraction, message, JobFraction(fraction)));
            try
            {
                context.Cancellation.ThrowIfCancellationRequested();
                await step.RunAsync(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                return new PortJobResult(false, step.Name, "Cancelled", ex, context.Report, stopwatch.Elapsed);
            }
            catch (PortStepException ex)
            {
                context.Log.Error(ex.Message);
                return new PortJobResult(false, step.Name, ex.Message, ex, context.Report, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                context.Log.Error($"{step.Name} failed: {ex}");
                return new PortJobResult(false, step.Name, ex.Message, ex, context.Report, stopwatch.Elapsed);
            }
            finally
            {
                context.StepProgressSink = null;
            }
        }

        context.Progress?.Report(new StepProgress("Done", Steps.Count, Steps.Count, 1, JobFraction: 1));
        return PortJobResult.Success(context.Report, stopwatch.Elapsed);
    }

    /// <summary>
    /// Makes a fresh, empty work folder for a job under the user's temp folder.
    /// </summary>
    public static string CreateWorkDirectory(string jobName)
    {
        string safeName = string.Concat(jobName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string dir = Path.Combine(Path.GetTempPath(), "MomentumPorter", $"{safeName}_{Guid.NewGuid():N}"[..Math.Min(safeName.Length + 9, 60)]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
