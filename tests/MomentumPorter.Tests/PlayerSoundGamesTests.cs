namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// Player sound packs from Source and GoldSrc games: fake game folders built in a temp folder, and the installed games
/// when there are any.
/// </summary>
public sealed class PlayerSoundGamesTests : IDisposable
{
    private static readonly byte[] Wav = WavFileTests.MakeWav(WavFile.FormatPcm, 1, 22050, 16, WavFileTests.Sine16(441, 1, 22050));

    private const string MomentumSoundscript = """
        "MomPlayer.SprintStart" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.AirJump" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.PowerSlide" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.WallRun" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.LandSoft" { "channel" "CHAN_BODY" "wave" "player/footsteps/mechanical_leg-step_left.wav" }
        "MomPlayer.LandHard" { "channel" "CHAN_BODY" "wave" "player/footsteps/mechanical_leg-step_left.wav" }
        "MomPlayer.Jump" { "channel" "CHAN_AUTO" "volume" "0.1" "wave" "player/jump1.wav" }
        "Default.StepLeft" { "volume" "1.0" "wave" "player/footsteps/concrete/concrete_ct_01.wav" }
        "Default.StepRight" { "volume" "1.0" "wave" "player/footsteps/concrete/concrete_ct_02.wav" }
        "SolidMetal.StepLeft" { "wave" "player/footsteps/metal/metal_solid_35.wav" }
        "Metal_Box.StepLeft" { "wave" "player/footsteps/metal/metal_solid_35.wav" }
        "Snow.StepRight" { "wave" "player/footsteps/snow/snow_07.wav" }
        "Tile.StepLeft" { "wave" "player/footsteps/tile/tile_01.wav" }
        "Ladder.WoodStepRight" { "wave" "player/footsteps/wood/wood_02.wav" }
        """;

    // Momentum's surfaces: metal_box plays Metal_Box.StepLeft, which only CS:S-like games define by that name
    private const string MomentumSurfaces = """
        "default" { "stepleft" "Default.StepLeft" "stepright" "Default.StepRight" }
        "metal" { "stepleft" "SolidMetal.StepLeft" "stepright" "SolidMetal.StepRight" }
        "metal_box" { "base" "metal" "stepleft" "Metal_Box.StepLeft" }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteGame(string name, Dictionary<string, string> texts, IEnumerable<string> waves)
    {
        string root = Path.Combine(_root, name);
        foreach ((string path, string text) in texts)
        {
            string file = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        foreach (string wave in waves)
        {
            string file = Path.Combine(root, wave);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, Wav);
        }

        return root;
    }

    private static (SoundscriptFile Script, CustomPackWriter Pack, PortReport Report, PlayerSoundPackResult Result) Build(PlayerSoundSource source, PlayerSoundPackOptions options)
    {
        var report = new PortReport();
        var pack = new CustomPackWriter(options.ResolvePackName());
        SoundscriptFile script = SoundscriptFile.Parse(MomentumSoundscript);
        PlayerSoundPackResult result = new PlayerSoundPackBuilder(source, NullLog.Instance, report).Build(options, script, pack);
        return (script, pack, report, result);
    }

    private static SurfaceProperties Momentum()
    {
        var surfaces = new SurfaceProperties();
        surfaces.AddFile(MomentumSurfaces);
        return surfaces;
    }

    private static string Print(PortReport report)
    {
        using var writer = new StringWriter();
        report.Print(writer);
        return writer.ToString();
    }

    private SourceGameSoundSource CounterStrikeSource()
    {
        string root = WriteGame("cstrike", new Dictionary<string, string>
        {
            ["scripts/game_sounds_manifest.txt"] = """game_sounds_manifest { "preload_file" "scripts/game_sounds_physics.txt" }""",
            ["scripts/game_sounds_physics.txt"] = """
                "Default.StepLeft" { "pitch" "PITCH_NORM" "rndwave" { "wave" "player/footsteps/concrete1.wav" "wave" "player/footsteps/concrete3.wav" } }
                "Default.StepRight" { "rndwave" { "wave" "player/footsteps/concrete2.wav" "wave" "player/footsteps/concrete4.wav" } }
                "SolidMetal.StepLeft" { "pitch" "95,105" "soundlevel" "SNDLVL_70dB" "wave" ")player/footsteps/metal1.wav" }
                "Tile.StepLeft" { "wave" "player/footsteps/tile1.wav" }
                """,
            ["scripts/surfaceproperties_manifest.txt"] = """surfaceproperties_manifest { "file" "scripts/surfaceproperties.txt" }""",
            // The game's metal_box is plain metal
            ["scripts/surfaceproperties.txt"] = """
                "metal" { "stepleft" "SolidMetal.StepLeft" "stepright" "SolidMetal.StepRight" }
                "metal_box" { "base" "metal" }
                """,
        },
        ["sound/player/footsteps/concrete1.wav", "sound/player/footsteps/concrete2.wav", "sound/player/footsteps/concrete3.wav",
         "sound/player/footsteps/concrete4.wav", "sound/player/footsteps/metal1.wav"]);
        return new SourceGameSoundSource(SourceGames.CounterStrikeSource, new GameContent("css", [new FolderSource(root)]), root, Momentum());
    }

    [Fact]
    public void SourceGameFootstepsByNameThroughSurfacesAndDefault()
    {
        using SourceGameSoundSource css = CounterStrikeSource();
        (SoundscriptFile script, CustomPackWriter pack, PortReport report, PlayerSoundPackResult result) = Build(css, new PlayerSoundPackOptions { Game = "css", Character = "ranger" });

        // Games with one set of sounds have no character, whatever is set
        Assert.Null(result.Character);
        Assert.Equal("css_sounds", pack.Name);

        // By name, with the game's pitch and level; the waves are copied under porter/css/ without their prefix characters
        SoundscriptEntry metal = script.Find("SolidMetal.StepLeft")!;
        Assert.Equal(["porter/css/player/footsteps/metal1.wav"], metal.Waves);
        Assert.Equal("95,105", metal.Pitch);
        Assert.Equal("SNDLVL_70dB", metal.SoundLevel);
        Assert.True(pack.Contains("sound/porter/css/player/footsteps/metal1.wav"));

        // Momentum's metal_box plays Metal_Box.StepLeft, the game's metal_box SolidMetal.StepLeft
        Assert.Equal(["porter/css/player/footsteps/metal1.wav"], script.Find("Metal_Box.StepLeft")!.Waves);

        // Defined, but its wave is missing: falls back to the default footsteps, with a warning
        Assert.Equal(["porter/css/player/footsteps/concrete1.wav", "porter/css/player/footsteps/concrete3.wav"], script.Find("Tile.StepLeft")!.Waves);
        Assert.Contains("sound/player/footsteps/tile1.wav isn't in cstrike", Print(report));

        // Not in the game: the right foot's default
        Assert.Equal(["porter/css/player/footsteps/concrete2.wav", "porter/css/player/footsteps/concrete4.wav"], script.Find("Snow.StepRight")!.Waves);
        Assert.Equal(["porter/css/player/footsteps/concrete2.wav", "porter/css/player/footsteps/concrete4.wav"], script.Find("Ladder.WoodStepRight")!.Waves);

        // No jump sound: Momentum's stays. Landings play both feet's default footsteps
        Assert.Equal(["player/jump1.wav"], script.Find("MomPlayer.Jump")!.Waves);
        Assert.Contains("has no jump sound", Print(report));
        Assert.Equal(4, script.Find("MomPlayer.LandSoft")!.Waves.Count);
        Assert.Equal("0.35", script.Find("MomPlayer.LandHard")!.Volume);
    }

    [Fact]
    public void PureMutesTheJumpOfGamesWithoutOne()
    {
        using SourceGameSoundSource css = CounterStrikeSource();
        (SoundscriptFile script, _, _, _) = Build(css, new PlayerSoundPackOptions { Game = "css", Pure = true, SurfaceFootsteps = false });

        Assert.Equal(["player/q3/silent.wav"], script.Find("MomPlayer.Jump")!.Waves);
        Assert.Equal(["player/q3/silent.wav"], script.Find("MomPlayer.WallRun")!.Waves);
        // Without surface footsteps everything gets the default
        Assert.Equal(["porter/css/player/footsteps/concrete1.wav", "porter/css/player/footsteps/concrete3.wav"], script.Find("SolidMetal.StepLeft")!.Waves);
    }

    [Fact]
    public void CsgoUsesTheTeamsFootstepsAndItsLandingSound()
    {
        string root = WriteGame("csgo", new Dictionary<string, string>
        {
            // Not in a manifest: every game_sounds file is read
            ["scripts/game_sounds_footsteps.txt"] = """
                "CT_Default.StepLeft" { "wave" "~player/footsteps/new/concrete_ct_01.wav" }
                "T_Default.StepLeft" { "pitch" "105, 110" "wave" "~player/footsteps/new/concrete_ct_01.wav" }
                "T_SolidMetal.StepLeft" { "wave" "~player/footsteps/new/metal_solid_01.wav" }
                "Land_Default.StepLeft" { "pitch" "100" "rndwave" { "wave" "~)player/land.wav" "wave" "~)player/land2.wav" } }
                """,
        },
        ["sound/player/footsteps/new/concrete_ct_01.wav", "sound/player/footsteps/new/metal_solid_01.wav", "sound/player/land.wav", "sound/player/land2.wav"]);
        using var csgo = new SourceGameSoundSource(SourceGames.CounterStrikeGlobalOffensive, new GameContent("csgo", [new FolderSource(root)]), root, Momentum());
        Assert.Equal(["ct", "t"], csgo.Characters);

        (SoundscriptFile script, CustomPackWriter pack, _, PlayerSoundPackResult result) = Build(csgo, new PlayerSoundPackOptions { Game = "csgo", Character = "T" });

        Assert.Equal("t", result.Character);
        Assert.Equal("csgo_sounds_t", pack.Name);
        Assert.Equal(["porter/csgo/player/footsteps/new/metal_solid_01.wav"], script.Find("SolidMetal.StepLeft")!.Waves);
        Assert.Equal("105, 110", script.Find("Default.StepLeft")!.Pitch);
        Assert.Equal(["porter/csgo/player/land.wav", "porter/csgo/player/land2.wav"], script.Find("MomPlayer.LandSoft")!.Waves);
        Assert.Equal("100", script.Find("MomPlayer.LandHard")!.Pitch);

        // CT is the default team
        Assert.Equal("csgo_sounds_ct", new PlayerSoundPackOptions { Game = "csgo" }.ResolvePackName());

        var error = Assert.Throws<PortStepException>(() => Build(csgo, new PlayerSoundPackOptions { Game = "csgo", Character = "spectator" }));
        Assert.Contains("no team \"spectator\". Teams: ct, t", error.Message);
    }

    [Fact]
    public void GoldSrcFootstepsBySurfaceAndFoot()
    {
        string[] sets = ["step", "metal", "tile", "ladder"];
        string root = WriteGame("valve", new(), sets.SelectMany(set => Enumerable.Range(1, set == "tile" ? 5 : 4).Select(i => $"sound/player/pl_{set}{i}.wav")).Append("sound/player/pl_jump1.wav"));
        using var hl1 = new GoldSrcSoundSource(SourceGames.HalfLife, new GameContent("hl1", [new FolderSource(root)]), root);
        Assert.Equal(sets.Order(), hl1.Sets.Order());

        (SoundscriptFile script, CustomPackWriter pack, _, _) = Build(hl1, new PlayerSoundPackOptions { Game = "hl1" });

        Assert.Equal("hl1_sounds", pack.Name);
        // One foot plays 1 and 3, the other 2 and 4
        Assert.Equal(["porter/hl1/player/pl_step1.wav", "porter/hl1/player/pl_step3.wav"], script.Find("Default.StepLeft")!.Waves);
        Assert.Equal(["porter/hl1/player/pl_step2.wav", "porter/hl1/player/pl_step4.wav"], script.Find("Default.StepRight")!.Waves);
        Assert.Equal(["porter/hl1/player/pl_metal1.wav", "porter/hl1/player/pl_metal3.wav"], script.Find("Metal_Box.StepLeft")!.Waves);
        // pl_tile5 is never played
        Assert.Equal(["porter/hl1/player/pl_tile1.wav", "porter/hl1/player/pl_tile3.wav"], script.Find("Tile.StepLeft")!.Waves);
        Assert.Equal(["porter/hl1/player/pl_ladder2.wav", "porter/hl1/player/pl_ladder4.wav"], script.Find("Ladder.WoodStepRight")!.Waves);
        // Half-Life has no snow
        Assert.Equal(["porter/hl1/player/pl_step2.wav", "porter/hl1/player/pl_step4.wav"], script.Find("Snow.StepRight")!.Waves);
        // Landings play both feet's concrete steps
        Assert.Equal(4, script.Find("MomPlayer.LandSoft")!.Waves.Count);
        Assert.False(pack.Contains("sound/porter/hl1/player/pl_tile5.wav"));
    }

    [Theory]
    [InlineData("Default.StepLeft", "step")]
    [InlineData("SolidMetal.StepRight", "metal")]
    [InlineData("Metalgrate.StepLeft", "grate")]
    [InlineData("ChainLink.StepLeft", "grate")]
    [InlineData("MetalVent.StepLeft", "duct")]
    [InlineData("Grass.StepLeft", "dirt")]
    [InlineData("Water.StepLeft", "slosh")]
    [InlineData("Wade.StepLeft", "wade")]
    [InlineData("Ladder.WoodStepLeft", "ladder")]
    [InlineData("ceiling_tile.StepLeft", "tile")]
    [InlineData("Snow.StepLeft", "snow")]
    [InlineData("Wood.StepLeft", "step")]
    public void ClassifiesGoldSrcFootsteps(string entry, string set)
    {
        Assert.Equal(set, GoldSrcSoundSource.ClassifyFootstep(entry, surfaceFootsteps: true));
        Assert.Equal("step", GoldSrcSoundSource.ClassifyFootstep(entry, surfaceFootsteps: false));
    }

    [Fact]
    public void SurfacePropertiesFollowBases()
    {
        var surfaces = new SurfaceProperties();
        surfaces.AddFile("""
            "metal" { "stepleft" "SolidMetal.StepLeft" "stepright" "SolidMetal.StepRight" }
            "metalvehicle" { "base" "metal" "stepleft" "MetalVehicle.StepLeft" }
            "loop" { "base" "loop" }
            """);

        Assert.Equal("MetalVehicle.StepLeft", surfaces.GetStep("metalvehicle", left: true));
        Assert.Equal("SolidMetal.StepRight", surfaces.GetStep("metalvehicle", left: false));
        Assert.Null(surfaces.GetStep("loop", left: true));
        Assert.Equal(["metal", "metalvehicle"], surfaces.SurfacesUsing("SolidMetal.StepRight", left: false).Order());
    }

    /// <summary>
    /// The real thing for each installed game, as folder packs in a temp folder (never Momentum's custom folder).
    /// </summary>
    [Theory]
    [InlineData("css")]
    [InlineData("csgo")]
    [InlineData("tf2")]
    [InlineData("hl2")]
    [InlineData("cs16")]
    [InlineData("hl1")]
    public async Task BuildsARealPack(string gameId)
    {
        var steam = new SteamLocator();
        SourceGameInfo game = PlayerSoundSources.Find(gameId)!;
        GameLocation? momentum = steam.FindMomentum();
        if (momentum is null || PlayerSoundSources.Locate(game, null, steam) is null)
            return;

        var options = new PlayerSoundPackOptions { Game = gameId, Pure = true, OutputAsVpk = false, OutputDirectory = _root, MomentumDirectory = momentum.InstallDirectory };
        (PortJob job, PlayerSoundPackState state) = PlayerSoundPackJob.Create(options);
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = Path.Combine(_root, "work") };
        Directory.CreateDirectory(context.WorkDirectory);
        context.Set(state);
        PortJobResult result = await job.RunAsync(context);

        Assert.True(result.Succeeded, result.Error);
        string pack = context.Get<CustomPackOutput>().Path;
        SoundscriptFile script = SoundscriptFile.Load(Path.Combine(pack, "scripts", "game_sounds_momentum_player.txt"));
        Assert.All(script.Entries.Where(x => PlayerSoundPackBuilder.IsFootstep(x.Name)), x => Assert.StartsWith($"porter/{gameId}/", x.Waves[0]));

        string[] waves = Directory.GetFiles(Path.Combine(pack, "sound", "porter", gameId), "*.wav", SearchOption.AllDirectories);
        Assert.NotEmpty(waves);
        Assert.All(waves, x => Assert.True(WavFile.Parse(File.ReadAllBytes(x)).IsSourceCompatible, x));
    }
}
