namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
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
        context.Set(new MapPortState(temp.Combine("in.bsp"), bsp, new MapPortOptions()) { Profile = GameProfiles.Find("kzmod") });
        context.Set(new BspPackTool(missing));

        var ex = await Assert.ThrowsAsync<PortStepException>(() => new BspPackStep().RunAsync(context));
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void FactoryAppendsPackAndInstallSteps()
    {
        (PortJob job, _) = PortJobFactory.CreateSourceMapJob("in.bsp", "out.bsp", new MapPortOptions { Pack = true, Install = true });
        Assert.Equal(["Load map", "Convert entities", "Check entities", "Write map", "Pack assets", "Install into Momentum"], job.Steps.Select(x => x.Name));

        (job, _) = PortJobFactory.CreateSourceMapJob("in.bsp", "out.bsp", new MapPortOptions { Pack = false, Install = false });
        Assert.Equal(4, job.Steps.Count);
    }

    /// <summary>
    /// Ports and packs a real KZ map with the bundled bspPack, set up by the porter, when KZ and Momentum are installed.
    /// Packs in the job's work folder, then optionally installs it into a fake Momentum folder that borrows the real
    /// one's bspzip. The first run extracts KZ's VPKs into the porter's cache, like the app does.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortsAndPacksKzDance(bool install)
    {
        string? input = KzMaps.Find("kz_dance");
        string bspPack = BspPackTool.BundledPath;
        string? realMomentum = new SteamLocator().FindMomentum()?.InstallDirectory;
        if (input is null || !File.Exists(bspPack) || realMomentum is null)
            return;

        using var temp = new TempFolder();
        string momentum = temp.CreateMomentumInstall();
        Directory.CreateDirectory(Path.Combine(momentum, "bin"));
        BspPackGames.CreateDirectoryLink(Path.Combine(momentum, "bin", "win64"), Path.Combine(realMomentum, "bin", "win64"));
        string work = temp.Combine("work");
        Directory.CreateDirectory(work);
        string output = Path.Combine(work, "kz_dance.bsp");

        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, output, new MapPortOptions { Pack = true, Install = install });
        var log = new List<string>();
        var context = new PortJobContext { Log = new DelegateLog((_, message) => { lock (log) log.Add(message); }), WorkDirectory = work };
        context.Set(state);
        context.Set(new PorterSettings { MomentumDirectory = momentum });

        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, $"{result.FailedStep}: {result.Error}\n{string.Join('\n', log)}");
        Assert.Contains(log, x => x.StartsWith("bspPack packs assets from Kreedz Climbing "));
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
