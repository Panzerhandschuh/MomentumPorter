namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Detection;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using Xunit;

/// <summary>
/// Ports real KZ Mod maps when Kreedz Climbing is installed through Steam, and does nothing otherwise, since the
/// maps are too big and not ours to ship as fixtures.
/// </summary>
public class KzModPortTests
{
    private static string? FindKzMap(string name)
    {
        GameLocation? kz = new SteamLocator().Find(KnownGame.KreedzClimbing);
        if (kz?.ContentDirectory is null)
            return null;

        string path = Path.Combine(kz.ContentDirectory, "maps", name + ".bsp");
        return File.Exists(path) ? path : null;
    }

    [Theory]
    [InlineData("kz_bhop_arctic", "trigger_bhop_teleport -> trigger_momentum_bhop")]
    [InlineData("kz_dance", "trigger_teleport set to reset velocity")]
    public async Task PortsKzModMaps(string map, string expectedChange)
    {
        string? input = FindKzMap(map);
        if (input is null)
            return;

        string output = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", $"{map}_{Guid.NewGuid():N}.bsp");
        string work = PortJob.CreateWorkDirectory("test");
        try
        {
            Assert.Equal(InputKind.SourceBsp, InputDetector.Detect(input));

            (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(input, output, new MapPortOptions { Pack = false, Install = false });
            var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = work };
            context.Set(state);

            PortJobResult result = await job.RunAsync(context);

            Assert.True(result.Succeeded, result.Error);
            Assert.Equal("kzmod", state.Profile?.Name);
            Assert.True(File.Exists(output));

            using var writer = new StringWriter();
            result.Report.Print(writer);
            Assert.Contains(expectedChange, writer.ToString());

            // The output has to load again and keep the converted entities
            var ported = new BSP(new FileInfo(output));
            Assert.Contains(ported.Entities, x => x.ClassName is "trigger_momentum_bhop" or "trigger_teleport");
        }
        finally
        {
            File.Delete(output);
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void DetectsKzModFromEntities()
    {
        string? input = FindKzMap("kz_bhop_arctic");
        if (input is null)
            return;

        Assert.Equal("kzmod", SourceGameSniffer.DetectProfile(new BSP(new FileInfo(input))));
    }
}
