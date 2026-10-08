namespace MomentumPorter.Tests;

using MomentumPorter.BspConvert;
using MomentumPorter.Detection;
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
}
