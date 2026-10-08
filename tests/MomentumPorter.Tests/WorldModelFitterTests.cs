namespace MomentumPorter.Tests;

using System.Numerics;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using Xunit;

/// <summary>
/// Fits real world models onto Momentum's player's hand. Does nothing when CS:GO or Momentum isn't installed.
/// </summary>
public class WorldModelFitterTests
{
    private static SourceModelMesh? ReadMomentum(string path)
    {
        if (WeaponTestGames.Momentum is not { } momentum)
            return null;

        string basePath = Path.Combine(momentum.GameDirectory, Path.ChangeExtension(path, null).Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(basePath + ".mdl")
            ? SourceModelMesh.Read(File.ReadAllBytes(basePath + ".mdl"), File.ReadAllBytes(basePath + ".vvd"), File.ReadAllBytes(basePath + ".dx90.vtx"))
            : null;
    }

    private static SourceModelMesh? ReadGame(GameContent content, string path)
    {
        string basePath = Path.ChangeExtension(path, null);
        return content.Read(path) is { } mdl && content.Read(basePath + ".vvd") is { } vvd && content.Read(basePath + ".dx90.vtx") is { } vtx
            ? SourceModelMesh.Read(mdl, vvd, vtx)
            : null;
    }

    [Fact]
    public void MomentumWorldModelsAreLevelInModelSpace()
    {
        if (ReadMomentum("models/weapons/mom_pistol/w_mom_pistol.mdl") is not { } pistol)
            return;

        GunFrame frame = WorldModelFitter.MeasureMomentum(pistol)!;

        // The pistol's weapon_bone.R rests turned -1.283 radians about x, so its barrel is 16 degrees off -z
        Assert.True(Vector3.Distance(WorldModelFitter.DefaultTarget.Forward, frame.Forward) < 0.01f, frame.ToString());
        Assert.True(Vector3.Distance(WorldModelFitter.DefaultTarget.Up, frame.Up) < 0.01f, frame.ToString());
        Assert.NotNull(frame.Trigger);
    }

    [Fact]
    public void FitsCsgoDeagleLikeMomentumsPistol()
    {
        if (ReadMomentum("models/weapons/mom_pistol/w_mom_pistol.mdl") is not { } pistol || WeaponTestGames.Open("csgo") is not { } content)
            return;

        using (content)
        {
            SourceModelMesh deagle = ReadGame(content, "models/weapons/w_pist_deagle.mdl")!;
            GunFrame target = WorldModelFitter.MeasureMomentum(pistol)!;

            (IReadOnlyList<SmdTriangle> triangles, WorldModelFit fit) = WorldModelFitter.Fit(deagle, null, target);

            Assert.Equal("weapon_hand_R", fit.GripBone);
            Assert.True(fit.TriggersAligned);
            var points = triangles.SelectMany(x => new[] { x.A, x.B, x.C }).Select(x => x.Position).ToList();
            Assert.All(triangles.SelectMany(x => new[] { x.A, x.B, x.C }), x => Assert.Equal(0, x.Bone));

            // In the pistol's frame: the barrel reaches well forward of the hand, the grip hangs below it, the slide
            // rises a little above it, and the gun is as wide on both sides
            float front = points.Max(x => Vector3.Dot(x, target.Forward));
            float back = -points.Min(x => Vector3.Dot(x, target.Forward));
            float top = points.Max(x => Vector3.Dot(x, target.Up));
            float bottom = -points.Min(x => Vector3.Dot(x, target.Up));
            Assert.True(front > 8 && front > 2 * back, $"front {front}, back {back}");
            Assert.True(bottom > top, $"top {top}, bottom {bottom}");

            Vector3 left = Vector3.Cross(target.Up, target.Forward);
            float leftmost = points.Max(x => Vector3.Dot(x, left));
            float rightmost = -points.Min(x => Vector3.Dot(x, left));
            Assert.True(MathF.Abs(leftmost - rightmost) < 0.5f, $"left {leftmost}, right {rightmost}");
        }
    }

    [Fact]
    public async Task DeaglePackHasAWorldModelOnMomentumsGripBone()
    {
        if (WeaponTestGames.Momentum is null || WeaponTestGames.Open("csgo") is not { } content)
            return;

        content.Dispose();
        string output = WeaponTestGames.TempDirectory("worldfit");
        var options = new WeaponPackOptions { Game = "csgo", Weapon = "deagle", Slot = "pistol", OutputDirectory = output };
        (PortJob job, WeaponPackState state) = WeaponPackBuilder.CreateJob(options);
        state.Steam = WeaponTestGames.Locator;
        string work = PortJob.CreateWorkDirectory("worldfit_test");
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = work };
        context.Set(state);
        try
        {
            PortJobResult result = await job.RunAsync(context);
            Assert.True(result.Succeeded, result.Error);

            string pack = state.PackDirectory;
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            Assert.Equal("models/porter/csgo/w_pist_deagle.mdl", script.GetModel("world"));

            string model = Path.Combine(pack, "models", "porter", "csgo", "w_pist_deagle");
            var header = StudioModelHeader.Read(model + ".mdl");
            StudioBone bone = Assert.Single(header.Bones);
            Assert.Equal(WorldModelFitter.MomentumGripBone, bone.Name);
            Assert.True(File.Exists(model + ".vvd"));
        }
        finally
        {
            state.Dispose();
            Directory.Delete(work, recursive: true);
            Directory.Delete(output, recursive: true);
        }
    }
}
