namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Sounds;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Pages;
using Xunit;

public class PlayerSoundsPageTests
{
    private static PlayerSoundsPageViewModel CreatePage()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", $"settings_{Guid.NewGuid():N}.json");
        SettingsService settings = SettingsService.Load(settingsPath);
        return new PlayerSoundsPageViewModel(settings, new JobRunnerService(a => a()), a => a());
    }

    [Fact]
    public void SelectingACharacterUpdatesTheOptions()
    {
        PlayerSoundsPageViewModel page = CreatePage();

        page.SelectedCharacter = "sarge";

        Assert.Equal("sarge", page.Options.Character);
    }

    [Fact]
    public void PreparesAGameAccurateVpkThatReplacesTheLastBuild()
    {
        PlayerSoundsPageViewModel page = CreatePage();
        page.SelectedCharacter = "ranger";

        var build = new SoundPackBuildViewModel("ranger", "q3_sounds_ranger");
        PreparedJob prepared = page.Prepare(build);
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = prepared.WorkDirectory };
        prepared.Configure(context);
        PlayerSoundPackOptions options = context.Get<PlayerSoundPackState>().Options;

        Assert.Contains(prepared.Job.Steps, x => x is FindPlayerSoundInputsStep);
        Assert.Contains(prepared.Job.Steps, x => x is BuildPlayerSoundPackStep);
        Assert.Equal(JumpSoundMode.Game, options.JumpSound);
        Assert.Equal(LandSoundMode.Game, options.LandSound);
        Assert.Equal(FootstepMode.Game, options.Footsteps);
        Assert.Equal(SprintSoundMode.Game, options.SprintSound);
        Assert.Equal(Quake3FootstepType.Character, options.FootstepType);
        Assert.True(options.Pure);
        Assert.True(options.SurfaceFootsteps);
        Assert.True(options.OutputAsVpk);
        Assert.True(options.Overwrite);
        Assert.True(Directory.Exists(prepared.WorkDirectory));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
    }

    [Fact]
    public void SelectingAGameResetsTheCharacterAndPackName()
    {
        PlayerSoundsPageViewModel page = CreatePage();
        Assert.Equal("q3", page.SelectedGame.Id);

        page.SelectedCharacter = "sarge";
        page.SelectedGame = SourceGames.CounterStrikeSource;

        Assert.Equal("css", page.Options.Game);
        Assert.Null(page.Options.Character);
        Assert.Equal("css_sounds", page.Options.ResolvePackName());
        Assert.Null(page.Options.OutputDirectory);
        Assert.Null(page.Options.GameDirectory);
    }
}
