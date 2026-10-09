namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Sounds;
using MomentumPorter.Assets.Vpk;
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
        // The page's character list replaces the form's free-text field
        Assert.Null(page.OptionsForm.Find(nameof(PlayerSoundPackOptions.Character)));

        // The sound options come before output and folder settings
        Assert.Equal("Sounds", page.OptionsForm.Groups[0].Name);
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

    [Fact]
    public void SelectingAGameShowsItsOptions()
    {
        PlayerSoundsPageViewModel page = CreatePage();
        PlayerSoundPackOptions options = (PlayerSoundPackOptions)page.OptionsForm.Options;
        IEnumerable<string> Shown() => page.OptionsForm.Groups.SelectMany(x => x.Fields.Concat(x.AdvancedFields)).Select(x => x.Name);

        // Quake 3 first, with its own options
        Assert.Equal("q3", page.SelectedGame.Id);
        Assert.Contains(nameof(PlayerSoundPackOptions.FootstepType), Shown());
        Assert.DoesNotContain(nameof(PlayerSoundPackOptions.GameDirectory), Shown());
        Assert.Null(page.OptionsForm.Find(nameof(PlayerSoundPackOptions.Game)));

        page.SelectedCharacter = "sarge";
        page.SelectedGame = SourceGames.CounterStrikeSource;

        Assert.Equal("css", options.Game);
        Assert.Null(options.Character);
        Assert.Equal("css_sounds", options.ResolvePackName());
        Assert.DoesNotContain(nameof(PlayerSoundPackOptions.FootstepType), Shown());
        Assert.DoesNotContain(nameof(PlayerSoundPackOptions.Quake3Path), Shown());
        Assert.Contains(nameof(PlayerSoundPackOptions.GameDirectory), Shown());
    }
}
