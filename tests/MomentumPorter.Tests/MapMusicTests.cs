namespace MomentumPorter.Tests;

using System.Buffers.Binary;
using LibBSP;
using MomentumPorter.Assets.Common;
using MomentumPorter.Fixes;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Review;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using Xunit;

/// <summary>
/// Finding a map's packed music and moving it into sound/music/ for the music volume slider. The map tests use the
/// CS:S maps and Momentum's installed copies (bhop_badges was fixed by hand in Lumper) and do nothing without them.
/// </summary>
public class MapMusicTests
{
    private static string? FindCssMap(string name)
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        string? path = css?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    private static string? FindMomentumMap(string name)
    {
        string? path = new SteamLocator().FindMomentum()?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }

    private static PortContext Open(string path) => new(new BSP(new FileInfo(path)), Path.GetFileNameWithoutExtension(path), null);

    private static byte[] Wav(int sampleRate, int channels, double seconds)
    {
        int frames = (int)(sampleRate * seconds);
        return new WavFile(WavFile.FormatPcm, channels, sampleRate, 16, new byte[frames * channels * 2]).ToBytes();
    }

    [Fact]
    public void WavLengthComesFromItsHeaders()
    {
        Assert.Equal(2.5, SoundLength.Read(new MemoryStream(Wav(22050, 2, 2.5)), 0)!.Value.TotalSeconds, 3);
        Assert.Equal(1.0, SoundLength.Read(new MemoryStream(Wav(44100, 1, 1)), 0)!.Value.TotalSeconds, 3);
        Assert.Null(SoundLength.Read(new MemoryStream("not a sound"u8.ToArray()), 11));
    }

    [Fact]
    public void Mp3LengthComesFromItsBitrateOrItsVbrHeader()
    {
        // MPEG-1 layer III, 128 kbps, 44.1 kHz, stereo: 160000 bytes are 10 seconds
        byte[] header = [0xFF, 0xFB, 0x90, 0x00];
        var cbr = new byte[160000];
        header.CopyTo(cbr, 0);
        Assert.Equal(10.0, SoundLength.Read(new MemoryStream(cbr), cbr.Length)!.Value.TotalSeconds, 3);

        // Behind an ID3 tag of 100 bytes, which isn't counted
        byte[] tagged = [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 100, .. new byte[100], .. cbr];
        Assert.Equal(10.0, SoundLength.Read(new MemoryStream(tagged), tagged.Length)!.Value.TotalSeconds, 3);

        // A Xing header with 1000 frames of 1152 samples
        var vbr = new byte[4096];
        header.CopyTo(vbr, 0);
        "Xing"u8.CopyTo(vbr.AsSpan(36));
        vbr[43] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(vbr.AsSpan(44), 1000);
        Assert.Equal(1000 * 1152 / 44100.0, SoundLength.Read(new MemoryStream(vbr), vbr.Length)!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void BhopBadgesMusicMovesTheWayItWasFixedByHand()
    {
        if (FindCssMap("bhop_badges") is not { } original || FindMomentumMap("bhop_badges") is not { } fixedByHand)
            return;

        PortContext context = Open(original);
        MapSounds sounds = MapMusic.Scan(context);

        // All twelve tracks are minutes long and played by the map's soundscapes
        Assert.Equal(12, sounds.Music.Count());
        Assert.All(sounds.Sounds, x => Assert.Contains(x.Uses, use => use.StartsWith("soundscape badges.", StringComparison.Ordinal)));

        (List<string> move, List<string> remove) = MapMusic.Decide(sounds, choice: null);
        MusicChanges changes = MapMusic.Apply(context, move, remove);
        Assert.Equal(12, changes.Moved.Count);
        Assert.Empty(changes.Skipped);

        // The same paths and soundscapes as the copy fixed in Lumper
        PortContext reference = Open(fixedByHand);
        static List<string> Sounds(PortContext x) => x.ListPakFiles().Where(p => p.StartsWith("sound/", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(Sounds(reference), Sounds(context));
        Assert.Equal(reference.ReadPakFile("scripts/soundscapes_bhop_badges.txt"), context.ReadPakFile("scripts/soundscapes_bhop_badges.txt"));
        Assert.Empty(MapMusic.Scan(context).Sounds);
        Assert.Equal(12, MapMusic.Scan(context).InMusicFolder);
    }

    [Fact]
    public void SurfChristmasShortLoopsAreLeftToTheReview()
    {
        if (FindCssMap("surf_christmas") is not { } path)
            return;

        PortContext context = Open(path);
        MapSounds sounds = MapMusic.Scan(context);

        Assert.Equal(["sound/xmas/christmasbestsong.mp3"], sounds.Music.Select(x => x.Path));
        // The carol loops next to it are maybes, not moved on their own
        Assert.Contains("sound/xmas/christmas1.wav", sounds.Maybe.Select(x => x.Path));
        Assert.Contains("sound/white_christmas.wav", sounds.Maybe.Select(x => x.Path));
        Assert.Contains(sounds.Find("sound/xmas/christmas1.wav")!.Uses, x => x.StartsWith("ambient_generic", StringComparison.Ordinal) && x.EndsWith("positional, looping", StringComparison.Ordinal));

        // Picked in the review: one carol is music, the long song isn't, the ducks go
        var choice = new MapMusicChoice { Music = ["sound/xmas/christmas1.wav"], NotMusic = ["sound/xmas/christmasbestsong.mp3"], Removed = ["sound/ducks.wav"] };
        (List<string> move, List<string> remove) = MapMusic.Decide(sounds, choice);
        Assert.Equal(["sound/xmas/christmas1.wav"], move);
        Assert.Equal(["sound/ducks.wav"], remove);

        MusicChanges changes = MapMusic.Apply(context, move, remove);
        Assert.Equal([("sound/xmas/christmas1.wav", "sound/music/xmas/christmas1.wav")], changes.Moved);
        Assert.True(context.HasPakFile("sound/music/xmas/christmas1.wav"));
        Assert.False(context.HasPakFile("sound/xmas/christmas1.wav"));
        Assert.False(context.HasPakFile("sound/ducks.wav"));

        List<string> messages = context.OfClass("ambient_generic").Select(x => x["message"]).ToList();
        Assert.Contains("music/xmas/christmas1.wav", messages.Select(x => x.Replace('\\', '/').ToLowerInvariant()));
        Assert.DoesNotContain("xmas/christmas1.wav", messages.Select(x => x.Replace('\\', '/').ToLowerInvariant()));
    }

    [Fact]
    public void EveryWayAMapNamesASoundIsRewritten()
    {
        if (FindCssMap("surf_christmas") is not { } path)
            return;

        PortContext context = Open(path);
        context.EditPakFiles(zip =>
        {
            zip.AddEntry("sound/test/song.mp3", new MemoryStream(new byte[16]), closeStream: true);
            zip.AddEntry("scripts/test_sounds.txt", new MemoryStream("\"Test.Song\" { \"wave\" \"#test\\song.mp3\" } // not test/song.mp3.bak or mytest/song.mp3"u8.ToArray()), closeStream: true);
        });

        var sound = new Entity("ambient_generic");
        sound["message"] = "*test/song.mp3";
        context.AddEntity(sound);
        var trigger = new Entity("trigger_multiple");
        trigger["targetname"] = "song";
        trigger.connections.Add(new Entity.EntityConnection { name = "OnStartTouch", target = "!activator", action = "Command", param = "play sound/Test/Song.mp3", delay = 0, fireOnce = -1 });
        context.AddEntity(trigger);

        Assert.Equal(["ambient_generic, positional, looping", "trigger_multiple \"song\" OnStartTouch", "soundscript scripts/test_sounds.txt"], MapMusic.Scan(context).Find("sound/test/song.mp3")!.Uses);

        MusicChanges changes = MapMusic.Apply(context, ["sound/test/song.mp3"], []);
        Assert.Equal(3, changes.References);
        Assert.Equal("*music/test/song.mp3", sound["message"]);
        Assert.Equal("play sound/music/Test/Song.mp3", trigger.connections[0].param);
        Assert.Equal("\"Test.Song\" { \"wave\" \"#music/test\\song.mp3\" } // not test/song.mp3.bak or mytest/song.mp3", context.ReadPakFile("scripts/test_sounds.txt"));
    }

    [Fact]
    public void ASoundWhoseMusicPathIsTakenStays()
    {
        if (FindCssMap("surf_christmas") is not { } path)
            return;

        PortContext context = Open(path);
        context.EditPakFiles(zip => zip.AddEntry("sound/music/xmas/icecap.wav", new MemoryStream(new byte[16]), closeStream: true));

        MusicChanges changes = MapMusic.Apply(context, ["sound/xmas/icecap.wav"], []);
        Assert.Empty(changes.Moved);
        Assert.Single(changes.Skipped);
        Assert.True(context.HasPakFile("sound/xmas/icecap.wav"));
    }

    [Fact]
    public void TheReviewFlagsMusicLeftOutsideTheMusicFolder()
    {
        if (FindCssMap("bhop_badges") is not { } path)
            return;

        EntityReview review = EntityReview.AnalyzeFile(path);
        ReviewItem music = review.Items.Single(x => x.Tool == "Music");
        Assert.Equal(12, music.Pending);
        Assert.Equal(ReviewSeverity.Review, music.Severity);

        // Settled in the Sounds review: not brought up again
        var choice = new MapMusicChoice { NotMusic = MapMusic.Scan(Open(path)).Sounds.Select(x => x.Path).ToList() };
        Assert.False(EntityReview.AnalyzeFile(path, choice).Items.Single(x => x.Tool == "Music").IsPending);
    }

    [Fact]
    public async Task PortingAMapAppliesItsPicks()
    {
        if (FindCssMap("surf_christmas") is not { } path)
            return;

        using var temp = new TempFolder();
        string output = temp.Combine("surf_christmas.bsp");
        (PortJob job, MapPortState state) = PortJobFactory.CreateSourceMapJob(path, output, new MapPortOptions { Compress = false, Pack = false, Install = false });
        var context = new PortJobContext { Log = new ConsoleLog(), WorkDirectory = temp.Path };
        context.Set(state);
        context.Set(new MapMusicChoice { Music = ["sound/xmas/christmas1.wav"], Removed = ["sound/ducks.wav"] });

        PortJobResult result = await job.RunAsync(context);
        Assert.True(result.Succeeded, result.Error);

        PortContext ported = Open(output);
        Assert.True(ported.HasPakFile("sound/music/xmas/christmasbestsong.mp3"));
        Assert.True(ported.HasPakFile("sound/music/xmas/christmas1.wav"));
        Assert.False(ported.HasPakFile("sound/xmas/christmas1.wav"));
        Assert.False(ported.HasPakFile("sound/ducks.wav"));
        Assert.Contains(ported.OfClass("ambient_generic"), x => x["message"].Replace('\\', '/').Equals("music/xmas/christmas1.wav", StringComparison.OrdinalIgnoreCase));

        // What's left for the review: the maybes nobody listened to, not the picked ones
        ReviewItem maybe = context.Get<EntityReview>().Items.Single(x => x.Tool == "Possible music");
        Assert.True(maybe.IsPending);
        Assert.DoesNotContain("xmas/christmas1.wav", maybe.Message);
    }

    [Fact]
    public void ChoicesAreKeptPerMap()
    {
        using var temp = new TempFolder();
        string path = temp.Combine("music.json");
        var choices = new MusicChoices();
        choices.Set("surf_christmas", new MapMusicChoice { Music = ["sound/xmas/christmas1.wav"] });
        choices.Set("bhop_empty", new MapMusicChoice());
        choices.Save(path);

        MusicChoices loaded = MusicChoices.Load(path);
        Assert.True(loaded.Find("SURF_CHRISTMAS")!.IsMusic("Sound/Xmas/Christmas1.wav"));
        Assert.Null(loaded.Find("bhop_empty"));
        Assert.Empty(MusicChoices.TryLoad(temp.Combine("missing.json")).Maps);
    }
}
