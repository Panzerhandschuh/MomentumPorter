namespace MomentumPorter.Tests;

using System.IO.Compression;
using System.Text;
using MomentumPorter.Assets.Vpk;
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

    private static async Task<(PortJobResult Result, string Pack, WeaponPackState State)> BuildAsync(string game, string weapon, string? slot = null)
    {
        string output = WeaponTestGames.TempDirectory("weapons");
        var options = new WeaponPackOptions { Game = game, Weapon = weapon, Slot = slot, OutputDirectory = output };
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
            Assert.Equal("css_ak47_machinegun", Path.GetFileName(pack));

            WeaponScript script = WeaponScript.Parse(File.ReadAllText(Path.Combine(pack, "scripts", "weapon_momentum_machinegun.txt")));
            Assert.Equal("models/weapons/v_rif_ak47.mdl", script.GetModel("view"));
            Assert.Equal("models/weapons/w_rif_ak47.mdl", script.GetModel("world"));
            Assert.Equal("models/weapons/w_rif_ak47.mdl", script.GetModel("spawner"));
            Assert.Equal(0, script.AttachToHands);
            Assert.False(script.BuiltRightHanded);
            Assert.Equal("Weapon_AK47.Single", script.SoundOverrides["Weapon_SMG1.Single"]);
            Assert.Equal("#MOM_Weapon_Machinegun", script.Root.GetString("printname"));

            // CS:S's AK entries are all in Momentum's copy, so no soundscript is needed
            Assert.False(File.Exists(Path.Combine(pack, "scripts", "game_sounds_momentum_weapons.txt")));
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
}
