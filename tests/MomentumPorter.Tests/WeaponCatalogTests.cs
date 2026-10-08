namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using Xunit;

public class WeaponCatalogTests
{
    [Fact]
    public void EmbeddedCatalogLoadsAndValidates()
    {
        WeaponCatalog catalog = WeaponCatalog.LoadEmbedded();
        Assert.Empty(catalog.Validate());
        Assert.Equal(["css", "csgo", "tf2", "hl2", "cs16", "hl1", "q3"], catalog.Games.Select(x => x.Id));
    }

    [Fact]
    public void CatalogCoversTheSlotsEachGameHas()
    {
        WeaponCatalog catalog = WeaponCatalog.Default;
        string[] csSlots = ["pistol", "machinegun", "shotgun", "sniper", "knife", "grenade"];
        foreach (string game in new[] { "css", "csgo", "cs16" })
            Assert.All(csSlots, slot => Assert.Contains(catalog.FindGame(game)!.Weapons, x => x.Slots.Contains(slot)));

        CatalogGame tf2 = catalog.FindGame("tf2")!;
        Assert.Equal("rocketlauncher", tf2.FindWeapon("rocketlauncher")!.Slots[0]);
        Assert.Equal("stickylauncher", tf2.FindWeapon("stickybomblauncher")!.Slots[0]);
        Assert.Contains("shotgun", tf2.FindWeapon("scattergun")!.Slots);

        string[] dfSlots = ["df_knife", "df_machinegun", "df_shotgun", "df_grenadelauncher", "df_rocketlauncher", "df_lightninggun", "df_railgun", "df_plasmagun", "df_bfg"];
        Assert.All(dfSlots, slot => Assert.Contains(catalog.FindGame("q3")!.Weapons, x => x.Slots[0] == slot));
    }

    [Fact]
    public void ValidateReportsBadEntries()
    {
        WeaponCatalog catalog = WeaponCatalog.Load("""
            { "games": [
              { "id": "css", "weapons": [
                { "id": "a", "name": "A", "slots": [ "laser" ], "view": "models/a.md3", "waves": { "single_shot": [ "weapons/a.wav" ] } },
                { "id": "a", "name": "", "slots": [], "view": "" } ] },
              { "id": "doom" } ] }
            """);

        IReadOnlyList<string> problems = catalog.Validate();
        Assert.Contains(problems, x => x.Contains("unknown slot laser"));
        Assert.Contains(problems, x => x.Contains("isn't a .mdl model"));
        Assert.Contains(problems, x => x.Contains("should use soundscript entries"));
        Assert.Contains(problems, x => x.Contains("listed 2 times"));
        Assert.Contains(problems, x => x.Contains("fits no slot"));
        Assert.Contains(problems, x => x.Contains("doom: not a game"));
    }

    /// <summary>
    /// Every model and sound of the mounted games' weapons exists where Momentum loads it, when the games are installed.
    /// </summary>
    [Theory]
    [InlineData("css")]
    [InlineData("csgo")]
    [InlineData("tf2")]
    [InlineData("hl2")]
    public void MountedGameWeaponsResolve(string gameId)
    {
        MomentumInstall? momentum = WeaponTestGames.Momentum;
        using GameContent? content = WeaponTestGames.Open(gameId);
        if (momentum is null || content is null)
            return;

        CatalogGame game = WeaponCatalog.Default.FindGame(gameId)!;
        var sourceGame = new SoundscriptLibrary();
        foreach (string script in game.Soundscripts)
        {
            string? text = content.Read(script) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : momentum.ReadText(script);
            Assert.NotNull(text);
            sourceGame.AddFile(script, text);
        }

        var resolver = new WeaponSoundResolver(SoundscriptLibrary.LoadMomentum(momentum), sourceGame, game, x => content.Exists(x) || File.Exists(Path.Combine(momentum.GameDirectory, x)));
        var problems = new List<string>();
        foreach (CatalogWeapon weapon in game.Weapons)
        {
            problems.AddRange(weapon.Models.Where(x => !content.Exists(x)).Select(x => $"{weapon.Id}: {x} missing"));
            foreach ((string role, string entry) in weapon.Sounds)
            {
                ResolvedSound resolved = resolver.Resolve(entry);
                if (resolved.Entry is null)
                    problems.Add($"{weapon.Id} {role}: {resolved.Problem}");
            }

            foreach (string prefix in weapon.SoundPrefixes)
            {
                if (!sourceGame.Entries.Any(x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    problems.Add($"{weapon.Id}: no entries start with {prefix}");
            }

            foreach (string slot in weapon.Slots)
            {
                IReadOnlyDictionary<string, string> slotSounds = WeaponScript.ReadSoundEntries(momentum.ReadRequiredText(WeaponSlots.Find(slot)!.ServerScriptPath));
                if (weapon.Sounds.Count > 0 && !weapon.Sounds.Keys.Any(slotSounds.ContainsKey))
                    problems.Add($"{weapon.Id}: none of its sounds are used by {slot}");
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// Every model and wave of the converted games' weapons exists in the game's files, when they're installed.
    /// </summary>
    [Theory]
    [InlineData("cs16")]
    [InlineData("hl1")]
    [InlineData("q3")]
    public void ConvertedGameWeaponsExist(string gameId)
    {
        using GameContent? content = WeaponTestGames.Open(gameId);
        if (content is null)
            return;

        CatalogGame game = WeaponCatalog.Default.FindGame(gameId)!;
        var missing = game.Weapons
            .SelectMany(w => w.Models.Concat(w.Waves.Values.SelectMany(x => x)).Select(x => (Weapon: w.Id, Path: x)))
            .Where(x => !content.Exists(x.Path))
            .Select(x => $"{x.Weapon}: {x.Path}")
            .ToList();
        Assert.Empty(missing);
    }
}
