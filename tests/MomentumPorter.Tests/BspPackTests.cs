namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steps;
using MomentumPorter.Tools;
using Xunit;

/// <summary>
/// bspPack writes its file lists next to its exe, so tests that run it can't run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BspPackCollection
{
    public const string Name = "bspPack";
}

[Collection(BspPackCollection.Name)]
public class BspPackTests
{
    [Fact]
    public async Task FailsNamingTheExeWhenBspPackIsMissing()
    {
        using var temp = new TempFolder();
        string bsp = temp.Combine("kz_test.bsp");
        File.WriteAllText(bsp, "map");
        string missing = temp.Combine("tools", "bspPack.exe");

        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path };
        context.Set(new MapPortState(temp.Combine("in.bsp"), bsp, new MapPortOptions()));
        context.Set(new PorterSettings { BspPackPath = missing });

        var ex = await Assert.ThrowsAsync<PortStepException>(() => new BspPackStep().RunAsync(context));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public async Task RefusesToRunBspPackWithoutConfigIni()
    {
        using var temp = new TempFolder();
        string bsp = temp.Combine("kz_test.bsp");
        File.WriteAllText(bsp, "map");
        string exe = temp.Combine("tools", BspPackTool.ExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "not run");

        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = temp.Path };
        context.Set(new MapPortState(temp.Combine("in.bsp"), bsp, new MapPortOptions()));
        context.Set(new PorterSettings { BspPackPath = exe });

        var ex = await Assert.ThrowsAsync<PortStepException>(() => new BspPackStep().RunAsync(context));
        Assert.Contains(temp.Combine("tools", "config.ini"), ex.Message);
    }

    [Fact]
    public void ExpandsPrePackPlaceholders()
    {
        var tool = new BspPackTool(@"C:\tools\bspPack\bspPack.exe");
        string command = tool.ExpandPrePackCommand("python \"x.py\" \"{ResourceConfig}\" {bsp} {unknown}", @"C:\work\kz_a.bsp");

        Assert.Equal($"python \"x.py\" \"{Path.Combine(@"C:\tools\bspPack", "ResourceConfig.ini")}\" {Path.GetFullPath(@"C:\work\kz_a.bsp")} {{unknown}}", command);
    }

    [Fact]
    public void ReadsTheGameFolderFromConfigIni()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.Combine("config.ini"), "[GameFolder]\n#comment\nC:\\games\\kz_stub\n[SteamPath]\nC:\\Steam\n");
        Assert.Equal(@"C:\games\kz_stub", new BspPackTool(temp.Combine(BspPackTool.ExecutableName)).ReadConfiguredGameFolder());
    }

    [Fact]
    public void FactoryAppendsPackAndInstallSteps()
    {
        (PortJob job, _) = PortJobFactory.CreateSourceMapJob("in.bsp", "out.bsp", new MapPortOptions { Pack = true, Install = true });
        Assert.Equal(["Load map", "Convert entities", "Write map", "Pack assets", "Install into Momentum"], job.Steps.Select(x => x.Name));

        (job, _) = PortJobFactory.CreateSourceMapJob("in.bsp", "out.bsp", new MapPortOptions { Pack = false, Install = false });
        Assert.Equal(3, job.Steps.Count);
    }

    /// <summary>
    /// Ports and packs a real KZ map with the local bspPack (configured for the KZ stub game) when both are around,
    /// in the job's work folder, then optionally installs it into a fake Momentum folder.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortsAndPacksKzDance(bool install)
    {
        string? input = KzMaps.Find("kz_dance");
        string? bspPack = BspPackTool.FindDefault();
        if (input is null || bspPack is null || !new BspPackTool(bspPack).IsConfigured)
            return;

        using var temp = new TempFolder();
        string momentum = temp.CreateMomentumInstall();
        string work = temp.Combine("work");
        Directory.CreateDirectory(work);
        string output = Path.Combine(work, "kz_dance.bsp");

        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, output, new MapPortOptions { Pack = true, Install = install });
        var log = new List<string>();
        var context = new PortJobContext { Log = new DelegateLog((_, message) => { lock (log) log.Add(message); }), WorkDirectory = work };
        context.Set(state);
        context.Set(new PorterSettings { BspPackPath = bspPack, MomentumDirectory = momentum });

        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, $"{result.FailedStep}: {result.Error}\n{string.Join('\n', log)}");
        Assert.Contains(log, x => x.StartsWith("[bspPack.exe] ") || x.StartsWith("[bspPack] "));
        Assert.Contains(log, x => x.StartsWith("Packed kz_dance.bsp: "));
        Assert.True(File.Exists(output));

        string installed = Path.Combine(momentum, "momentum", "maps", "kz_dance.bsp");
        Assert.Equal(install ? installed : null, context.TryGet<InstallResult>()?.InstalledPath);
        Assert.Equal(install, File.Exists(installed));

        // Packed maps carry a pakfile with the KZ assets the map uses
        var packed = new BSP(new FileInfo(install ? installed : output));
        Assert.True(packed.PakFile?.Data.Length > 0);
    }
}
