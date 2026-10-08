namespace MomentumPorter.Tests;

using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// Converts a stock Counter-Strike 1.6 map when Half-Life is installed through Steam, and does nothing otherwise.
/// </summary>
public sealed class BspConvertJobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string? FindCsMap(string name)
    {
        GameLocation? halfLife = new SteamLocator().Find(KnownGame.CounterStrike16);
        if (halfLife?.ContentDirectory is null)
            return null;

        string path = Path.Combine(halfLife.ContentDirectory, "maps", name + ".bsp");
        return File.Exists(path) ? path : null;
    }

    private async Task<PortJobResult> Run(PortJob job, BspConvertState state, List<string> log)
    {
        string work = PortJob.CreateWorkDirectory("test");
        try
        {
            var context = new PortJobContext { Log = new DelegateLog((_, message) => log.Add(message)), WorkDirectory = work };
            context.Set(state);
            return await job.RunAsync(context);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertsAGoldSrcMapIntoTheOutputFolder()
    {
        string? input = FindCsMap("cs_assault");
        if (input is null)
            return;

        string output = Path.Combine(_root, "momentum");
        var options = new BspConvertOptions
        {
            OutputDirectory = output,
            Prefix = "gs_",
        };
        BspConvertDefaults.Fill(options, new SteamLocator(), input);

        (PortJob job, BspConvertState state) = BspConvertJobs.Create(input, options);
        var log = new List<string>();
        PortJobResult result = await Run(job, state, log);

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("Converting cs_assault.bsp...", log);

        string map = Assert.Single(state.MapPaths);
        Assert.Equal(Path.Combine("maps", "gs_cs_assault.bsp"), state.GetGamePath(map));
        Assert.Empty(state.ZonePaths);
        Assert.True(File.Exists(Path.Combine(output, "maps", "gs_cs_assault.bsp")));
        Assert.Equal(state.ProducedFiles.Count, state.InstalledFiles.Count);
        Assert.Equal(InputKind.SourceBsp, InputDetector.Detect(Path.Combine(output, "maps", "gs_cs_assault.bsp")));

        // Converting again doesn't replace the map when overwriting is turned off
        options.Overwrite = false;
        (job, state) = BspConvertJobs.Create(input, options);
        result = await Run(job, state, log);
        Assert.False(result.Succeeded);
        Assert.Equal("Copy to game folder", result.FailedStep);
        Assert.Contains("already in", result.Error);
    }

    [Fact]
    public void MapJobsForGoldSrcInputUseBspConvert()
    {
        string? input = FindCsMap("cs_assault");
        if (input is null)
            return;

        (PortJob job, object state) = PortJobFactory.CreateMapJob(input, Path.Combine(_root, "momentum"), new MapPortOptions());
        Assert.IsType<BspConvertState>(state);
        Assert.Equal(["Convert map", "Copy to game folder"], job.Steps.Select(x => x.Name));
    }

    [Fact]
    public async Task FailsCleanlyOnMissingInput()
    {
        (PortJob job, BspConvertState state) = BspConvertJobs.Create(Path.Combine(_root, "missing.pk3"), new BspConvertOptions { OutputDirectory = _root });
        PortJobResult result = await Run(job, state, []);

        Assert.False(result.Succeeded);
        Assert.IsType<PortStepException>(result.Exception);
    }
}
