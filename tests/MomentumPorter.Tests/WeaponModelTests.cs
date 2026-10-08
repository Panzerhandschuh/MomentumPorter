namespace MomentumPorter.Tests;

using System.Numerics;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using Xunit;

public class WeaponModelTests
{
    [Fact]
    public void AssignsActivitiesByName()
    {
        // CS 1.6 M4A1: the unsilenced sequences are the ones drawn
        string[] m4a1 = ["idle", "shoot1", "shoot2", "shoot3", "reload", "draw", "add_silencer", "idle_unsil", "shoot1_unsil", "shoot2_unsil", "shoot3_unsil", "reload_unsil", "draw_unsil", "detach_silencer"];
        Assert.Equal(
            [null, null, null, null, null, null, null, "ACT_VM_IDLE", "ACT_VM_PRIMARYATTACK", "ACT_VM_PRIMARYATTACK", "ACT_VM_PRIMARYATTACK", "ACT_VM_RELOAD", "ACT_VM_DRAW", null],
            GoldSrcWeaponConverter.AssignActivities(m4a1, ViewModelKind.Gun));

        string[] knife = ["idle", "slash1", "slash2", "draw", "stab", "stab_miss", "midslash1", "midslash2"];
        Assert.Equal(
            ["ACT_VM_IDLE", "ACT_VM_HITCENTER", "ACT_VM_MISSCENTER", "ACT_VM_DRAW", "ACT_VM_SWINGHIT", "ACT_VM_SWINGMISS", "ACT_VM_HITCENTER", "ACT_VM_MISSCENTER"],
            GoldSrcWeaponConverter.AssignActivities(knife, ViewModelKind.Melee));

        string[] shotgun = ["idle", "shoot1", "shoot2", "insert", "after_reload", "start_reload", "draw"];
        Assert.Equal(
            ["ACT_VM_IDLE", "ACT_VM_PRIMARYATTACK", "ACT_VM_PRIMARYATTACK", "ACT_VM_RELOAD", null, null, "ACT_VM_DRAW"],
            GoldSrcWeaponConverter.AssignActivities(shotgun, ViewModelKind.Gun));

        Assert.Equal(["ACT_VM_IDLE", "ACT_VM_PULLPIN", "ACT_VM_THROW", "ACT_VM_DRAW"], GoldSrcWeaponConverter.AssignActivities(["idle", "pullpin", "throw", "deploy"], ViewModelKind.Grenade));

        // Something has to idle
        Assert.Equal(["ACT_VM_IDLE", null], GoldSrcWeaponConverter.AssignActivities(["spin", "wobble"], ViewModelKind.Gun));
    }

    [Fact]
    public void ReadsCs16SoundEvents()
    {
        using GameContent? cs16 = WeaponTestGames.Open("cs16");
        if (cs16 is null)
            return;

        byte[] data = cs16.Read("models/v_ak47.mdl")!;
        int sequences = BitConverter.ToInt32(data, 164);
        var events = GoldSrcWeaponConverter.ReadEvents(data, sequences);
        Assert.Contains(events.SelectMany(x => x), x => x.Event == 5004 && x.Options == "weapons/ak47_clipout.wav");
    }

    [Fact]
    public void TagPoseMatchesTagAxes()
    {
        // 90 degrees about z, then 30 about y, then -20 about x, as a tag's axes
        Matrix4x4 rotation = Matrix4x4.CreateRotationX(-20 * MathF.PI / 180) * Matrix4x4.CreateRotationY(30 * MathF.PI / 180) * Matrix4x4.CreateRotationZ(90 * MathF.PI / 180);
        var tag = new Md3Model.Tag("tag_weapon", new Vector3(1, 2, 3), [
            Vector3.TransformNormal(Vector3.UnitX, rotation),
            Vector3.TransformNormal(Vector3.UnitY, rotation),
            Vector3.TransformNormal(Vector3.UnitZ, rotation)]);

        SmdBonePose pose = Quake3WeaponConverter.ToPose(tag);
        Assert.Equal(new Vector3(1, 2, 3), pose.Position);
        Assert.Equal(-20 * MathF.PI / 180, pose.Rotation.X, 4);
        Assert.Equal(30 * MathF.PI / 180, pose.Rotation.Y, 4);
        Assert.Equal(90 * MathF.PI / 180, pose.Rotation.Z, 4);
    }

    [Fact]
    public void ReadsQuake3Railgun()
    {
        using GameContent? q3 = WeaponTestGames.Open("q3");
        if (q3 is null)
            return;

        Md3Model railgun = Md3Model.Read(q3.Read("models/weapons2/railgun/railgun.md3")!);
        Assert.NotEmpty(railgun.Surfaces);
        Assert.All(railgun.Surfaces, x => Assert.Equal(x.Triangles.Length % 3, 0));

        Md3Model hand = Md3Model.Read(q3.Read("models/weapons2/railgun/railgun_hand.md3")!);
        Assert.True(hand.FrameCount > 6);
        Assert.NotNull(hand.FindTag("tag_weapon"));
    }
}
