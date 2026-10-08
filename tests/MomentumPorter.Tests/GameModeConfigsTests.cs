namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Weapons;
using Xunit;

public class GameModeConfigsTests
{
    [Fact]
    public void AppendsThenReplacesItsBlockAndKeepsTheRest()
    {
        const string original = "bind q slot8\nbind e slot5\n";

        string once = GameModeConfigs.SetBlock(original, "test", ["fov_viewmodel 90"]);
        string twice = GameModeConfigs.SetBlock(once, "test", ["fov_viewmodel 110"]);

        Assert.StartsWith("bind q slot8\nbind e slot5\n\n// >>> MomentumPorter: test", once);
        Assert.Equal(once.Replace("90", "110"), twice);
        Assert.Equal(["fov_viewmodel 110"], GameModeConfigs.GetBlock(twice, "test"));
        Assert.Null(GameModeConfigs.GetBlock(original, "test"));
    }

    [Fact]
    public void ReadsTheLastValueACvarIsSetTo()
    {
        const string config = "fov_desired \"90\"\nfov_viewmodel \"54\"\n// fov_desired 70\nfov_desired 110\nviewmodel_offset_z \"-1.5\"\n";

        Assert.Equal(110f, GameModeConfigs.ReadFloat(config, "fov_desired"));
        Assert.Equal("54", GameModeConfigs.ReadCvar(config, "fov_viewmodel"));
        Assert.Equal(-1.5f, GameModeConfigs.ReadFloat(config, "viewmodel_offset_z"));
        Assert.Null(GameModeConfigs.ReadCvar(config, "viewmodel_offset_x"));
    }
}
