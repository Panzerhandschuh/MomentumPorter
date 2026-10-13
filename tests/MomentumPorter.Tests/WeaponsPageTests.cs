namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
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
    public void OffersEveryModeButClimbMomentumWithDefragAsOne()
    {
        WeaponsPageViewModel page = CreatePage();

        Assert.Equal(["Surf", "Bhop", "Bhop (HL1)", "Climb (KZT)", "Climb (1.6)", "Rocket Jump", "Sticky Jump", "Ahop", "Conc", "Defrag"], page.Modes.Select(x => x.Name));
        GameModeGroup defrag = page.Modes.Single(x => x.Name == "Defrag");
        Assert.Equal(["df_cpm", "df_vq3", "df_vtg"], defrag.Modes.Select(x => x.Id));
        Assert.Equal("df_cpm,df_vq3,df_vtg", defrag.ModeList);
        Assert.All(defrag.Slots, x => Assert.StartsWith("df_", x.Id));
        Assert.Equal(["surf"], page.Modes[0].Modes.Select(x => x.Id));
    }

    [Fact]
    public void ListsTheModesWeaponsThenTheGamesWeaponsThatFit()
    {
        WeaponsPageViewModel page = CreatePage();

        // Surf first: the classic weapons, and the catalog's loadout pick for the pistol
        Assert.Equal("Surf", page.SelectedMode.Name);
        Assert.Equal(["pistol", "machinegun", "shotgun", "sniper", "knife", "grenade"], page.Slots.Select(x => x.Slot.Id));
        Assert.Equal("pistol", page.SelectedSlot!.Slot.Id);
        Assert.Equal("css", page.SelectedGame!.Id);
        Assert.All(page.Weapons, x => Assert.Contains("pistol", x.Slots));
        Assert.Equal("glock", page.SelectedWeapon!.Id);

        // Another Momentum weapon: only the weapons that fit it, best fit first
        page.SelectedSlot = page.Slots.Single(x => x.Slot.Id == "sniper");
        Assert.Equal(["awp", "g3sg1", "scout", "sg550"], page.Weapons.Select(x => x.Id));
        Assert.Equal("awp", page.SelectedWeapon!.Id);

        // Another mode keeps the Momentum weapon where it has it, and switches to the game it comes from
        page.SelectedMode = page.Modes.Single(x => x.Name == "Rocket Jump");
        Assert.Equal("tf2", page.SelectedGame!.Id);
        Assert.Equal(["shotgun", "knife", "rocketlauncher"], page.Slots.Select(x => x.Slot.Id));
        Assert.Equal("shotgun", page.SelectedSlot!.Slot.Id);
        Assert.Equal("shotgun", page.SelectedWeapon!.Id);
        page.SelectedSlot = page.Slots.Single(x => x.Slot.Id == "rocketlauncher");
        Assert.Equal(["rocketlauncher"], page.Weapons.Select(x => x.Id));
        page.SelectedGame = page.Games.Single(x => x.Id == "css");
        Assert.Empty(page.Weapons);
        Assert.Equal("No Counter-Strike: Source weapon fits the rocket launcher", page.NoWeaponsMessage);

        page.SelectedMode = page.Modes.Single(x => x.Name == "Defrag");
        Assert.Equal("q3", page.SelectedGame!.Id);
        Assert.DoesNotContain(page.Slots, x => x.Name.StartsWith("Defrag"));
        page.SelectedSlot = page.Slots.Single(x => x.Slot.Id == "df_railgun");
        Assert.Equal("Railgun", page.SelectedSlot.Name);
        Assert.Equal(["railgun"], page.Weapons.Select(x => x.Id));
        Assert.Contains("Railgun as Railgun", page.BuildAllTip);
        Assert.Contains("Gauntlet as Gauntlet", page.BuildAllTip);
    }

    [Theory]
    [InlineData("Rocket Jump", "tf2", "rocketlauncher:rocketlauncher,shotgun:shotgun,knife:shovel")]
    [InlineData("Sticky Jump", "tf2", "stickylauncher:stickybomblauncher,pistol:pistol,knife:bottle")]
    [InlineData("Ahop", "hl2", "pistol:pistol,machinegun:smg1,shotgun:shotgun,sniper:crossbow,knife:crowbar,grenade:grenade")]
    [InlineData("Conc", "hl1", "concgrenade:grenade")]
    [InlineData("Defrag", "q3", "df_bfg:bfg,df_railgun:railgun,df_knife:gauntlet")]
    [InlineData("Surf", "css", "pistol:glock,sniper:awp")]
    [InlineData("Bhop", "css", "pistol:glock,machinegun:ak47")]
    [InlineData("Bhop (HL1)", "hl1", "pistol:glock,knife:crowbar")]
    [InlineData("Climb (KZT)", "csgo", "pistol:glock,knife:knife")]
    [InlineData("Climb (1.6)", "cs16", "pistol:glock18,knife:knife")]
    public void EachModeStartsOnTheGameItComesFrom(string mode, string game, string picks)
    {
        WeaponsPageViewModel page = CreatePage();
        page.SelectedGame = page.Games.Single(x => x.Id == "tf2");
        page.SelectedMode = page.Modes.Single(x => x.Name == "Defrag");

        page.SelectedMode = page.Modes.Single(x => x.Name == mode);

        Assert.Equal(game, page.SelectedGame!.Id);
        foreach (string[] pick in picks.Split(',').Select(x => x.Split(':')))
        {
            page.SelectedSlot = page.Slots.Single(x => x.Slot.Id == pick[0]);
            Assert.Equal(pick[1], page.SelectedWeapon!.Id);
        }
    }

    [Fact]
    public void PreparesAWeaponPackJobForTheSelectedMode()
    {
        WeaponsPageViewModel page = CreatePage();
        page.SelectedMode = page.Modes.Single(x => x.Name == "Climb (1.6)");
        page.SelectedGame = page.Games.First(x => x.Id == "cs16");
        page.SelectedSlot = page.Slots.Single(x => x.Slot.Id == "machinegun");
        page.SelectedWeapon = page.Weapons.First(x => x.Id == "ak47");

        PreparedJob prepared = page.Prepare(new WeaponPackBuildViewModel("test"));
        WeaponPackOptions options = Assert.IsType<WeaponPackState>(PreparedState(prepared)).Options;

        Assert.Equal("cs16", options.Game);
        Assert.Equal("ak47", options.Weapon);
        Assert.Equal("machinegun", options.Slot);
        Assert.Equal("kz16", options.GameModes);
        Assert.EndsWith("custom", options.OutputDirectory);
        Assert.Contains(prepared.Job.Steps, x => x is LoadWeaponStep);
        Assert.Contains(prepared.Job.Steps, x => x is WriteWeaponPackStep);
        Assert.True(Directory.Exists(prepared.WorkDirectory));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
    }

    [Fact]
    public void QueuedBuildsKeepTheWeaponTheyWereQueuedWith()
    {
        WeaponsPageViewModel page = CreatePage();

        var options = new WeaponPackOptions { Game = "q3", Weapon = "railgun", Slot = "df_railgun", GameModes = "df_cpm,df_vq3,df_vtg" };
        page.SelectedGame = page.Games.First(x => x.Id == "css");
        PreparedJob prepared = page.Prepare(new WeaponPackBuildViewModel("test", options));
        Directory.Delete(prepared.WorkDirectory, recursive: true);

        Assert.Equal("q3_railgun", prepared.Job.Name);
        Assert.Equal("railgun", options.Weapon);
        Assert.Equal("", options.OutputDirectory); // Prepare fills in a copy
    }

    [Fact]
    public void TheSettingsStudiomdlOverridesMomentums()
    {
        WeaponsPageViewModel page = CreatePage();
        PreparedJob prepared = page.Prepare(new WeaponPackBuildViewModel("test"));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
        Assert.Null(Assert.IsType<WeaponPackState>(PreparedState(prepared)).Options.StudioMdlPath);

        string root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        SettingsService settings = SettingsService.Load(Path.Combine(root, "settings.json"));
        settings.Settings.StudiomdlPath = @"C:\tools\studiomdl.exe";
        page = new WeaponsPageViewModel(settings, new JobRunnerService(a => a()), a => a());
        prepared = page.Prepare(new WeaponPackBuildViewModel("test"));
        Directory.Delete(prepared.WorkDirectory, recursive: true);
        Assert.Equal(@"C:\tools\studiomdl.exe", Assert.IsType<WeaponPackState>(PreparedState(prepared)).Options.StudioMdlPath);
    }

    [Fact]
    public async Task ResettingAWeaponGivesItBackToMomentumInTheSelectedMode()
    {
        WeaponsPageViewModel page = CreatePage();
        page.SelectedMode = page.Modes.Single(x => x.Name == "Defrag");
        MomentumWeaponViewModel railgun = page.Slots.Single(x => x.Slot.Id == "df_railgun");

        // Any weapon of the list, not only the selected one
        await page.ResetAsync(railgun);

        WeaponPackBuildViewModel build = Assert.Single(page.Builds);
        Assert.Equal("Defrag: Momentum's Railgun restored", build.Title);
        Assert.True(build.Options!.Restore);
        Assert.Equal("df_railgun", build.Options.Slot);
        Assert.Equal("df_cpm,df_vq3,df_vtg", build.Options.GameModes);
    }

    [Fact]
    public void DescribesWhatThePackHoldsPerMode()
    {
        WeaponSlot pistol = WeaponSlots.Find("pistol")!;
        GameMode kzt = GameModes.Find("kzt")!;
        GameMode kz16 = GameModes.Find("kz16")!;
        GameMode surf = GameModes.Find("surf")!;
        var inventory = new List<WeaponPackEntry>
        {
            new(pistol, null, "Glock-18 from Counter-Strike: Source"),
            new(pistol, kz16, "USP .45 from Counter-Strike 1.6"),
        };

        Assert.Null(WeaponsPageViewModel.DescribeInPack(WeaponSlots.Find("knife")!, [surf], inventory));
        Assert.Equal("Glock-18 from Counter-Strike: Source", WeaponsPageViewModel.DescribeInPack(pistol, [surf], inventory));
        Assert.Equal("USP .45 from Counter-Strike 1.6", WeaponsPageViewModel.DescribeInPack(pistol, [kz16], inventory));
        Assert.Equal("Climb (KZT): Glock-18 from Counter-Strike: Source; Climb (1.6): USP .45 from Counter-Strike 1.6", WeaponsPageViewModel.DescribeInPack(pistol, [kzt, kz16], inventory));
        Assert.Equal("Surf: Momentum's; Climb (1.6): USP .45 from Counter-Strike 1.6", WeaponsPageViewModel.DescribeInPack(pistol, [surf, kz16], [inventory[1]]));
    }

    [Fact]
    public void BuildTitlesNameTheModeGameWeaponAndSlot()
    {
        WeaponsPageViewModel page = CreatePage();
        page.SelectedMode = page.Modes.Single(x => x.Name == "Defrag");
        page.SelectedGame = page.Games.Single(x => x.Id == "q3");

        Assert.Contains("Build every Quake III Arena weapon that fits a Defrag weapon", page.BuildAllTip);
        page.SelectedGame = page.Games.Single(x => x.Id == "tf2");
        Assert.Contains("Rocket Launcher as Rocket launcher", page.BuildAllTip);
        page.SelectedMode = page.Modes.Single(x => x.Name == "Conc");
        page.SelectedGame = page.Games.Single(x => x.Id == "tf2");
        Assert.Equal("No Team Fortress 2 weapon fits Conc's weapons", page.BuildAllTip);
    }

    private static object PreparedState(PreparedJob prepared)
    {
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = prepared.WorkDirectory };
        prepared.Configure(context);
        return context.Get<WeaponPackState>();
    }
}
