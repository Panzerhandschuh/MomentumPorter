namespace MomentumPorter.Tests;

using System.Globalization;
using System.Numerics;
using LibBSP;
using MomentumPorter.BspConvert;
using MomentumPorter.Bsp;
using MomentumPorter.Games.GoldSrc;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// A stock Counter-Strike 1.6 map converted without the bhop block fix, when Half-Life is installed through Steam.
/// The tests build bhop blocks, teleports and floors out of its brush models, away from its own geometry.
/// </summary>
public sealed class ConvertedGoldSrcMap : IDisposable
{
    private readonly TempFolder _folder = new();

    public ConvertedGoldSrcMap()
    {
        GameLocation? halfLife = new SteamLocator().Find(KnownGame.CounterStrike16);
        string? input = halfLife?.ContentDirectory is { } content ? Path.Combine(content, "maps", "cs_assault.bsp") : null;
        if (input is null || !File.Exists(input))
            return;

        var options = new BspConvertOptions { OutputDirectory = _folder.Path, FixBhopBlocks = false };
        BspConvertDefaults.Fill(options, new SteamLocator(), input);
        (PortJob job, BspConvertState state) = BspConvertJobs.Create(input, options);

        string work = PortJob.CreateWorkDirectory("test");
        try
        {
            var context = new PortJobContext { Log = new DelegateLog((_, _) => { }), WorkDirectory = work };
            context.Set(state);
            PortJobResult result = job.RunAsync(context).GetAwaiter().GetResult();
            Assert.True(result.Succeeded, result.Error);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        MapPath = Path.Combine(_folder.Path, "maps", "cs_assault.bsp");
    }

    /// <summary>The converted map, or null when Half-Life isn't installed.</summary>
    public string? MapPath { get; }

    public void Dispose() => _folder.Dispose();
}

public sealed class BhopBlockTests(ConvertedGoldSrcMap map) : IClassFixture<ConvertedGoldSrcMap>
{
    // Far outside the map, so its own geometry doesn't get in the way
    private static readonly Vector3 Spot = new(10000, 10000, 1000);

    private sealed record Scene(BSP Bsp, Entity Block, Entity Teleport, int Model, Vector3 Size);

    // A block 1000 units above a teleport of the same shape, both made of one of the map's brush models
    private Scene? CreateScene()
    {
        if (map.MapPath is null)
            return null;

        var bsp = new BSP(new FileInfo(map.MapPath));
        int model = Enumerable.Range(1, bsp.Models.Count - 1)
            .First(x => BrushGeometry.GetBrushes(bsp, x).Count > 0 && bsp.Models[x].Maximums.Z - bsp.Models[x].Minimums.Z > 8);

        Entity block = Add(bsp, "func_door", model, Spot);
        block["spawnflags"] = "1024";
        block["movedir"] = "90 0 0";
        block["targetname"] = "block";

        Entity teleport = Add(bsp, "trigger_teleport", model, Spot - new Vector3(0, 0, 1000));
        teleport["target"] = "destination";
        teleport["VelocityMode"] = "1";

        return new Scene(bsp, block, teleport, model, bsp.Models[model].Maximums - bsp.Models[model].Minimums);
    }

    // Places a brush entity so its model is centered on a point
    private static Entity Add(BSP bsp, string className, int model, Vector3 center)
    {
        Model bounds = bsp.Models[model];
        Vector3 origin = center - (bounds.Minimums + bounds.Maximums) / 2;
        var entity = new Entity(className, bsp.Entities)
        {
            ["model"] = $"*{model}",
            ["origin"] = string.Create(CultureInfo.InvariantCulture, $"{origin.X} {origin.Y} {origin.Z}"),
        };
        bsp.Entities.Add(entity);
        return entity;
    }

    private static int Convert(Scene scene) => BhopBlocks.Convert(new PortContext(scene.Bsp, "cs_assault", gameDirectory: null));

    [Fact]
    public void TurnsBlocksDroppingIntoATeleportIntoFuncBhops()
    {
        if (CreateScene() is not Scene scene)
            return;

        string model = scene.Block["model"];
        string origin = scene.Block["origin"];
        Assert.Equal(1, Convert(scene));

        Entity block = scene.Block;
        Assert.Equal("func_bhop", block.ClassName);
        Assert.Equal(model, block["model"]);
        Assert.Equal(origin, block["origin"]);
        Assert.Equal("destination", block["target"]);
        Assert.Equal("0", block["ProgressTeleport"]);
        Assert.Equal("1", block["UseDestinationAngles"]);
        Assert.Equal("1", block["ResetVelocityOnTeleport"]);
        Assert.Equal("3", block["spawnflags"]);
        Assert.Equal("-1", block["LandingLimit"]);
        Assert.False(block.ContainsKey("movedir"));
        Assert.False(block.ContainsKey("targetname"));
    }

    [Fact]
    public void TakesTheTeleportsAnglesAndVelocity()
    {
        if (CreateScene() is not Scene scene)
            return;

        scene.Teleport["UseLandmarkAngles"] = "0";
        scene.Teleport["VelocityMode"] = "0";
        Assert.Equal(1, Convert(scene));
        Assert.Equal("0", scene.Block["UseDestinationAngles"]);
        Assert.Equal("0", scene.Block["ResetVelocityOnTeleport"]);
    }

    [Fact]
    public void FindsTheTeleportTheBlockDropsInto()
    {
        if (CreateScene() is not Scene scene)
            return;

        // Right under the block, which drops its own height into it, with a floor under the teleport
        scene.Teleport["origin"] = OriginBelow(scene, scene.Size.Z - 1);
        Add(scene.Bsp, "func_wall", scene.Model, Spot - new Vector3(0, 0, 4 * scene.Size.Z));
        Assert.Equal(1, Convert(scene));
    }

    [Fact]
    public void LeavesBlocksAboveSolidGround()
    {
        if (CreateScene() is not Scene scene)
            return;

        // A floor between the block and the teleport
        Add(scene.Bsp, "func_wall", scene.Model, Spot - new Vector3(0, 0, 500));
        Assert.Equal(0, Convert(scene));
        Assert.Equal("func_door", scene.Block.ClassName);
    }

    [Theory]
    [InlineData("spawnflags", "0")] // opened by something else, not by touching it
    [InlineData("spawnflags", "3072")] // locked
    [InlineData("movedir", "0 90 0")] // slides sideways
    [InlineData("movedir", "-90 0 0")] // rises
    [InlineData("lip", "100000")] // doesn't move
    public void LeavesDoorsThatDontDropWhenTouched(string key, string value)
    {
        if (CreateScene() is not Scene scene)
            return;

        scene.Block[key] = value;
        Assert.Equal(0, Convert(scene));
        Assert.Equal("func_door", scene.Block.ClassName);
    }

    [Fact]
    public void ConvertsTouchedButtonsButNotStillOnes()
    {
        if (CreateScene() is not Scene scene)
            return;

        scene.Block.ClassName = "func_button";
        scene.Block["spawnflags"] = "257";
        Assert.Equal(0, Convert(scene));

        scene.Block["spawnflags"] = "256";
        Assert.Equal(1, Convert(scene));
        Assert.Equal("func_bhop", scene.Block.ClassName);
    }

    [Fact]
    public void LeavesBlocksWithoutATeleportBelow()
    {
        if (CreateScene() is not Scene scene)
            return;

        scene.Teleport["origin"] = OriginBelow(scene, -2000);
        Assert.Equal(0, Convert(scene));
    }

    [Fact]
    public void MapJobsFixGoldSrcBhopBlocksUnlessTurnedOff()
    {
        string input = Path.Combine(Path.GetTempPath(), "map.bsp");
        Assert.Equal(["Convert map", "Fix bhop blocks", "Fix entities", "Copy to game folder"], BspConvertJobs.Create(input, new BspConvertOptions()).Job.Steps.Select(x => x.Name));
        Assert.Equal(["Convert map", "Fix entities", "Copy to game folder"], BspConvertJobs.Create(input, new BspConvertOptions { FixBhopBlocks = false }).Job.Steps.Select(x => x.Name));
    }

    // The teleport's origin that puts its top this far below the block's bottom
    private static string OriginBelow(Scene scene, float distance)
    {
        Model bounds = scene.Bsp.Models[scene.Model];
        Vector3 blockOrigin = scene.Block.Origin;
        float blockBottom = blockOrigin.Z + bounds.Minimums.Z;
        float z = blockBottom - distance - bounds.Maximums.Z;
        return string.Create(CultureInfo.InvariantCulture, $"{blockOrigin.X} {blockOrigin.Y} {z}");
    }
}
