namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steps;
using Xunit;

/// <summary>
/// Installs into a fake Momentum folder, never the real one.
/// </summary>
public class InstallStepTests
{
    private sealed class Prompts(bool answer) : IUserPrompts
    {
        public List<string> Questions { get; } = [];

        public Task<bool> ConfirmAsync(string question)
        {
            Questions.Add(question);
            return Task.FromResult(answer);
        }
    }

    private static (PortJobContext Context, MapPortState State, string Maps) Setup(TempFolder temp, MapPortOptions? options = null, IUserPrompts? prompts = null, CancellationToken cancellation = default)
    {
        string install = temp.CreateMomentumInstall();
        string work = temp.Combine("work");
        Directory.CreateDirectory(work);
        string output = Path.Combine(work, "kz_test.bsp");
        File.WriteAllText(output, "new map");

        var state = new MapPortState(temp.Combine("in", "kz_test.bsp"), output, options ?? new MapPortOptions());
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = work, Cancellation = cancellation };
        context.Set(state);
        context.Set(new PorterSettings { MomentumDirectory = install });
        if (prompts is not null)
            context.Set(prompts);

        return (context, state, Path.Combine(install, "momentum", "maps"));
    }

    [Fact]
    public async Task CopiesTheMapIntoMomentumsMapsFolder()
    {
        using var temp = new TempFolder();
        (PortJobContext context, _, string maps) = Setup(temp);

        await new InstallStep().RunAsync(context);

        string installed = Path.Combine(maps, "kz_test.bsp");
        Assert.Equal("new map", File.ReadAllText(installed));
        Assert.Equal(new InstallResult(installed, Skipped: false), context.Get<InstallResult>());
        Assert.Empty(Directory.GetFiles(maps, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task KeepsAnExistingMapWhenNobodyCanBeAsked()
    {
        using var temp = new TempFolder();
        (PortJobContext context, _, string maps) = Setup(temp);
        File.WriteAllText(Path.Combine(maps, "kz_test.bsp"), "old map");

        await new InstallStep().RunAsync(context);

        Assert.Equal("old map", File.ReadAllText(Path.Combine(maps, "kz_test.bsp")));
        Assert.True(context.Get<InstallResult>().Skipped);
    }

    [Theory]
    [InlineData(false, "old map")]
    [InlineData(true, "new map")]
    public async Task AsksBeforeOverwriting(bool answer, string expected)
    {
        using var temp = new TempFolder();
        var prompts = new Prompts(answer);
        (PortJobContext context, _, string maps) = Setup(temp, prompts: prompts);
        File.WriteAllText(Path.Combine(maps, "kz_test.bsp"), "old map");

        await new InstallStep().RunAsync(context);

        Assert.Single(prompts.Questions);
        Assert.Contains("kz_test.bsp already exists", prompts.Questions[0]);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(maps, "kz_test.bsp")));
        Assert.Equal(!answer, context.Get<InstallResult>().Skipped);
    }

    [Fact]
    public async Task OverwritesWithoutAskingWhenTheOptionsSaySo()
    {
        using var temp = new TempFolder();
        var prompts = new Prompts(false);
        (PortJobContext context, _, string maps) = Setup(temp, new MapPortOptions { Overwrite = true }, prompts);
        File.WriteAllText(Path.Combine(maps, "kz_test.bsp"), "old map");

        await new InstallStep().RunAsync(context);

        Assert.Empty(prompts.Questions);
        Assert.Equal("new map", File.ReadAllText(Path.Combine(maps, "kz_test.bsp")));
    }

    [Fact]
    public async Task InstallsExtraOutputsWithTheSameLayout()
    {
        using var temp = new TempFolder();
        (PortJobContext context, MapPortState state, string maps) = Setup(temp);
        var bsp = new BSP("kz_test", MapType.Source20) { Entities = new Entities() };
        state.PortContext = new PortContext(bsp, "kz_test", null);
        state.PortContext.ExtraOutputs.Add(new ExtraOutput("zones/local/kz_test.json", "{}"));
        string zone = WriteBspStep.GetExtraOutputPath(context.WorkDirectory, "zones/local/kz_test.json");
        Directory.CreateDirectory(Path.GetDirectoryName(zone)!);
        File.WriteAllText(zone, "{ \"zones\": 1 }");

        await new InstallStep().RunAsync(context);

        Assert.Equal("{ \"zones\": 1 }", File.ReadAllText(Path.Combine(maps, "zones", "local", "kz_test.json")));
        Assert.True(File.Exists(Path.Combine(maps, "kz_test.bsp")));
    }

    [Fact]
    public async Task CancellingInstallsNothing()
    {
        using var temp = new TempFolder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        (PortJobContext context, _, string maps) = Setup(temp, cancellation: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InstallStep().RunAsync(context));

        Assert.Empty(Directory.GetFiles(maps, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailsClearlyWhenMomentumIsMissing()
    {
        using var temp = new TempFolder();
        (PortJobContext context, _, _) = Setup(temp);
        context.Set(new PorterSettings { MomentumDirectory = temp.Combine("nowhere") });

        var ex = await Assert.ThrowsAsync<PortStepException>(() => new InstallStep().RunAsync(context));
        Assert.Contains("Momentum Mod wasn't found", ex.Message);
    }
}
