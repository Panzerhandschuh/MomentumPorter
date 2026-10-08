namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Sounds;
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

        PlayerSoundPackOptions options = (PlayerSoundPackOptions)page.OptionsForm.Options;
        Assert.Equal("sarge", options.Character);
        Assert.NotNull(page.OptionsForm.Find(nameof(PlayerSoundPackOptions.Character)));
    }

    [Fact]
    public void PreparesASoundPackJob()
    {
        PlayerSoundsPageViewModel page = CreatePage();
        page.SelectedCharacter = "ranger";

        var build = new SoundPackBuildViewModel("ranger", "q3_sounds_ranger");
        PreparedJob prepared = page.Prepare(build);

        Assert.Contains(prepared.Job.Steps, x => x is FindPlayerSoundInputsStep);
        Assert.Contains(prepared.Job.Steps, x => x is BuildPlayerSoundPackStep);
        Assert.True(Directory.Exists(prepared.WorkDirectory));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
    }
}
