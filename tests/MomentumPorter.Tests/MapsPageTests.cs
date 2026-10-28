namespace MomentumPorter.Tests;

using System.Reactive.Linq;
using MomentumPorter.Detection;
using MomentumPorter.Fixes;
using MomentumPorter.Options;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using MomentumPorter.Steps;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels;
using MomentumPorter.UI.ViewModels.Pages;
using MomentumPorter.UI.ViewModels.Shared;
using Xunit;

[Collection(BspPackCollection.Name)]
public class MapsPageTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public bool ConfirmAnswer { get; set; }

        public List<string> Messages { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(ConfirmAnswer);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FileFilter[] filters) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, params FileFilter[] filters) => Task.FromResult<string?>(null);

        /// <summary>Answers the Sounds review: changes its picks and returns whether to save them.</summary>
        public Func<MapSoundsViewModel, bool>? SoundsReview { get; set; }

        public Task<bool> ShowMapSoundsAsync(MapSoundsViewModel sounds) => Task.FromResult(SoundsReview?.Invoke(sounds) ?? false);
    }

    private static readonly Action<Action> Inline = action => action();

    private static MainWindowViewModel CreateWindow(TempFolder temp, Action<PorterSettings>? configure = null)
    {
        var settings = SettingsService.Load(temp.Combine("settings.json"));
        configure?.Invoke(settings.Settings);
        return new MainWindowViewModel(settings, new FakeDialogs(), new JobRunnerService(Inline), Inline);
    }

    private static string WriteFakeMap(TempFolder temp, string name, byte[] header)
    {
        string path = temp.Combine(name);
        File.WriteAllBytes(path, [.. header, 0, 0, 0, 0, 0, 0, 0, 0]);
        return path;
    }

    private static readonly byte[] Quake3Header = [.. "IBSP"u8, 46, 0, 0, 0];
    private static readonly byte[] SourceHeader = [.. "VBSP"u8, 20, 0, 0, 0];

    [Fact]
    public void DroppedMapsGoToTheQueueAndOtherFilesGetAMessage()
    {
        using var temp = new TempFolder();
        MainWindowViewModel window = CreateWindow(temp);
        window.SelectedPage = window.Settings;
        string q3 = WriteFakeMap(temp, "q3dm17.bsp", Quake3Header);
        string model = temp.Combine("v_ak47.mdl");
        File.WriteAllText(model, "IDST");

        window.OpenFiles([q3, model]);

        Assert.Same(window.Maps, window.SelectedPage);
        MapQueueItemViewModel item = Assert.Single(window.Maps.Queue);
        Assert.Equal(InputKind.Quake3Bsp, item.Kind);
        Assert.Equal("Quake 3", item.Game);
        Assert.True(item.IsSupported);
        Assert.True(item.UsesBspConvert);
        Assert.Null(item.Note);
        Assert.Contains("Added 1 map", window.StatusMessage);
        Assert.Contains("v_ak47.mdl is a model", window.StatusMessage);

        // Dropping the same queued map again doesn't add it twice
        window.OpenFiles([q3]);
        Assert.Single(window.Maps.Queue);
        Assert.Contains("already queued", window.StatusMessage);
    }

    [Fact]
    public void DroppedFoldersAddTheMapsInThem()
    {
        using var temp = new TempFolder();
        MainWindowViewModel window = CreateWindow(temp);
        WriteFakeMap(temp, "a.bsp", Quake3Header);
        WriteFakeMap(temp, "b.bsp", Quake3Header);
        File.WriteAllText(temp.Combine("readme.txt"), "hi");

        window.OpenFiles([temp.Path]);

        Assert.Equal(["a.bsp", "b.bsp"], window.Maps.Queue.Select(x => x.Title));
    }

    [Fact]
    public async Task NonSourceMapsRunThroughBspConvert()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp).Maps;
        maps.Options.Install = false;
        maps.AddFiles([WriteFakeMap(temp, "q3dm17.bsp", Quake3Header)]);
        Assert.True(maps.HasQueued);
        Assert.True(maps.Queue[0].UsesBspConvert);
        Assert.Contains(maps.Prepare(maps.Queue[0]).Job.Steps, x => x is MomentumPorter.BspConvert.BspConvertStep);

        // A header-only file isn't a map BSPConvert can read, so the job fails rather than being skipped
        await maps.RunAsync();

        Assert.Equal(QueueStatus.Failed, maps.Queue[0].Status);
        Assert.False(maps.HasQueued);

        maps.RequeueAll();
        Assert.True(maps.HasQueued);
        maps.ClearFinished();
        Assert.Single(maps.Queue);
    }

    [Fact]
    public async Task ABrokenSourceMapFailsWithTheStepsMessage()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp, s => s.OutputDirectory = temp.Combine("out")).Maps;
        maps.AddFiles([WriteFakeMap(temp, "kz_broken.bsp", SourceHeader)]);

        await maps.RunAsync();

        MapQueueItemViewModel item = maps.Queue[0];
        Assert.Equal(QueueStatus.Failed, item.Status);
        Assert.StartsWith("Load map failed", item.StatusMessage);
        Assert.False(Directory.Exists(temp.Combine("out")));
    }

    [Fact]
    public void JobsUseACopyOfTheOptionsAndTheConfiguredFolders()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp, s => s.WorkDirectory = temp.Combine("work")).Maps;
        MapQueueItemViewModel item = maps.AddFiles([WriteFakeMap(temp, "kz_x.bsp", SourceHeader)])[0];
        maps.Options.Install = false;
        maps.Options.Pack = true;

        item.Start();
        PreparedJob job = maps.Prepare(item);
        var context = new PortJobContext { Log = NullLog.Instance, WorkDirectory = job.WorkDirectory };
        job.Configure(context);

        Assert.StartsWith(temp.Combine("work"), job.WorkDirectory);
        Assert.Equal(["Load map", "Convert entities", "Fix entities", "Remove mounted assets", "Move music", "Fix materials", "Check entities", "Write map", "Pack assets"], job.Job.Steps.Select(x => x.Name));
        MapPortState state = context.Get<MapPortState>();
        Assert.Equal(Path.Combine(job.WorkDirectory, "kz_x.bsp"), state.OutputPath);
        Assert.NotSame(maps.Options, state.Options);
        Assert.NotNull(context.TryGet<IUserPrompts>());
        Assert.Equal(temp.Combine("work"), context.Get<PorterSettings>().WorkDirectory);
    }

    [Fact]
    public void PresetsRoundTrip()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp).Maps;
        maps.Options.Compress = false;
        maps.Options.Pack = false;
        maps.Options.Install = false;

        using var stream = new MemoryStream();
        maps.SavePreset(stream);
        maps.SetOptions(new MapPortOptions());
        Assert.True(maps.Options.Compress);

        stream.Position = 0;
        maps.LoadPreset(stream);

        Assert.False(maps.Options.Compress);
        // The page always packs KZ maps, installs and replaces, whatever the preset says; packing isn't on the form
        Assert.True(maps.Options.Pack);
        Assert.Null(maps.OptionsForm.Find(nameof(MapPortOptions.Pack)));
        Assert.True(maps.Options.Install);
        Assert.True(maps.Options.Overwrite);
    }

    [Fact]
    public void NextStepsTabShowsADotUntilTheStepsAreLookedAt()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp).Maps;
        IReadOnlyList<MapQueueItemViewModel> items = maps.AddFiles([WriteFakeMap(temp, "kz_a.bsp", SourceHeader), WriteFakeMap(temp, "kz_b.bsp", SourceHeader)]);
        Assert.Same(items[0], maps.SelectedItem);
        Assert.False(maps.NextStepsUnread);

        // Steps land on the selected map while the log is showing: the dot comes on
        items[0].SetNextSteps(MomentumPorter.Review.SubmissionSteps.For("kz_a", null, false, null));
        Assert.True(items[0].NextStepsUnread);
        Assert.True(maps.NextStepsUnread);

        // Opening the tab reads them
        maps.BottomTab = MapsPageViewModel.NextStepsTab;
        Assert.False(items[0].NextStepsUnread);
        Assert.False(maps.NextStepsUnread);

        // Steps for another map don't light the dot until that map is selected, and selecting it while the tab is open reads them at once
        items[1].SetNextSteps(MomentumPorter.Review.SubmissionSteps.For("kz_b", null, false, null));
        Assert.False(maps.NextStepsUnread);
        maps.BottomTab = 0;
        maps.SelectedItem = items[1];
        Assert.True(maps.NextStepsUnread);
        maps.BottomTab = MapsPageViewModel.NextStepsTab;
        Assert.False(items[1].NextStepsUnread);

        // Running the map again clears its steps
        items[1].Start();
        Assert.Empty(items[1].NextSteps);
        Assert.False(items[1].NextStepsUnread);
    }

    [Fact]
    public void MapsAlwaysInstallIntoMomentum()
    {
        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp).Maps;

        Assert.True(maps.Options.Install);
        Assert.True(maps.Options.Overwrite);
        Assert.Null(maps.Options.Game);
        Assert.Null(maps.OptionsForm.Find(nameof(MapPortOptions.Install)));
        Assert.Null(maps.OptionsForm.Find(nameof(MapPortOptions.Overwrite)));
        Assert.Null(maps.OptionsForm.Find(nameof(MapPortOptions.Game)));
        Assert.Null(maps.OptionsForm.Find(nameof(MapPortOptions.GameDirectory)));
    }

    /// <summary>
    /// The whole Maps page flow on a real KZ map: port and pack, then deliver to the output folder.
    /// </summary>
    [Fact]
    public async Task PortsAKzMapToTheOutputFolder()
    {
        string? input = KzMaps.Find("kz_dance");
        if (input is null || !File.Exists(MomentumPorter.Tools.BspPackTool.BundledPath))
            return;

        using var temp = new TempFolder();
        MapsPageViewModel maps = CreateWindow(temp, s => s.OutputDirectory = temp.Combine("out")).Maps;
        maps.Options.Install = false;
        maps.AddFiles([input]);

        await maps.RunAsync();

        MapQueueItemViewModel item = maps.Queue[0];
        Assert.True(item.Status == QueueStatus.Done, item.StatusMessage + "\n" + item.Log);
        Assert.Equal(temp.Combine("out", "kz_dance.bsp"), item.OutputPath);
        Assert.True(File.Exists(item.OutputPath));
        Assert.True(item.HasReport);
        Assert.Contains(item.Log.Lines, x => x.Text.StartsWith("Packed kz_dance.bsp"));
        Assert.Null(item.InstalledPath);
    }
    [Fact]
    public async Task TheSoundsReviewKeepsItsPicksForThePort()
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        string? map = css?.ContentDirectory is { } content ? Path.Combine(content, "maps", "bhop_badges.bsp") : null;
        if (map is null || !File.Exists(map))
            return;

        using var temp = new TempFolder();
        var dialogs = new FakeDialogs();
        var settings = SettingsService.Load(temp.Combine("settings.json"));
        var page = new MapsPageViewModel(settings, dialogs, new JobRunnerService(Inline), Inline);

        MapQueueItemViewModel item = Assert.Single(page.AddFiles([map]));
        await item.DetectGameAsync(Inline);
        Assert.Equal("Sounds: 12 music", item.SoundsSummary);

        // Untick one track and take another out
        dialogs.SoundsReview = review =>
        {
            Assert.Equal(12, review.Rows.Count(x => x.IsMusic));
            // One list, longest first
            Assert.Equal(review.Rows.OrderByDescending(x => x.Sound.Length).Select(x => x.Path), review.Rows.Select(x => x.Path));
            Assert.Equal(50, review.Volume);
            review.Volume = 20;
            review.Rows.Single(x => x.Name == "badges/underwater1.mp3").IsMusic = false;
            review.Rows.Single(x => x.Name == "badges/rush_hour.mp3").IsRemoved = true;
            return true;
        };
        await page.ReviewSoundsAsync(item);

        Assert.Equal("Sounds: 10 music, 1 other, 1 removed", item.SoundsSummary);
        MapMusicChoice choice = MusicChoices.Load(temp.Combine(MusicChoices.FileName)).Find("bhop_badges")!;
        Assert.True(choice.IsNotMusic("sound/badges/underwater1.mp3"));
        Assert.True(choice.IsRemoved("sound/badges/rush_hour.mp3"));
        Assert.Equal(10, choice.Music.Count);
        Assert.Equal(0.2, SettingsService.Load(temp.Combine("settings.json")).Settings.PreviewVolume, 3);

        // Cancelled: nothing changes
        dialogs.SoundsReview = review =>
        {
            review.Rows[0].IsRemoved = true;
            review.Volume = 35;
            return false;
        };
        await page.ReviewSoundsAsync(item);
        Assert.Equal(10, page.FindMusicChoice("bhop_badges")!.Music.Count);
        // The volume is kept even so
        Assert.Equal(0.35, SettingsService.Load(temp.Combine("settings.json")).Settings.PreviewVolume, 3);
    }

    [Fact]
    public async Task TheSoundsPreviewTimelineFollowsAndSeeks()
    {
        // The header guessed the length a little short, as it can for a VBR MP3
        var sound = new PackedSound("sound/map/track.mp3", 1000, TimeSpan.FromSeconds(170), ["ambient_generic, plays everywhere"], MusicGuess.Music, "long");
        var preview = new FakeSoundPreview();
        var scheduler = new System.Reactive.Concurrency.HistoricalScheduler();
        var review = new MapSoundsViewModel("map", new MapSounds([sound], 0), null, false, _ => [1, 2, 3], preview, 0.5, scheduler);
        MapSoundRowViewModel row = Assert.Single(review.Rows);
        Assert.Equal(0.5, preview.Volume);

        await row.PlayCommand.Execute();
        Assert.True(row.IsPlaying);
        Assert.True(review.IsPreviewing);
        Assert.Equal("map/track.mp3", review.NowPlaying);
        Assert.Equal(170, review.Duration);

        // It follows the player, with the player's length
        preview.Now = new PreviewPosition(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), false);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200));
        Assert.Equal(180, review.Duration);
        Assert.Equal(10, review.Position);
        Assert.Equal("0:10 / 3:00", review.PositionText);

        // Moving the slider skips ahead, but not past the end
        review.Position = 95;
        review.Position = 500;
        Assert.Equal([TimeSpan.FromSeconds(95), TimeSpan.FromSeconds(180)], preview.Seeks);
        Assert.Equal(180, review.Position);

        // At the end the button goes back to Play and the timeline hides
        preview.Now = preview.Now!.Value with { Finished = true };
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200));
        Assert.False(row.IsPlaying);
        Assert.False(review.IsPreviewing);
        Assert.Null(preview.Now);

        // Nothing to seek once it's stopped
        review.Position = 30;
        Assert.Equal(2, preview.Seeks.Count);
        Assert.Equal(0, review.Position);
    }

    [Fact]
    public async Task TheSoundsPreviewMutesAndUnmutes()
    {
        var sound = new PackedSound("sound/map/track.mp3", 1000, TimeSpan.FromMinutes(3), [], MusicGuess.Music, "long");
        var preview = new FakeSoundPreview();
        var review = new MapSoundsViewModel("map", new MapSounds([sound], 0), null, false, _ => [1], preview, 0.3, new System.Reactive.Concurrency.HistoricalScheduler());

        await review.MuteCommand.Execute();
        Assert.True(review.IsMuted);
        Assert.Equal(0, preview.Volume);

        // Back to where it was
        await review.MuteCommand.Execute();
        Assert.False(review.IsMuted);
        Assert.Equal(30, review.Volume);
        Assert.Equal(0.3, preview.Volume, 3);

        // Dragged all the way down instead, unmuting goes to the default
        review.Volume = 0;
        Assert.True(review.IsMuted);
        await review.MuteCommand.Execute();
        Assert.Equal(50, review.Volume);
    }
}
