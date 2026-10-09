namespace MomentumPorter.Tests;

using System.Text;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Output;
using MomentumPorter.Assets.Sounds;
using MomentumPorter.Pipeline;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// Builds packs from a fake Quake 3 pk3 and a fake Momentum folder into a temp folder.
/// </summary>
public sealed class PlayerSoundPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
    private readonly string _quake3;
    private readonly string _momentum;
    private readonly string _output;

    public PlayerSoundPackTests()
    {
        _quake3 = Path.Combine(_root, "Quake 3 Arena", "baseq3");
        _momentum = Path.Combine(_root, "Momentum Mod", "momentum");
        _output = Path.Combine(_root, "out");
        Directory.CreateDirectory(_quake3);
        Directory.CreateDirectory(Path.Combine(_momentum, "scripts"));

        byte[] q3Wav = WavFileTests.MakeWav(WavFile.FormatPcm, 1, 22050, 16, WavFileTests.Sine16(441, 1, 22050));
        var files = new Dictionary<string, byte[]>
        {
            ["sound/player/ranger/jump1.wav"] = q3Wav,
            ["sound/player/ranger/fall1.wav"] = q3Wav,
            // A rate Source can't play, to check it gets re-encoded
            ["sound/player/sarge/jump1.wav"] = WavFileTests.MakeWav(WavFile.FormatPcm, 1, 32000, 16, WavFileTests.Sine16(320, 1, 32000)),
            ["sound/player/announce/ranger.wav"] = q3Wav,
            ["sound/player/land1.wav"] = q3Wav,
            ["models/players/sarge/animation.cfg"] = "sex m\nfootsteps boot\n"u8.ToArray(),
        };
        foreach (string set in new[] { "step", "boot", "clank", "splash", "flesh" })
        {
            for (int i = 1; i <= 4; i++)
                files[$"sound/player/footsteps/{set}{i}.wav"] = q3Wav;
        }

        Pk3ReaderTests.WritePk3(Path.Combine(_quake3, "pak0.pk3"), files);

        File.WriteAllText(Path.Combine(_momentum, "scripts", "game_sounds_momentum_player.txt"), MomentumSoundscript);
        File.WriteAllText(Path.Combine(_momentum, "scripts", "surfaceproperties.txt"), """
            "default" { "stepleft" "Default.StepLeft" "stepright" "Default.StepRight" }
            "metal" { "stepleft" "SolidMetal.StepLeft" "stepright" "SolidMetal.StepRight" }
            "kz_only" { "stepleft" "Undefined.StepLeft" "stepright" "Default.StepRight" }
            """);
    }

    private const string MomentumSoundscript = """
        // Channels

        "MomPlayer.SprintStart"
        {
        	"channel"	"CHAN_VOICE"
        	"wave"	"player/mom_player-sprint.wav"
        }

        "MomPlayer.AirJump" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.PowerSlide" { "wave" "player/mom_player-sprint.wav" }
        "MomPlayer.WallRun" { "wave" "player/mom_player-sprint.wav" }

        "MomPlayer.LandSoft"
        {
        	"channel"	"CHAN_BODY"
        	rndwave
        	{
        		"wave"	"player/footsteps/mechanical_leg-step_left.wav"
        	}
        }

        "MomPlayer.LandHard" { "channel" "CHAN_BODY" "wave" "player/footsteps/mechanical_leg-step_left.wav" }

        "MomPlayer.Jump"
        {
        	"channel"	"CHAN_AUTO"
        	"volume"	"0.1"
        	"pitch"		"95,105"
        	"wave"	"player/jump1.wav"
        }

        // Footsteps

        "Default.StepLeft"
        {
        	"volume"	"1.0"
        	"rndwave" { "wave" "player/footsteps/concrete/concrete_ct_01.wav" }
        }
        "Default.StepRight" { "volume" "1.0" "wave" "player/footsteps/concrete/concrete_ct_02.wav" }
        "SolidMetal.StepLeft" { "volume" "0.5" "wave" "player/footsteps/metal/metal_solid_35.wav" }
        "SolidMetal.StepRight" { "wave" "player/footsteps/metal/metal_solid_36.wav" }
        "Water.StepLeft" { "wave" "player/footsteps/water/slosh2.wav" }
        "Ladder.WoodStepLeft" { "wave" "player/footsteps/wood/wood_01.wav" }
        """;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private async Task<(PortJobResult Result, PortJobContext Context, PlayerSoundPackState State)> RunAsync(PlayerSoundPackOptions options)
    {
        options.Quake3Path ??= _quake3;
        options.MomentumDirectory ??= Path.GetDirectoryName(_momentum);
        options.OutputDirectory ??= _output;

        (PortJob job, PlayerSoundPackState state) = PlayerSoundPackJob.Create(options);
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = Path.Combine(_root, "work") };
        Directory.CreateDirectory(context.WorkDirectory);
        context.Set(state);
        return (await job.RunAsync(context), context, state);
    }

    private static string Print(PortJobResult result)
    {
        using var writer = new StringWriter();
        result.Report.Print(writer);
        return writer.ToString();
    }

    [Fact]
    public void PreviewListsEachCharactersSoundsAsThePackWouldUseThem()
    {
        using Quake3SoundSource source = Quake3SoundSource.Open(Path.GetDirectoryName(_quake3)!);
        var options = new PlayerSoundPackOptions();
        options.ApplyGameAccurate();

        IReadOnlyList<PreviewSound> ranger = PlayerSoundPreview.List(source, "ranger", options);
        Assert.Equal(["Jump", "Soft landing", "Hard landing", "Footsteps", "Footsteps", "Footsteps"], ranger.Select(x => x.Name));
        Assert.Equal("sound/player/ranger/jump1.wav", Assert.Single(ranger[0].Sound.Files).Source);
        Assert.Equal("sound/player/ranger/fall1.wav", Assert.Single(ranger[2].Sound.Files).Source);

        // Footsteps sounding the same on several surfaces are one entry: the character's set, metal's clank, water's splash
        Assert.Equal(["step", "clank", "splash"], ranger.Where(x => x.Name == "Footsteps").Select(x => x.Sound.Label));
        Assert.StartsWith("Concrete, default", ranger[3].Detail);
        Assert.Contains("metal grate", ranger[4].Detail);
        Assert.Equal("Shallow water, wading", ranger[5].Detail);
        Assert.Equal(4, ranger[3].Sound.Files.Count);

        // Sarge: his own jump and boots; his fall1.wav isn't in the pk3, so there's no hard landing to hear
        IReadOnlyList<PreviewSound> sarge = PlayerSoundPreview.List(source, "sarge", options);
        Assert.Equal("sound/player/sarge/jump1.wav", Assert.Single(sarge[0].Sound.Files).Source);
        Assert.DoesNotContain(sarge, x => x.Name == "Hard landing");
        Assert.Equal("boot", sarge.First(x => x.Name == "Footsteps").Sound.Label);
    }

    [Fact]
    public void FindsCharactersAndFootstepTypes()
    {
        using Quake3SoundSource source = Quake3SoundSource.Open(Path.GetDirectoryName(_quake3)!);
        Assert.Equal(["ranger", "sarge"], source.Characters);
        Assert.Equal(Quake3FootstepType.Boot, source.GetFootstepType("sarge"));
        Assert.Equal(Quake3FootstepType.Normal, source.GetFootstepType("ranger"));
    }

    [Fact]
    public async Task BuildsAPureQuake3FolderPack()
    {
        (PortJobResult result, PortJobContext context, PlayerSoundPackState state) = await RunAsync(new PlayerSoundPackOptions
        {
            Character = "Ranger",
            Pure = true,
            OutputAsVpk = false,
        });

        Assert.True(result.Succeeded, result.Error);
        string pack = Path.Combine(_output, "q3_sounds_ranger");
        Assert.Equal(pack, context.Get<CustomPackOutput>().Path);

        string[] files = Directory.GetFiles(pack, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(pack, x).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                CustomPackWriter.MarkerFileName,
                "scripts/game_sounds_momentum_player.txt",
                .. Enumerable.Range(1, 4).Select(i => $"sound/player/footsteps/q3/clank{i}.wav"),
                .. Enumerable.Range(1, 4).Select(i => $"sound/player/footsteps/q3/splash{i}.wav"),
                .. Enumerable.Range(1, 4).Select(i => $"sound/player/footsteps/q3/step{i}.wav"),
                "sound/player/q3/land1.wav",
                "sound/player/q3/silent.wav",
                "sound/player/ranger/fall1.wav",
                "sound/player/ranger/jump1.wav",
            ],
            files);

        SoundscriptFile script = SoundscriptFile.Load(Path.Combine(pack, "scripts", "game_sounds_momentum_player.txt"));
        SoundscriptEntry jump = script.Find("MomPlayer.Jump")!;
        Assert.Equal(["player/ranger/jump1.wav"], jump.Waves);
        Assert.Equal("CHAN_VOICE", jump.Channel);
        Assert.Equal("0.35", jump.Volume);
        Assert.Null(jump.Pitch);
        Assert.Equal(["player/q3/land1.wav"], script.Find("MomPlayer.LandSoft")!.Waves);
        Assert.Equal(["player/ranger/fall1.wav"], script.Find("MomPlayer.LandHard")!.Waves);
        foreach (string muted in PlayerSoundPackBuilder.MomentumOnlySounds)
            Assert.Equal(["player/q3/silent.wav"], script.Find(muted)!.Waves);

        string[] steps = Enumerable.Range(1, 4).Select(i => $"player/footsteps/q3/step{i}.wav").ToArray();
        Assert.Equal(steps, script.Find("Default.StepLeft")!.Waves);
        Assert.Equal(steps, script.Find("Default.StepRight")!.Waves);
        Assert.Equal(steps, script.Find("Ladder.WoodStepLeft")!.Waves);
        Assert.Equal(Enumerable.Range(1, 4).Select(i => $"player/footsteps/q3/clank{i}.wav"), script.Find("SolidMetal.StepLeft")!.Waves);
        Assert.Equal("1.0", script.Find("SolidMetal.StepLeft")!.Volume);
        Assert.Equal(Enumerable.Range(1, 4).Select(i => $"player/footsteps/q3/splash{i}.wav"), script.Find("Water.StepLeft")!.Waves);
        // surfaceproperties uses it but the soundscript didn't define it
        Assert.Equal(steps, script.Find("Undefined.StepLeft")!.Waves);

        // The rest of Momentum's file is kept as is
        string text = File.ReadAllText(Path.Combine(pack, "scripts", "game_sounds_momentum_player.txt"));
        Assert.StartsWith("// Channels", text);

        Assert.Null(state.Result!.ExecCommand);
        Assert.Contains("MomPlayer.SprintStart muted", Print(result));
        Assert.All(files.Where(x => x.EndsWith(".wav")), x => Assert.True(WavFile.Parse(File.ReadAllBytes(Path.Combine(pack, x))).IsSourceCompatible));
    }

    [Fact]
    public async Task SilentFootstepsWriteACfg()
    {
        (PortJobResult result, _, PlayerSoundPackState state) = await RunAsync(new PlayerSoundPackOptions
        {
            Character = "sarge",
            JumpSound = JumpSoundMode.Game,
            LandSound = LandSoundMode.None,
            Footsteps = FootstepMode.Silent,
            PackName = "quiet",
            OutputAsVpk = false,
        });

        Assert.True(result.Succeeded, result.Error);
        string pack = Path.Combine(_output, "quiet");
        Assert.Contains("snd_volume_footsteps 0", File.ReadAllText(Path.Combine(pack, "cfg", "quiet.cfg")));
        Assert.Equal("exec quiet", state.Result!.ExecCommand);

        SoundscriptFile script = SoundscriptFile.Load(Path.Combine(pack, "scripts", "game_sounds_momentum_player.txt"));
        Assert.Equal(["player/q3/silent.wav"], script.Find("Default.StepLeft")!.Waves);
        Assert.Equal(["player/q3/silent.wav"], script.Find("MomPlayer.LandHard")!.Waves);
        // Not pure, so Momentum's own sounds stay
        Assert.Equal(["player/mom_player-sprint.wav"], script.Find("MomPlayer.SprintStart")!.Waves);

        // Sarge's 32 kHz jump is re-encoded at a rate Source plays
        WavFile jump = WavFile.Parse(File.ReadAllBytes(Path.Combine(pack, "sound", "player", "sarge", "jump1.wav")));
        Assert.Equal(44100, jump.SampleRate);
        Assert.Contains("re-encoded for Source", Print(result));
        Assert.False(Directory.Exists(Path.Combine(pack, "sound", "player", "footsteps")));
    }

    [Fact]
    public async Task UsesTheCharactersFootstepSet()
    {
        (PortJobResult result, _, _) = await RunAsync(new PlayerSoundPackOptions { Character = "sarge", OutputAsVpk = false, SurfaceFootsteps = false });

        Assert.True(result.Succeeded, result.Error);
        SoundscriptFile script = SoundscriptFile.Load(Path.Combine(_output, "q3_sounds_sarge", "scripts", "game_sounds_momentum_player.txt"));
        Assert.Equal(Enumerable.Range(1, 4).Select(i => $"player/footsteps/q3/boot{i}.wav"), script.Find("SolidMetal.StepLeft")!.Waves);
    }

    [Fact]
    public async Task DefaultsLeaveMomentumsEntries()
    {
        (PortJobResult result, _, _) = await RunAsync(new PlayerSoundPackOptions
        {
            JumpSound = JumpSoundMode.MomentumDefault,
            LandSound = LandSoundMode.MomentumDefault,
            Footsteps = FootstepMode.MomentumDefault,
            Pure = true,
            OutputAsVpk = false,
        });

        Assert.True(result.Succeeded, result.Error);
        string text = File.ReadAllText(Path.Combine(_output, "q3_sounds_ranger", "scripts", "game_sounds_momentum_player.txt"));
        SoundscriptFile script = SoundscriptFile.Parse(text);
        SoundscriptFile original = SoundscriptFile.Parse(MomentumSoundscript);

        // Only the four muted entries changed; the rest is Momentum's text as it was
        foreach (SoundscriptEntry entry in original.Entries)
        {
            IReadOnlyList<string> expected = PlayerSoundPackBuilder.MomentumOnlySounds.Contains(entry.Name) ? ["player/q3/silent.wav"] : entry.Waves;
            Assert.Equal(expected, script.Find(entry.Name)!.Waves);
        }

        Assert.Contains("""
            "MomPlayer.Jump"
            {
            	"channel"	"CHAN_AUTO"
            	"volume"	"0.1"
            	"pitch"		"95,105"
            	"wave"	"player/jump1.wav"
            }
            """, text);
    }

    [Fact]
    public async Task FailsOnUnknownCharacter()
    {
        (PortJobResult result, _, _) = await RunAsync(new PlayerSoundPackOptions { Character = "nobody", OutputAsVpk = false });

        Assert.False(result.Succeeded);
        Assert.Contains("ranger, sarge", result.Error);
        Assert.False(Directory.Exists(_output));
    }

    [Fact]
    public async Task OnlyReplacesFoldersItMade()
    {
        string pack = Path.Combine(_output, "q3_sounds_ranger");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "mine.txt"), "user's file");

        (PortJobResult refused, _, _) = await RunAsync(new PlayerSoundPackOptions { OutputAsVpk = false });
        Assert.False(refused.Succeeded);
        Assert.True(File.Exists(Path.Combine(pack, "mine.txt")));

        Directory.Delete(pack, recursive: true);
        Assert.True((await RunAsync(new PlayerSoundPackOptions { OutputAsVpk = false })).Result.Succeeded);
        File.WriteAllText(Path.Combine(pack, "stale.txt"), "left from an older build");
        Assert.True((await RunAsync(new PlayerSoundPackOptions { OutputAsVpk = false })).Result.Succeeded);
        Assert.False(File.Exists(Path.Combine(pack, "stale.txt")));
    }

    [Fact]
    public void PackWriterRejectsPathsOutsideThePack()
    {
        var writer = new CustomPackWriter("test");
        Assert.Throws<ArgumentException>(() => writer.AddFile("../escape.txt", [1]));
        Assert.Throws<ArgumentException>(() => writer.AddFile(@"C:\abs.txt", [1]));
        Assert.Throws<ArgumentException>(() => new CustomPackWriter("bad/name"));
        writer.AddFile(@"Sound\Player\X.wav", [1]);
        Assert.Equal(["sound/player/x.wav"], writer.Files);
    }

    /// <summary>
    /// The real thing when Quake 3 and Momentum (with vpk.exe) are installed: Ranger, pure Quake 3, as a VPK in a temp
    /// folder (never Momentum's custom folder).
    /// </summary>
    [Fact]
    public async Task BuildsARealRangerVpk()
    {
        var locator = new SteamLocator();
        string? quake3 = Quake3SoundSource.FindInstalled(locator);
        GameLocation? momentum = locator.FindMomentum();
        if (quake3 is null || momentum is null || CustomPackWriter.FindVpkTool(momentum) is null)
            return;

        (PortJobResult result, PortJobContext context, _) = await RunAsync(new PlayerSoundPackOptions
        {
            Character = "ranger",
            Pure = true,
            Quake3Path = quake3,
            MomentumDirectory = momentum.InstallDirectory,
        });

        Assert.True(result.Succeeded, result.Error);
        string vpk = context.Get<CustomPackOutput>().Path;
        Assert.Equal(Path.Combine(_output, "q3_sounds_ranger.vpk"), vpk);

        IReadOnlyList<string> files = VpkDirectory.ReadFiles(vpk);
        Assert.Contains("scripts/game_sounds_momentum_player.txt", files);
        Assert.Contains("sound/player/ranger/jump1.wav", files);
        Assert.Contains("sound/player/ranger/fall1.wav", files);
        Assert.Contains("sound/player/q3/land1.wav", files);
        Assert.Contains("sound/player/q3/silent.wav", files);
        Assert.Contains("sound/player/footsteps/q3/step1.wav", files);
        Assert.DoesNotContain(CustomPackWriter.MarkerFileName, files);
    }

    /// <summary>Lists a single-file VPK's (v1 or v2) files from its directory tree.</summary>
    private static class VpkDirectory
    {
        public static IReadOnlyList<string> ReadFiles(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.ASCII);
            Assert.Equal(0x55AA1234u, reader.ReadUInt32());
            uint version = reader.ReadUInt32();
            reader.ReadUInt32(); // tree size
            if (version == 2)
                reader.ReadBytes(16);

            var files = new List<string>();
            while (ReadString(reader) is { Length: > 0 } extension)
            {
                while (ReadString(reader) is { Length: > 0 } folder)
                {
                    while (ReadString(reader) is { Length: > 0 } name)
                    {
                        reader.ReadUInt32(); // crc
                        ushort preload = reader.ReadUInt16();
                        reader.ReadBytes(2 + 4 + 4 + 2); // archive index, offset, length, terminator
                        reader.ReadBytes(preload);
                        files.Add((folder == " " ? "" : folder + "/") + name + "." + extension);
                    }
                }
            }

            return files;
        }

        private static string ReadString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
                bytes.Add(b);
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
    }
}
