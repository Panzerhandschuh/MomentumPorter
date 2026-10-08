namespace MomentumPorter.Tests;

using MomentumPorter.Pipeline;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Shared;
using Xunit;

public class JobRunnerTests
{
    private sealed class Item(string title) : QueueItemViewModel
    {
        public override string Title { get; } = title;
    }

    private sealed class Step(string name, Func<PortJobContext, Task> run, double weight = 1) : IPortStep
    {
        public string Name { get; } = name;

        public double Weight { get; } = weight;

        public Task RunAsync(PortJobContext context) => run(context);
    }

    private sealed class ListProgress : IProgress<StepProgress>
    {
        public List<StepProgress> Reports { get; } = [];

        public void Report(StepProgress value) => Reports.Add(value);
    }

    [Fact]
    public async Task StepsReportTheirProgressAsPartOfTheJobsByWeight()
    {
        using var temp = new TempFolder();
        var progress = new ListProgress();
        var job = new PortJob("test",
        [
            new Step("Convert", context =>
            {
                context.ReportStepProgress(0.5, "Converting textures 1/2");
                return Task.CompletedTask;
            }, weight: 3),
            new Step("Copy", context =>
            {
                context.ReportStepProgress(0.5);
                return Task.CompletedTask;
            }),
        ]);

        PortJobResult result = await job.RunAsync(new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path, Progress = progress });

        Assert.True(result.Succeeded);
        Assert.Equal([0, 0.375, 0.75, 0.875, 1], progress.Reports.Select(x => x.JobFraction!.Value));
        Assert.Equal("Converting textures 1/2", progress.Reports[1].Message);
        Assert.Equal(("Copy", 1, 0.5), (progress.Reports[3].Step, progress.Reports[3].StepIndex, progress.Reports[3].Fraction!.Value));
    }

    // Everything runs inline: there's no UI thread in tests
    private static readonly Action<Action> Inline = action => action();

    private static PreparedJob Job(TempFolder temp, params IPortStep[] steps)
    {
        string work = temp.Combine(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return new PreparedJob(
            new PortJob("test", steps),
            work,
            _ => { },
            (_, result) => result.Succeeded
                ? new JobOutcome(QueueStatus.Done, "ok", OutputPath: "out.bsp")
                : new JobOutcome(result.Exception is OperationCanceledException ? QueueStatus.Skipped : QueueStatus.Failed, result.Error));
    }

    [Fact]
    public async Task RunsQueuedItemsInOrderAndRecordsTheOutcome()
    {
        using var temp = new TempFolder();
        var runner = new JobRunnerService(Inline);
        var queue = new List<Item> { new("ok"), new("broken"), new("skipped"), new("done before") };
        queue[3].Start();
        queue[3].Finish(QueueStatus.Done, "earlier");
        var order = new List<string>();
        var workDirectories = new List<string>();

        await runner.RunAsync(queue, item =>
        {
            order.Add(item.Title);
            Assert.Equal(QueueStatus.Running, item.Status);
            PreparedJob job = item.Title switch
            {
                "ok" => Job(temp, new Step("Convert", context =>
                {
                    context.Log.Info("converting");
                    context.Log.Warn("careful");
                    context.Report.Change("entity converted", 2);
                    context.Report.Warn("something lost");
                    return Task.CompletedTask;
                })),
                "broken" => Job(temp, new Step("Pack", _ => throw new PortStepException("bspPack is missing"))),
                _ => throw new JobSkippedException("not yet"),
            };
            workDirectories.Add(job.WorkDirectory);
            return job;
        });

        Assert.Equal(["ok", "broken", "skipped"], order);
        Assert.False(runner.IsRunning);

        Assert.Equal(QueueStatus.Done, queue[0].Status);
        Assert.Equal("ok", queue[0].StatusMessage);
        Assert.Equal("out.bsp", queue[0].OutputPath);
        Assert.Equal(100, queue[0].Progress);
        Assert.Contains(queue[0].Log.Lines, x => x is { Text: "converting", Level: LogLevel.Info });
        Assert.Contains(queue[0].Log.Lines, x => x is { Text: "careful", IsWarning: true });
        Assert.Contains(queue[0].Report, x => x.Text == "2 x entity converted" && !x.IsWarning);
        Assert.Contains(queue[0].Report, x => x.Text == "1 x warning: something lost" && x.IsWarning);

        Assert.Equal(QueueStatus.Failed, queue[1].Status);
        Assert.Equal("bspPack is missing", queue[1].StatusMessage);
        Assert.Contains(queue[1].Log.Lines, x => x is { Text: "bspPack is missing", IsError: true });

        Assert.Equal(QueueStatus.Skipped, queue[2].Status);
        Assert.Equal("not yet", queue[2].StatusMessage);

        Assert.Equal("earlier", queue[3].StatusMessage);

        // Work folders are cleaned up whatever happened
        Assert.All(workDirectories, x => Assert.False(Directory.Exists(x)));
    }

    [Fact]
    public async Task CancelStopsTheRunningJobAndLeavesTheRestQueued()
    {
        using var temp = new TempFolder();
        var runner = new JobRunnerService(Inline);
        var queue = new List<Item> { new("first"), new("second") };
        var started = new TaskCompletionSource();

        Task run = runner.RunAsync(queue, _ => Job(temp, new Step("Wait", async context =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, context.Cancellation);
        })));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(runner.IsRunning);
        Assert.Same(queue[0], runner.Current);
        runner.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(QueueStatus.Skipped, queue[0].Status);
        Assert.Equal("Cancelled", queue[0].StatusMessage);
        Assert.Equal(QueueStatus.Queued, queue[1].Status);
        Assert.False(runner.IsRunning);
        Assert.Null(runner.Current);
    }

    [Fact]
    public void QueueItemsMoveThroughTheirStates()
    {
        var item = new Item("map");
        Assert.Equal(QueueStatus.Queued, item.Status);
        Assert.False(item.IsFinished);

        item.Start();
        Assert.True(item.IsRunning);
        Assert.Throws<InvalidOperationException>(item.Start);
        Assert.Throws<InvalidOperationException>(item.Requeue);

        item.ReportProgress(new StepProgress("Pack assets", 3, 5, null));
        Assert.Equal(60, item.Progress);
        Assert.Equal("4/5 Pack assets", item.StepText);

        // A step reporting its own progress, in a job whose steps are weighted
        item.ReportProgress(new StepProgress("Convert map", 0, 3, 0.5, "Converting textures 12/67", JobFraction: 0.45));
        Assert.Equal(45, item.Progress, 3);
        Assert.Equal("1/3 Convert map: Converting textures 12/67", item.StepText);

        item.Report.Add(new ReportLine("1 x x", false));
        Assert.True(item.HasReport);
        item.Finish(QueueStatus.Failed, "broke");
        Assert.True(item.IsFinished);
        Assert.Null(item.StepText);

        item.Requeue();
        Assert.Equal(QueueStatus.Queued, item.Status);
        Assert.Null(item.StatusMessage);

        // Starting again clears the last run's log and report
        item.Log.Add(LogLevel.Info, "old");
        item.Start();
        Assert.Empty(item.Log.Lines);
        Assert.False(item.HasReport);
    }

    [Fact]
    public void LogSplitsLinesAndKeepsALimit()
    {
        var log = new LogViewModel();
        log.Add(LogLevel.Info, "a\r\nb");
        Assert.Equal(["a", "b"], log.Lines.Select(x => x.Text));

        for (int i = 0; i < LogViewModel.MaxLines + 5; i++)
            log.Add(LogLevel.Info, "line");

        Assert.Equal(LogViewModel.MaxLines, log.Lines.Count);
    }
}
