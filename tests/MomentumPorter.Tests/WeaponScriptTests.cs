namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using Xunit;

public class WeaponScriptTests
{
    [Fact]
    public void ParsesKeyValuesWithCommentsAndConditionals()
    {
        KeyValuesNode root = KeyValuesNode.ParseDocument("""
            // comment
            "Weapon.Single" // trailing
            {
                "channel"   "CHAN_WEAPON"
                volume      0.5 [$X360]
                "rndwave"
                {
                    "wave"  ")weapons/a.wav"
                    "wave"  "weapons/b.wav"
                }
            }
            Other { wave "^c.wav" }
            """);

        Assert.Equal(2, root.Children.Count);
        KeyValuesNode entry = root.Find("weapon.single")!;
        Assert.Equal("CHAN_WEAPON", entry.GetString("channel"));
        Assert.Equal("0.5", entry.GetString("volume"));
        Assert.Equal("[$X360]", entry.Find("volume")!.Condition);
        Assert.Equal([")weapons/a.wav", "weapons/b.wav"], SoundscriptLibrary.GetWaves(entry));

        KeyValuesNode reparsed = KeyValuesNode.ParseDocument(root.Write());
        Assert.Equal(root.Write(), reparsed.Write());
    }

    [Fact]
    public void ThrowsOnUnbalancedBraces()
    {
        Assert.Throws<FormatException>(() => KeyValuesNode.Parse("WeaponData { \"a\" \"b\""));
    }

    [Fact]
    public void EditsModelsSoundsAndParticles()
    {
        WeaponScript script = WeaponScript.Parse("""
            WeaponData
            {
                "weight"    "25"
                "ModelData"
                {
                    "view"      "models/a.mdl"
                    "view_side" "models/a_side.mdl"
                    "world"     "models/w.mdl"
                    "rocket"    "models/r.mdl"
                    "surprise"  "models/s.mdl"
                }
                SoundData { Overrides { } }
            }
            """);

        Assert.Equal(["rocket"], script.ProjectileModelKeys);
        script.SetModel("view", "models/weapons/v_models/v_rocketlauncher_soldier.mdl");
        script.AttachToHands = 0;
        script.BuiltRightHanded = false;
        script.SetSoundOverride("Weapon_RL.Single", "Weapon_RPG.Single.SoundTF2");
        script.SetSoundOverride("Rocket.Trail", "");
        script.SetParticle("RocketTrail", "rockettrail");

        WeaponScript reparsed = WeaponScript.Parse(script.Write("header line\nsecond line"));
        Assert.Equal("models/weapons/v_models/v_rocketlauncher_soldier.mdl", reparsed.GetModel("view"));
        Assert.Equal(0, reparsed.AttachToHands);
        Assert.False(reparsed.BuiltRightHanded);
        Assert.Equal("Weapon_RPG.Single.SoundTF2", reparsed.SoundOverrides["Weapon_RL.Single"]);
        Assert.Equal("", reparsed.SoundOverrides["rocket.trail"]);
        Assert.Equal("rockettrail", reparsed.GetParticle("RocketTrail"));
        // attach_to_hands goes after weight, like Momentum's scripts
        Assert.Equal("attach_to_hands", reparsed.Root.Children[1].Key);
    }

    /// <summary>
    /// Every weapon script Momentum ships parses, and writes back to the same tree.
    /// </summary>
    [Fact]
    public void RoundTripsMomentumWeaponScripts()
    {
        MomentumInstall? momentum = WeaponTestGames.Momentum;
        if (momentum is null)
            return;

        foreach (WeaponSlot slot in WeaponSlots.All)
        {
            WeaponScript script = WeaponScript.Parse(momentum.ReadRequiredText(slot.ScriptPath), slot.ScriptPath);
            Assert.False(string.IsNullOrEmpty(script.GetModel("view")), slot.Id);
            if (slot.ProjectileKey is not null)
                Assert.Contains(slot.ProjectileKey, script.ProjectileModelKeys);

            string written = script.Write();
            Assert.Equal(written, WeaponScript.Parse(written).Write());

            IReadOnlyDictionary<string, string> sounds = WeaponScript.ReadSoundEntries(momentum.ReadRequiredText(slot.ServerScriptPath));
            Assert.NotEmpty(sounds);
        }

        IReadOnlyDictionary<string, string> rocketSounds = WeaponScript.ReadSoundEntries(momentum.ReadRequiredText("scripts/server_weapon/weapon_momentum_rocketlauncher.txt"));
        Assert.Equal("Weapon_RL.Single", rocketSounds["single_shot"]);
        Assert.Equal("Weapon_RL.Explosion", rocketSounds["explosion"]);
    }

    [Fact]
    public void ResolverCopiesMissingAndHiddenEntries()
    {
        var momentum = new SoundscriptLibrary();
        momentum.AddFile("scripts/game_sounds_momentum_weapons.txt", """
            "Weapon_Pistol.Single" { "wave" ")weapons/mom_pistol.wav" }
            "Shared.Same" { "wave" "weapons/same.wav" }
            "Shared.Hidden" { "wave" "weapons/momentum.wav" }
            """);
        momentum.AddFile("scripts/game_sounds_weapons_x.txt", """
            "Weapon_Own.Single" { "wave" "weapons/own.wav" }
            "Weapon_Pistol.Single" { "wave" "weapons/never_loaded.wav" }
            """);

        var source = new SoundscriptLibrary();
        source.AddFile("scripts/game_sounds_weapons.txt", """
            "Weapon_Pistol.Single" { "channel" "CHAN_WEAPON" "wave" "`)weapons/pistol.wav" "operator_stacks" { "x" "y" } }
            "Weapon_New.Single" { "rndwave" { "wave" "~weapons/new1.wav" "wave" "weapons/new2.wav" } }
            "Weapon_New.Reload" { "wave" "weapons/reload.wav" }
            "Weapon_Own.Single" { "wave" "weapons/different.wav" }
            "Shared.Same" { "wave" ")weapons/same.wav" }
            "Shared.Hidden" { "wave" "weapons/source.wav" }
            "Weapon_Missing.Single" { "wave" "weapons/missing.wav" }
            """);

        var game = new CatalogGame { Id = "x", Name = "X", MomentumSoundscripts = ["scripts/game_sounds_weapons_x.txt"] };
        var resolver = new WeaponSoundResolver(momentum, source, game, path => !path.Contains("missing"));

        Assert.Equal(new ResolvedSound("", false), resolver.Resolve(""));
        Assert.Equal(new ResolvedSound("Weapon_Own.Single", false), resolver.Resolve("Weapon_Own.Single"));
        Assert.Equal(new ResolvedSound("Shared.Same", false), resolver.Resolve("Shared.Same"));
        Assert.Equal(new ResolvedSound("Porter_x.Weapon_Pistol.Single", true), resolver.Resolve("Weapon_Pistol.Single"));
        Assert.Equal(new ResolvedSound("Weapon_New.Single", true), resolver.Resolve("Weapon_New.Single"));
        Assert.Null(resolver.Resolve("Weapon_Missing.Single").Entry);
        Assert.Null(resolver.Resolve("Nowhere").Entry);

        // The shot was already renamed for the weapon's override, so it isn't reported as hidden
        Assert.Empty(resolver.AddByPrefix("Weapon_Pistol."));
        Assert.Equal(["Shared.Hidden"], resolver.AddByPrefix("Shared."));
        Assert.Empty(resolver.AddByPrefix("Weapon_New."));

        KeyValuesNode pistol = resolver.AddedEntries.Single(x => x.Key == "Porter_x.Weapon_Pistol.Single");
        Assert.Equal(")weapons/pistol.wav", pistol.GetString("wave"));
        Assert.Null(pistol.Find("operator_stacks"));
        Assert.Contains(resolver.AddedEntries, x => x.Key == "Weapon_New.Reload");
        Assert.Equal(["weapons/new1.wav", "weapons/new2.wav"], SoundscriptLibrary.GetWaves(resolver.AddedEntries.Single(x => x.Key == "Weapon_New.Single")));

        string made = resolver.AddWaves("Porter_x.gun.single_shot", "single_shot", ["sound/porter/x/weapons/a.wav", "sound/porter/x/weapons/b.wav"]);
        KeyValuesNode madeEntry = resolver.AddedEntries.Single(x => x.Key == made);
        Assert.Equal("CHAN_WEAPON", madeEntry.GetString("channel"));
        Assert.Equal([")porter/x/weapons/a.wav", ")porter/x/weapons/b.wav"], SoundscriptLibrary.GetWaves(madeEntry));
    }

    [Fact]
    public void ContentPathsNormalizeSoundWaves()
    {
        Assert.Equal("sound/weapons/ak47/ak47_01.wav", ContentPath.SoundFile("`)weapons/ak47/ak47_01.wav"));
        Assert.Equal("sound/weapons/x.wav", ContentPath.SoundFile("^weapons\\x.wav"));
        Assert.Equal("models/a.mdl", ContentPath.Normalize("/models\\a.mdl"));
    }
}
