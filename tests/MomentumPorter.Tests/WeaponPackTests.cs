namespace MomentumPorter.Tests;

using System.IO.Compression;
using System.Text;
using MomentumPorter.Assets.Models;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Pipeline;
using Xunit;

/// <summary>
/// Reads the installed games and builds weapon packs into temp folders. Each test does nothing when a game it
/// needs isn't installed. Nothing is written into the Momentum install.
/// </summary>
public class WeaponPackTests
{
    [Fact]
    public void VpkReaderFindsCssAk()
    {
        using GameContent? css = WeaponTestGames.Open("css");
        if (css is null)
            return;

        VpkReader vpk = css.Sources.OfType<VpkReader>().Single();
        Assert.Equal("cstrike_pak_dir.vpk", vpk.Description);
        Assert.True(vpk.Exists("models/weapons/v_rif_ak47.mdl"));
        Assert.True(vpk.Exists("Models\\Weapons\\V_RIF_AK47.MDL"));
        Assert.False(vpk.Exists("models/weapons/v_rif_ak48.mdl"));
        Assert.Contains("models/weapons/v_rif_ak47.mdl", vpk.List("models/weapons/"));

        byte[] model = vpk.Read("models/weapons/v_rif_ak47.mdl")!;
        Assert.Equal("IDST", Encoding.ASCII.GetString(model, 0, 4));
        Assert.Null(vpk.Read("models/weapons/v_rif_ak48.mdl"));
    }

    [Fact]
    public void ReadsZipAndPakSources()
    {
        string dir = WeaponTestGames.TempDirectory("sources");
        try
        {
            string zip = Path.Combine(dir, "a.pk3");
            using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(archive.CreateEntry("models/weapons2/x.md3").Open());
                writer.Write("hello");
            }

            string pak = Path.Combine(dir, "pak0.pak");
            using (var stream = new BinaryWriter(File.Create(pak)))
            {
                byte[] data = Encoding.ASCII.GetBytes("world");
                stream.Write(Encoding.ASCII.GetBytes("PACK"));
                stream.Write(12 + data.Length);
                stream.Write(64);
                stream.Write(data);
                byte[] name = new byte[56];
                Encoding.ASCII.GetBytes("sound/weapons/Y.wav").CopyTo(name, 0);
                stream.Write(name);
                stream.Write(12);
                stream.Write(data.Length);
            }

            using var content = new GameContent("test", [new ZipSource(zip), new PakSource(pak), new FolderSource(dir)]);
            Assert.Equal("hello", Encoding.ASCII.GetString(content.Read("MODELS/weapons2/x.md3")!));
            Assert.Equal("world", Encoding.ASCII.GetString(content.Read("sound/weapons/y.wav")!));
            Assert.True(content.Exists("a.pk3"));
            Assert.Contains("sound/weapons/Y.wav", content.List("sound/"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<(PortJobResult Result, string Pack, WeaponPackState State)> BuildAsync(string game, string weapon, string? slot = null, string? output = null, string? modes = null)
    {
        output ??= WeaponTestGames.TempDirectory("weapons");
        var options = new WeaponPackOptions { Game = game, Weapon = weapon, Slot = slot, OutputDirectory = output, GameModes = modes };
        (PortJob job, WeaponPackState state) = WeaponPackBuilder.CreateJob(options);
        state.Steam = WeaponTestGames.Locator;
        string work = PortJob.CreateWorkDirectory("weapon_test");
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = work };
        context.Set(state);
        try
        {
            PortJobResult result = await job.RunAsync(context);
            return (result, state.PackDirectory, state);
        }
        finally
        {
            state.Dispose();
            Directory.Delete(work, recursive: true);
        }
    }

    private static string Report(PortJobResult result)
    {
        var writer = new StringWriter();
        result.Report.Print(writer);
        return writer.ToString();
    }

    private static bool CanBuild(string game) => WeaponTestGames.Momentum is not null && WeaponTestGames.Open(game) is { } content && Dispose(content);

    private static bool Dispose(IDisposable disposable)
    {
        disposable.Dispose();
        return true;
    }

    [Fact]
    public async Task BuildsCssAkPack()
    {
        if (!CanBuild("css"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("css", "ak47");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(WeaponPackBuilder.SharedPackName, Path.GetFileName(pack));

            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_machinegun.txt")));
            // CS:GO ships models/weapons/v_rif_ak47.mdl too, and Momentum mounts both, so CS:S's copy goes in the pack
            if (CanBuild("csgo"))
            {
                Assert.Equal("models/porter/css/weapons/v_rif_ak47.mdl", script.GetModel("view"));
                Assert.True(File.Exists(Path.Combine(pack, "models", "porter", "css", "weapons", "v_rif_ak47.vvd")));
                Assert.True(File.Exists(Path.Combine(pack, "models", "porter", "css", "weapons", "v_rif_ak47.dx90.vtx")));
                Assert.Contains("Counter-Strike: Global Offensive ships", Report(result));
            }
            else
            {
                Assert.Equal("models/weapons/v_rif_ak47.mdl", script.GetModel("view"));
            }
            // The world model is recompiled to sit in Momentum's player's hand
            Assert.Equal("models/porter/css/w_rif_ak47.mdl", script.GetModel("world"));
            Assert.Equal("models/porter/css/w_rif_ak47.mdl", script.GetModel("spawner"));
            Assert.True(File.Exists(Path.Combine(pack, "models", "porter", "css", "w_rif_ak47.mdl")));
            Assert.Equal(0, script.AttachToHands);
            Assert.False(script.BuiltRightHanded);
            Assert.Equal("Weapon_AK47.Single", script.SoundOverrides["Weapon_SMG1.Single"]);
            Assert.Equal("#MOM_Weapon_Machinegun", script.Root.GetString("printname"));

            // The machine gun's shot is played from the player, where Overrides don't apply, so the pack's soundscript
            // redefines Weapon_SMG1.Single as CS:S's AK shot (CS:S's entries are otherwise all in Momentum's copy)
            KeyValuesNode entries = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
            Assert.Single(entries.FindAll("Weapon_SMG1.Single"));
            Assert.Contains("ak47", entries.Find("Weapon_SMG1.Single")!.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Wrote scripts/weapon_momentum_machinegun.txt", Report(result));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    [Fact]
    public async Task BuildsCssScoutPackWithRenamedShot()
    {
        if (!CanBuild("css"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("css", "scout");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_sniper.txt")));

            // Momentum's sniper plays its own Weapon_Scout.Single, which hides CS:S's
            Assert.Equal("Porter_css.Weapon_Scout.Single", script.SoundOverrides["Weapon_Scout.Single"]);
            string soundscript = File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt"));
            KeyValuesNode entries = KeyValuesNode.ParseDocument(soundscript);
            Assert.NotNull(entries.Find("Weapon_RL.Single"));
            Assert.Contains("weapons/scout/scout_fire-1.wav", SoundscriptLibrary.GetWaves(entries.Find("Porter_css.Weapon_Scout.Single")!).Single());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    /// <summary>
    /// Two weapons built into the same folder (the default) share one soundscript copy and one particle manifest.
    /// </summary>
    [Fact]
    public async Task CsgoWeaponsInOneFolderMergeTheirSharedFiles()
    {
        if (!CanBuild("csgo"))
            return;

        string output = WeaponTestGames.TempDirectory("merge");
        try
        {
            (PortJobResult first, string pack, _) = await BuildAsync("csgo", "deagle", "pistol", output);
            Assert.True(first.Succeeded, first.Error);
            (PortJobResult second, string samePack, _) = await BuildAsync("csgo", "ak47", "machinegun", output);
            Assert.True(second.Succeeded, second.Error);
            Assert.Equal(pack, samePack);
            Assert.Equal(WeaponPackBuilder.SharedPackName, Path.GetFileName(pack));

            KeyValuesNode entries = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
            KeyValuesNode shot = Assert.Single(entries.FindAll("Weapon_Pistol.Single"));
            Assert.Contains("deagle", shot.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(entries.Find("Porter_csgo.Weapon_AK47.Single"));
            Assert.NotNull(entries.Find("Weapon_Scout.Single"));

            string manifest = File.ReadAllText(Path.Combine(pack, "particles", "particles_manifest.txt"));
            Assert.Equal(1, manifest.Split("cs_weapon_fx.pcf").Length - 1);
            Assert.True(File.Exists(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            Assert.True(File.Exists(Path.Combine(pack, "scripts", "weapon_momentum_machinegun.txt")));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(pack, "scripts", "porter_weapons")).Length);

            // Pistols from other games get CS:GO's pistol flash when CS:GO is mounted, with its particle file
            (PortJobResult cssPistol, _, _) = await BuildAsync("css", "usp", "pistol", output);
            Assert.True(cssPistol.Succeeded, cssPistol.Error);
            WeaponScript usp = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            Assert.Equal("weapon_muzzle_flash_pistol", usp.GetParticle("MuzzleFlashEffect"));
            Assert.Equal(1, File.ReadAllText(Path.Combine(pack, "particles", "particles_manifest.txt")).Split("cs_weapon_fx.pcf").Length - 1);

            // Another weapon over the pistol replaces the deagle's entries rather than adding to them
            (PortJobResult third, _, _) = await BuildAsync("csgo", "glock", "pistol", output);
            Assert.True(third.Succeeded, third.Error);
            entries = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
            shot = Assert.Single(entries.FindAll("Weapon_Pistol.Single"));
            Assert.Contains("glock", shot.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deagle", shot.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(entries.Find("Porter_csgo.Weapon_AK47.Single"));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(pack, "scripts", "porter_weapons")).Length);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task WeaponsForDifferentModesShareASlot()
    {
        if (!CanBuild("csgo"))
            return;

        string output = WeaponTestGames.TempDirectory("modes");
        try
        {
            // The deagle in KZT, the glock in Climb 1.6: each mode's script, no shared one
            (PortJobResult kzt, string pack, _) = await BuildAsync("csgo", "deagle", "pistol", output, "kzt");
            Assert.True(kzt.Succeeded, kzt.Error);
            (PortJobResult kz16, _, _) = await BuildAsync("csgo", "glock", "pistol", output, "kz16");
            Assert.True(kz16.Succeeded, kz16.Error);

            Assert.False(File.Exists(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            WeaponScript deagle = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "kzt", "weapon_momentum_pistol.txt")));
            WeaponScript glock = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "kz16", "weapon_momentum_pistol.txt")));
            Assert.Contains("deagle", deagle.GetModel("view"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("glock", glock.GetModel("view"), StringComparison.OrdinalIgnoreCase);

            // The shot is played from the player, where the scripts' overrides don't apply, so each mode redefines
            // Momentum's entry in its own soundscript, which the engine loads over Momentum's in that mode only
            Assert.Equal("Porter_csgo.Weapon_DEagle.Single", deagle.SoundOverrides["Weapon_Pistol.Single"]);
            Assert.Equal("Porter_csgo.Weapon_Glock.Single", glock.SoundOverrides["Weapon_Pistol.Single"]);
            string text = File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt"));
            KeyValuesNode entries = KeyValuesNode.ParseDocument(text);
            KeyValuesNode shot = Assert.Single(entries.FindAll("Weapon_Pistol.Single"));
            Assert.Contains("mom_pistol-fire01.wav", shot.Write());
            Assert.NotNull(entries.Find("Porter_csgo.Weapon_DEagle.Single"));
            Assert.NotNull(entries.Find("Porter_csgo.Weapon_Glock.Single"));
            Assert.Equal(["pistol.kz16.txt", "pistol.kzt.txt"], Directory.GetFiles(Path.Combine(pack, "scripts", "porter_weapons")).Select(Path.GetFileName).Order().ToArray());
            Assert.Contains("gamemode-weapon-scripts engine change", Report(kz16));
            Assert.Contains("Weapon_Pistol.Single redefined for Climb (1.6) only", Report(kz16));

            KeyValuesNode kztSounds = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "kzt", "game_sounds_momentum_weapons.txt")));
            Assert.Contains("deagle", Assert.Single(kztSounds.Children).Write(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Weapon_Pistol.Single", kztSounds.Children[0].Key);
            KeyValuesNode kz16Sounds = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "kz16", "game_sounds_momentum_weapons.txt")));
            Assert.Contains("glock", Assert.Single(kz16Sounds.Children).Write(), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(pack, "cfg", "porter_reload.cfg")));

            // A pistol for every mode goes next to them, redefining the shot entry as before, and they keep their modes
            (PortJobResult all, _, _) = await BuildAsync("csgo", "p250", "pistol", output);
            Assert.True(all.Succeeded, all.Error);
            Assert.True(File.Exists(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            Assert.True(File.Exists(Path.Combine(pack, "scripts", "kzt", "weapon_momentum_pistol.txt")));
            entries = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
            shot = Assert.Single(entries.FindAll("Weapon_Pistol.Single"));
            Assert.Contains("p250", shot.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(entries.Find("Porter_csgo.Weapon_DEagle.Single"));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(pack, "scripts", "porter_weapons")).Length);
            Assert.Contains("deagle", File.ReadAllText(Path.Combine(pack, "scripts", "kzt", "game_sounds_momentum_weapons.txt")), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Climb (KZT), Climb (1.6)", Report(all));

            IReadOnlyList<WeaponPackEntry> inventory = WeaponPackInventory.Read(pack);
            Assert.Equal(3, inventory.Count);
            Assert.Equal("Desert Eagle from Counter-Strike: Global Offensive", Assert.Single(inventory, x => x.Mode?.Id == "kzt").Description);
            Assert.Equal("P250 from Counter-Strike: Global Offensive", Assert.Single(inventory, x => x.Mode is null).Description);

            (PortJobResult unknown, _, _) = await BuildAsync("csgo", "deagle", "pistol", output, "kzx");
            Assert.False(unknown.Succeeded);
            Assert.Contains("kzx", unknown.Error);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void GameModeListsParse()
    {
        Assert.Empty(GameModes.Parse(null));
        Assert.Empty(GameModes.Parse(" "));
        Assert.Equal(["kzt", "kz16"], GameModes.Parse("kz16, KZT,kzt").Select(x => x.Id));
        Assert.Equal("kzt,kz16", GameModes.ToList(GameModes.Parse("kz16 kzt")));
        Assert.Null(GameModes.ToList([]));
        Assert.Throws<FormatException>(() => GameModes.Parse("kzt,nope"));
        Assert.Equal(["df_cpm.cfg", "df_vq3.cfg", "df_vtg.cfg"], GameModeConfigs.DefragConfigs);
        Assert.Equal("scripts/kz16/weapon_momentum_pistol.txt", WeaponSlots.Find("pistol")!.ScriptPathFor(GameModes.Find("kz16")));
        Assert.Equal((WeaponSlots.Find("pistol")!, GameModes.Find("kz16")), WriteWeaponPackStep.ParseSoundEntriesPath("scripts/porter_weapons/pistol.kz16.txt"));
        Assert.Equal((WeaponSlots.Find("pistol")!, null), WriteWeaponPackStep.ParseSoundEntriesPath("scripts/porter_weapons/pistol.txt"));
        Assert.Null(WriteWeaponPackStep.ParseSoundEntriesPath("scripts/porter_weapons/csgo_deagle_pistol.txt"));
        Assert.Null(WriteWeaponPackStep.ParseSoundEntriesPath("scripts/porter_weapons/pistol.nope.txt"));
    }

    [Fact]
    public async Task CsgoDeaglePistolRedefinesTheShotAndAsksForCsgoArms()
    {
        if (!CanBuild("csgo"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("csgo", "deagle", "pistol");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_pistol.txt")));
            Assert.Equal(1, script.AttachToHands);
            // CS:GO's own arms share the view model's bones; Momentum's don't
            Assert.Equal("models/weapons/ct_arms.mdl", script.GetModel("arms"));

            // CS:GO's muzzle flash, drawn at the view model's "1" attachment by the engine patch; its particle file
            // only loads through the manifest
            Assert.Equal("weapon_muzzle_flash_pistol", script.GetParticle("MuzzleFlashEffect"));
            Assert.Contains("\"particles/weapons/cs_weapon_fx.pcf\"", File.ReadAllText(Path.Combine(pack, "particles", "particles_manifest.txt")));

            // The pistol's shot is played from the player, where Overrides don't apply, so the entry itself is redefined
            string text = File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt"));
            KeyValuesNode entries = KeyValuesNode.ParseDocument(text);
            Assert.Single(entries.FindAll("Weapon_Pistol.Single"));
            KeyValuesNode shot = entries.Find("Weapon_Pistol.Single")!;
            Assert.Contains("deagle", shot.Write(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mom_pistol-fire01.wav", text);

            // Everything else in Momentum's file is still there
            Assert.NotNull(entries.Find("Weapon_Scout.Single"));
            Assert.Contains("Weapon_Pistol.Single redefined", Report(result));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    [Fact]
    public async Task BuildsCsgoAkPackWithCopiedSounds()
    {
        if (!CanBuild("csgo"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("csgo", "ak47");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_machinegun.txt")));
            Assert.Equal(1, script.AttachToHands);
            // Momentum loads CS:S's Weapon_AK47.Single, which hides CS:GO's
            Assert.Equal("Porter_csgo.Weapon_AK47.Single", script.SoundOverrides["Weapon_SMG1.Single"]);

            KeyValuesNode entries = KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
            KeyValuesNode single = entries.Find("Porter_csgo.Weapon_AK47.Single")!;
            Assert.Equal(")weapons/ak47/ak47_01.wav", single.GetString("wave"));
            Assert.Null(single.Find("operator_stacks"));
            // CS:GO-only entries its animations play are copied under their own names
            Assert.NotNull(entries.Find("Weapon_AK47.SingleDistant"));
            Assert.NotNull(entries.Find("Weapon_PG.Single"));
            Assert.DoesNotContain("The view model's animations play", Report(result));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    [Fact]
    public async Task BuildsTf2RocketLauncherPack()
    {
        if (!CanBuild("tf2"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("tf2", "rocketlauncher");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_rocketlauncher.txt")));
            Assert.Equal("models/weapons/v_models/v_rocketlauncher_soldier.mdl", script.GetModel("view"));
            Assert.Equal("models/weapons/v_models/v_rocketlauncher_soldier.mdl", script.GetModel("view_side"));
            Assert.Equal("models/weapons/w_models/w_rocket.mdl", script.GetModel("rocket"));
            Assert.Equal(0, script.AttachToHands);
            Assert.Equal("Weapon_RPG.Single.SoundTF2", script.SoundOverrides["Weapon_RL.Single"]);
            Assert.Equal("BaseExplosionEffect.SoundTF2", script.SoundOverrides["Weapon_RL.Explosion"]);
            Assert.Equal("", script.SoundOverrides["Rocket.Trail"]);
            Assert.Equal("rockettrail", script.GetParticle("RocketTrail"));
            // Momentum's HL2 content ships a particles/explosion.pcf that hides TF2's, so the pack carries TF2's under its own name
            Assert.True(File.Exists(Path.Combine(pack, "particles", "porter_tf2_explosion.pcf")));
            Assert.Contains("\"particles/porter_tf2_explosion.pcf\"", File.ReadAllText(Path.Combine(pack, "particles", "particles_manifest.txt")));
            Assert.False(File.Exists(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    [Fact]
    public async Task BuildsHl2ShotgunPackWithRenamedShot()
    {
        if (!CanBuild("hl2"))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("hl2", "shotgun");
        try
        {
            Assert.True(result.Succeeded, result.Error);
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_shotgun.txt")));
            Assert.Equal("models/weapons/v_shotgun.mdl", script.GetModel("view"));
            Assert.Equal("Porter_hl2.Weapon_Shotgun.Single", script.SoundOverrides["Weapon_Shotgun.Single"]);
            Assert.NotNull(KeyValuesNode.ParseDocument(File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt"))).Find("Porter_hl2.Weapon_Shotgun.Single"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    [Fact]
    public async Task FailsCleanlyForUnknownWeapon()
    {
        if (WeaponTestGames.Momentum is null)
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("css", "bfg9000");
        Assert.False(result.Succeeded);
        Assert.Contains("no weapon \"bfg9000\"", result.Error);
    }

    /// <summary>
    /// Converts CS 1.6's AK with Momentum's studiomdl (slow).
    /// </summary>
    [Fact]
    public async Task ConvertsCs16AkPack()
    {
        if (!CanBuild("cs16") || !File.Exists(WeaponTestGames.Momentum!.StudioMdlPath))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("cs16", "ak47");
        try
        {
            Assert.True(result.Succeeded, result.Error + Report(result));
            foreach (string file in new[] { "v_ak47.mdl", "v_ak47.vvd", "v_ak47.dx90.vtx", "w_ak47.mdl" })
                Assert.True(File.Exists(Path.Combine(pack, "models", "porter", "cs16", file)), file);

            Assert.NotEmpty(Directory.GetFiles(Path.Combine(pack, "materials", "models", "porter", "cs16", "v_ak47"), "*.vtf"));
            Assert.True(File.Exists(Path.Combine(pack, "sound", "porter", "cs16", "weapons", "ak47-1.wav")));
            // The reload sounds its animation events play
            Assert.True(File.Exists(Path.Combine(pack, "sound", "porter", "cs16", "weapons", "ak47_clipout.wav")));

            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_machinegun.txt")));
            Assert.Equal("models/porter/cs16/v_ak47.mdl", script.GetModel("view"));
            // The model's attachments come along, so its muzzle flash events have somewhere to draw, and the script
            // names the rifle flash Momentum ships for them
            Assert.Contains(StudioModelHeader.Read(Path.Combine(pack, "models", "porter", "cs16", "v_ak47.mdl")).Attachments, x => x.Name == "muzzle");
            Assert.Equal("weapon_muzzle_flash_assaultrifle", script.GetParticle("MuzzleFlashEffect"));
            Assert.Equal("Porter_cs16.ak47.single_shot", script.SoundOverrides["Weapon_SMG1.Single"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    /// <summary>
    /// Converts Quake 3's railgun (MD3) with Momentum's studiomdl (slow).
    /// </summary>
    [Fact]
    public async Task ConvertsQuake3RailgunPack()
    {
        if (!CanBuild("q3") || !File.Exists(WeaponTestGames.Momentum!.StudioMdlPath))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("q3", "railgun");
        try
        {
            Assert.True(result.Succeeded, result.Error + Report(result));
            string models = Path.Combine(pack, "models", "porter", "q3", "weapons2", "railgun");
            foreach (string file in new[] { "v_railgun.mdl", "v_railgun.vvd", "v_railgun.dx90.vtx", "w_railgun.mdl" })
                Assert.True(File.Exists(Path.Combine(models, file)), file);

            Assert.NotEmpty(Directory.GetFiles(Path.Combine(pack, "materials", "models", "porter", "q3", "weapons2", "railgun"), "*.vtf"));
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_df_railgun.txt")));
            Assert.Equal("models/porter/q3/weapons2/railgun/v_railgun.mdl", script.GetModel("view"));
            Assert.Equal("Porter_q3.railgun.single_shot", script.SoundOverrides["Weapon_Scout.Single"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    /// <summary>
    /// Quake 3's rocket launcher gets Quake 3's explosion and smoke trail, and a view model config (slow).
    /// </summary>
    [Fact]
    public async Task Quake3RocketLauncherPackHasQuake3Effects()
    {
        if (!CanBuild("q3") || !File.Exists(WeaponTestGames.Momentum!.DmxConvertPath))
            return;

        string momentumCfg = Path.Combine(WeaponTestGames.Momentum.GameDirectory, "cfg");
        DateTime gameCfgTime = File.Exists(Path.Combine(momentumCfg, "game.cfg")) ? File.GetLastWriteTimeUtc(Path.Combine(momentumCfg, "game.cfg")) : default;
        (PortJobResult result, string pack, _) = await BuildAsync("q3", "rocketlauncher");
        try
        {
            Assert.True(result.Succeeded, result.Error + Report(result));
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_df_rocketlauncher.txt")));
            Assert.Equal("porter_q3_rocket_explosion", script.GetParticle("ExplosionEffect"));
            Assert.Equal("porter_q3_rocket_trail", script.GetParticle("RocketTrail"));
            Assert.Equal("models/porter/q3/ammo/rocket/w_rocket.mdl", script.GetModel("rocket"));

            Assert.True(File.Exists(Path.Combine(pack, "particles", "porter_q3.pcf")));
            Assert.Contains("\"!particles/porter_q3.pcf\"", File.ReadAllText(Path.Combine(pack, "particles", "particles_manifest.txt")));
            Assert.True(File.Exists(Path.Combine(pack, "materials", "porter", "q3", "effects", "rlboom_8.vtf")));

            string viewModel = File.ReadAllText(Path.Combine(pack, "cfg", "porter_q3_viewmodel.cfg"));
            Assert.Matches(@"(?m)^fov_viewmodel \d+", viewModel);

            // Written outside momentum/custom, so the player's configs are left alone
            if (gameCfgTime != default)
                Assert.Equal(gameCfgTime, File.GetLastWriteTimeUtc(Path.Combine(momentumCfg, "game.cfg")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    /// <summary>
    /// Momentum's Defrag grenade draws no trail from the weapon script, so the grenade model draws Quake 3's smoke
    /// itself (slow).
    /// </summary>
    [Fact]
    public async Task Quake3GrenadeModelDrawsItsTrail()
    {
        if (!CanBuild("q3") || !File.Exists(WeaponTestGames.Momentum!.DmxConvertPath) || !File.Exists(WeaponTestGames.Momentum.StudioMdlPath))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("q3", "grenadelauncher");
        try
        {
            Assert.True(result.Succeeded, result.Error + Report(result));
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_df_grenadelauncher.txt")));
            // Still in the script, which precaches it
            Assert.Equal("porter_q3_grenade_trail", script.GetParticle("GrenadeTrail"));

            string[] trailModels = Directory.GetFiles(Path.Combine(pack, "models"), "*.mdl", SearchOption.AllDirectories)
                .Where(x => StudioModelHeader.Read(x).KeyValues.Contains("porter_q3_grenade_trail", StringComparison.Ordinal))
                .ToArray();
            string projectile = Assert.Single(trailModels);
            Assert.StartsWith("w_", Path.GetFileName(projectile));
            string keyValues = StudioModelHeader.Read(projectile).KeyValues;
            Assert.Contains("follow_attachment", keyValues);
            Assert.Matches("attachment_point\"?\\s+\"?trail", keyValues);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }

    /// <summary>Quake 3's plasma ball is a sprite on an invisible projectile, not Momentum's rocket (slow).</summary>
    [Fact]
    public async Task Quake3PlasmaGunPackFiresPlasmaBalls()
    {
        if (!CanBuild("q3") || !File.Exists(WeaponTestGames.Momentum!.DmxConvertPath))
            return;

        (PortJobResult result, string pack, _) = await BuildAsync("q3", "plasmagun");
        try
        {
            Assert.True(result.Succeeded, result.Error + Report(result));
            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_df_plasmagun.txt")));
            Assert.Equal(Quake3Effects.InvisibleProjectile, script.GetModel("plasma"));
            Assert.Equal("porter_q3_plasma_ball", script.GetParticle("PlasmaTrail"));
            Assert.True(File.Exists(Path.Combine(pack, "models", "porter", "q3", "effects", "invisible_projectile.mdl")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(pack)!, recursive: true);
        }
    }
}
