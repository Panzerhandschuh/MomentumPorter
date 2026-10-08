namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Weapons;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Pages;
using Xunit;

public class WeaponsPageTests
{
    private static WeaponsPageViewModel CreatePage(string? outputDirectory = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        SettingsService settings = SettingsService.Load(Path.Combine(root, "settings.json"));
        settings.Settings.OutputDirectory = outputDirectory ?? Path.Combine(root, "out");
        return new WeaponsPageViewModel(settings, new JobRunnerService(a => a()), a => a());
    }

    [Fact]
    public void ListsTheCatalogAndFollowsTheSelection()
    {
        WeaponsPageViewModel page = CreatePage();

        Assert.NotEmpty(page.Games);
        Assert.NotNull(page.SelectedGame);
        Assert.NotEmpty(page.Weapons);
        Assert.NotNull(page.SelectedWeapon);
        Assert.NotEmpty(page.Slots);
        Assert.Equal(page.SelectedWeapon!.Slots[0], page.SelectedSlot);

        CatalogGame tf2 = page.Games.First(x => x.Id == "tf2");
        page.SelectedGame = tf2;
        Assert.All(page.Weapons, x => Assert.Contains(x, tf2.Weapons));

        var options = (WeaponPackOptions)page.OptionsForm.Options;
        Assert.Equal("tf2", options.Game);
        Assert.Equal(page.SelectedWeapon!.Id, options.Weapon);
        Assert.Equal(page.SelectedSlot, options.Slot);

        // Every mode by default; ticked modes become the option's list
        Assert.True(page.AllModes);
        Assert.Null(page.SelectedModes);
        Assert.Equal(GameModes.All.Count, page.ModeChoices.Count);
        page.AllModes = false;
        page.ModeChoices.First(x => x.Mode.Id == "kz16").IsSelected = true;
        page.ModeChoices.First(x => x.Mode.Id == "kzt").IsSelected = true;
        Assert.Equal("kzt,kz16", page.SelectedModes);
    }

    [Fact]
    public void SwitchingGamesKeepsTheGameTheJobBuilds()
    {
        WeaponsPageViewModel page = CreatePage();

        // The form must not offer map profiles for the weapon's game, which turned "q3" back into "css"
        Assert.Null(page.OptionsForm.Find(nameof(WeaponPackOptions.Game)));
        Assert.Null(page.OptionsForm.Find(nameof(WeaponPackOptions.Weapon)));
        Assert.Null(page.OptionsForm.Find(nameof(WeaponPackOptions.Slot)));

        foreach (string game in new[] { "q3", "tf2", "cs16", "css" })
        {
            page.SelectedGame = page.Games.First(x => x.Id == game);
            page.OptionsForm.Reload();

            var options = (WeaponPackOptions)page.OptionsForm.Options;
            Assert.Equal(game, options.Game);
            Assert.Equal(page.SelectedWeapon!.Id, options.Weapon);
        }
    }

    [Fact]
    public void MapOptionsStillOfferGameProfiles()
    {
        var form = new MomentumPorter.UI.ViewModels.Shared.OptionsFormViewModel(new MomentumPorter.Options.MapPortOptions());
        Assert.IsType<MomentumPorter.UI.ViewModels.Shared.ChoiceOptionViewModel>(form.Find(nameof(MomentumPorter.Options.MapPortOptions.Game)));
    }

    [Fact]
    public void PreparesAWeaponPackJob()
    {
        WeaponsPageViewModel page = CreatePage();
        page.SelectedGame = page.Games.First(x => x.Id == "css");
        page.SelectedWeapon = page.Weapons.First(x => x.Id == "ak47");

        PreparedJob prepared = page.Prepare(new WeaponPackBuildViewModel("test"));

        Assert.Contains(prepared.Job.Steps, x => x is LoadWeaponStep);
        Assert.Contains(prepared.Job.Steps, x => x is WriteWeaponPackStep);
        Assert.True(Directory.Exists(prepared.WorkDirectory));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
    }
}
