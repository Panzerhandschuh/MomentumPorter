namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Sounds;
using MomentumPorter.Steam;
using Xunit;

public class SoundscriptFileTests
{
    // The quirks of Momentum's game_sounds_momentum_player.txt: comments, quoted and unquoted keys, rndwave blocks
    public const string Sample = """
        // Channels
        //	CHAN_AUTO		= 0,

        "MomPlayer.Jump"
        {
        	"channel"	"CHAN_AUTO"
        	"volume"	"0.1"
        	"soundlevel"  "SNDLVL_NORM"
        	"pitch"		"95,105"

        	"wave"	"player/jump1.wav"
        }

        MomPlayer.StepLeft
        {
        	channel		CHAN_BODY
        	volume		.35 // quiet
        	soundlevel	SNDLVL_NORM

        	wave		"player/footsteps/mechanical_leg-step_left.wav"
        }

        // Footsteps

        "Default.StepLeft"
        {
        	"CompatibilityAttenuation"	"1.0"
        	"volume"			"1.0"
        	"rndwave"
        	{
        		"wave"		"player/footsteps/concrete/concrete_ct_01.wav"
        		"wave"		"player/footsteps/concrete/concrete_ct_03.wav"
        	}
        }

        "Console.Only"
        {
        	"wave"	"a.wav" [$X360]
        	"wave"	"b.wav" [!$X360]
        }
        """;

    [Fact]
    public void RoundTripsUnchangedText()
    {
        SoundscriptFile file = SoundscriptFile.Parse(Sample);

        Assert.Equal(["MomPlayer.Jump", "MomPlayer.StepLeft", "Default.StepLeft", "Console.Only"], file.Entries.Select(x => x.Name));
        Assert.Equal(Sample, file.ToText());
    }

    [Fact]
    public void ReadsEntries()
    {
        SoundscriptFile file = SoundscriptFile.Parse(Sample);

        SoundscriptEntry jump = file.Find("momplayer.jump")!;
        Assert.Equal("CHAN_AUTO", jump.Channel);
        Assert.Equal("0.1", jump.Volume);
        Assert.Equal("95,105", jump.Pitch);
        Assert.Equal(["player/jump1.wav"], jump.Waves);

        Assert.Equal(".35", file.Find("MomPlayer.StepLeft")!.Volume);
        Assert.Equal(["player/footsteps/concrete/concrete_ct_01.wav", "player/footsteps/concrete/concrete_ct_03.wav"], file.Find("Default.StepLeft")!.Waves);
        Assert.Equal("[$X360]", file.Find("Console.Only")!.Node.Children![0].Conditional);
    }

    [Fact]
    public void ReplacesOneEntryAndKeepsTheRest()
    {
        SoundscriptFile file = SoundscriptFile.Parse(Sample);
        SoundscriptEntry jump = file.Find("MomPlayer.Jump")!;
        jump.Volume = "0.35";
        jump.Pitch = null;
        jump.SetWaves(["player/ranger/jump1.wav"]);

        string text = file.ToText();

        Assert.Contains("""
            "MomPlayer.Jump"
            {
            	"channel"	"CHAN_AUTO"
            	"volume"	"0.35"
            	"soundlevel"	"SNDLVL_NORM"
            	"wave"	"player/ranger/jump1.wav"
            }
            """.ReplaceLineEndings(file.NewLine), text);
        Assert.DoesNotContain("95,105", text);
        // Everything else is untouched, comments included
        Assert.StartsWith("// Channels", text);
        Assert.Contains("volume		.35 // quiet", text);
        Assert.Contains("\"wave\"	\"a.wav\" [$X360]", text);

        // And it parses back to the same thing
        SoundscriptFile reparsed = SoundscriptFile.Parse(text);
        Assert.Equal(["player/ranger/jump1.wav"], reparsed.Find("MomPlayer.Jump")!.Waves);
        Assert.Equal(file.Entries.Select(x => x.Name), reparsed.Entries.Select(x => x.Name));
        Assert.Equal(text, reparsed.ToText());
    }

    [Fact]
    public void SetWavesSwitchesBetweenWaveAndRndwave()
    {
        SoundscriptFile file = SoundscriptFile.Parse(Sample);
        SoundscriptEntry step = file.Find("Default.StepLeft")!;

        step.SetWaves(["x.wav"]);
        Assert.Null(step.Node.Find("rndwave"));
        Assert.Equal("x.wav", step.Node.GetValue("wave"));

        SoundscriptEntry jump = file.Find("MomPlayer.Jump")!;
        jump.SetWaves(["1.wav", "2.wav"]);
        Assert.Null(jump.Node.Find("wave"));
        Assert.Equal(["1.wav", "2.wav"], jump.Waves);
        Assert.Equal(["1.wav", "2.wav"], SoundscriptFile.Parse(file.ToText()).Find("MomPlayer.Jump")!.Waves);
    }

    [Fact]
    public void AddsAndRemovesEntries()
    {
        SoundscriptFile file = SoundscriptFile.Parse(Sample);
        SoundscriptEntry added = file.AddCopy("Metal.StepLeft", file.Find("Default.StepLeft")!);
        added.SetWaves(["clank.wav"]);
        Assert.True(file.Remove("Console.Only"));
        Assert.False(file.Remove("Nope"));

        SoundscriptFile reparsed = SoundscriptFile.Parse(file.ToText());
        Assert.Equal(["MomPlayer.Jump", "MomPlayer.StepLeft", "Default.StepLeft", "Metal.StepLeft"], reparsed.Entries.Select(x => x.Name));
        Assert.Equal("1.0", reparsed.Find("Metal.StepLeft")!.Volume);
        Assert.Equal(["clank.wav"], reparsed.Find("Metal.StepLeft")!.Waves);
        // The template is unchanged
        Assert.Equal(2, reparsed.Find("Default.StepLeft")!.Waves.Count);
    }

    [Fact]
    public void FirstDefinitionWins()
    {
        SoundscriptFile file = SoundscriptFile.Parse("\"A\" { \"wave\" \"1.wav\" }\n\"a\" { \"wave\" \"2.wav\" }\n");
        Assert.Equal(["1.wav"], file.Find("a")!.Waves);
    }

    [Fact]
    public void RejectsBrokenFiles()
    {
        Assert.Throws<FormatException>(() => SoundscriptFile.Parse("\"A\" { \"wave\" \"1.wav\""));
        Assert.Throws<FormatException>(() => SoundscriptFile.Parse("}"));
    }

    [Fact]
    public void RoundTripsMomentumsSoundscripts()
    {
        string? content = new SteamLocator().FindMomentum()?.ContentDirectory;
        if (content is null)
            return;

        foreach (string path in Directory.GetFiles(Path.Combine(content, "scripts"), "game_sounds*.txt"))
        {
            string text = File.ReadAllText(path);
            SoundscriptFile file = SoundscriptFile.Parse(text, path);
            Assert.Equal(text, file.ToText());
        }

        SoundscriptFile player = SoundscriptFile.Load(Path.Combine(content, "scripts", "game_sounds_momentum_player.txt"));
        Assert.NotNull(player.Find("MomPlayer.Jump"));
        Assert.NotNull(player.Find("Default.StepLeft"));
    }
}
