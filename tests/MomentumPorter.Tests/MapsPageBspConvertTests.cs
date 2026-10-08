namespace MomentumPorter.Tests;

using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
using MomentumPorter.Options;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Pages;
using Xunit;

public class MapsPageBspConvertTests
{
    private sealed class NoDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FileFilter[] filters) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, params FileFilter[] filters) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void Quake3ArchivesGetABspConvertJob()
    {
        string root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string pk3 = Path.Combine(root, "somemap.pk3");
        File.WriteAllBytes(pk3, []);
        try
        {
            SettingsService settings = SettingsService.Load(Path.Combine(root, "settings.json"));
            settings.Settings.OutputDirectory = Path.Combine(root, "out");
            var page = new MapsPageViewModel(settings, new NoDialogs(), new JobRunnerService(a => a()), a => a());
            page.Options.Install = false;

            MapQueueItemViewModel item = page.AddFiles([pk3]).Single();
            Assert.Equal(InputKind.Pk3Archive, item.Kind);
            Assert.True(item.IsSupported);
            Assert.True(item.UsesBspConvert);

            PreparedJob prepared = page.Prepare(item);

            Assert.Contains(prepared.Job.Steps, x => x is BspConvertStep);
            Assert.Contains(prepared.Job.Steps, x => x is CopyConvertedFilesStep);
            Assert.True(Directory.Exists(prepared.WorkDirectory));
            Directory.Delete(prepared.WorkDirectory, recursive: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OptionsShowOnlyForTheQueuedKindsOfMap()
    {
        string root = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string pk3 = Path.Combine(root, "q3map.pk3");
        File.WriteAllBytes(pk3, []);
        string goldSrc = Path.Combine(root, "gsmap.bsp");
        File.WriteAllBytes(goldSrc, [30, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            SettingsService settings = SettingsService.Load(Path.Combine(root, "settings.json"));
            var page = new MapsPageViewModel(settings, new NoDialogs(), new JobRunnerService(a => a()), a => a());
            IEnumerable<string> Groups() => page.BspConvertOptionsForm.Groups.Select(x => x.Name);
            IEnumerable<string> MapFields() => page.OptionsForm.Groups.SelectMany(x => x.Fields.Concat(x.AdvancedFields)).Select(x => x.Name);

            // Empty queue: everything, minus what the map options already cover
            Assert.True(page.ShowBspConvertOptions);
            Assert.Contains(BspConvertOptions.GoldSrcGroup, Groups());
            Assert.Contains(BspConvertOptions.Quake3Group, Groups());
            Assert.Contains(nameof(MapPortOptions.Game), MapFields());
            Assert.Null(page.BspConvertOptionsForm.Find(nameof(BspConvertOptions.Overwrite)));
            Assert.Null(page.BspConvertOptionsForm.Find(nameof(BspConvertOptions.OutputDirectory)));

            page.AddFiles([pk3]);
            Assert.True(page.ShowBspConvertOptions);
            Assert.Equal([BspConvertOptions.GeneralGroup, BspConvertOptions.Quake3Group, BspConvertOptions.AnimatedTexturesGroup], Groups());
            Assert.DoesNotContain(nameof(MapPortOptions.Game), MapFields());
            Assert.Contains(nameof(MapPortOptions.Install), MapFields());
            Assert.Contains(nameof(MapPortOptions.Overwrite), MapFields());

            page.AddFiles([goldSrc]);
            Assert.Contains(BspConvertOptions.GoldSrcGroup, Groups());

            page.Remove(page.Queue[0]);
            Assert.Equal([BspConvertOptions.GeneralGroup, BspConvertOptions.GoldSrcGroup], Groups());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConvertedMapsOverwriteByDefault()
    {
        Assert.True(new MapPortOptions().Overwrite);
        Assert.True(new BspConvertOptions().Overwrite);
    }
}
