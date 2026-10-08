namespace MomentumPorter.Tests;

using MomentumPorter.Options;
using MomentumPorter.Settings;
using MomentumPorter.UI.Services;
using MomentumPorter.UI.ViewModels.Pages;
using Xunit;

public class SettingsTests
{
    [Fact]
    public void SettingsRoundTrip()
    {
        using var temp = new TempFolder();
        string path = temp.Combine("sub", "settings.json");
        var settings = new PorterSettings
        {
            MomentumDirectory = @"D:\Momentum",
            BspPackPath = @"C:\bspPack\bspPack.exe",
            PrePackCommand = "python x.py \"{resourceconfig}\"",
            InstallByDefault = false,
        };

        settings.Save(path);
        PorterSettings loaded = PorterSettings.Load(path);

        Assert.Equal(settings.MomentumDirectory, loaded.MomentumDirectory);
        Assert.Equal(settings.BspPackPath, loaded.BspPackPath);
        Assert.Equal(settings.PrePackCommand, loaded.PrePackCommand);
        Assert.False(loaded.InstallByDefault);
        Assert.True(PorterSettings.Load(temp.Combine("missing.json")).InstallByDefault);
    }

    [Fact]
    public void ACorruptSettingsFileFallsBackToDefaults()
    {
        using var temp = new TempFolder();
        string path = temp.Combine("settings.json");
        File.WriteAllText(path, "{ not json");

        SettingsService service = SettingsService.Load(path);

        Assert.NotNull(service.LoadError);
        Assert.Null(service.Settings.MomentumDirectory);
    }

    [Fact]
    public void TheMomentumOverrideMayPointAtTheInstallContentOrMapsFolder()
    {
        using var temp = new TempFolder();
        string install = temp.CreateMomentumInstall();
        string content = Path.Combine(install, "momentum");
        string maps = Path.Combine(content, "maps");

        foreach (string choice in new[] { install, content, maps })
            Assert.Equal(maps, new PorterSettings { MomentumDirectory = choice }.ResolveMomentumMapsDirectory());

        Assert.Null(new PorterSettings { MomentumDirectory = temp.Combine("nope") }.ResolveMomentumMapsDirectory());
    }

    [Fact]
    public void PresetsKeepDefaultsForMissingProperties()
    {
        using var stream = new MemoryStream("{ \"Game\": \"kzmod\" }"u8.ToArray());
        MapPortOptions options = OptionsPreset.Load<MapPortOptions>(stream);

        Assert.Equal("kzmod", options.Game);
        Assert.True(options.Pack);
        Assert.Throws<InvalidDataException>(() => OptionsPreset.Load<MapPortOptions>(new MemoryStream("nope"u8.ToArray())));
    }

    [Fact]
    public void SettingsPageSavesEveryChange()
    {
        using var temp = new TempFolder();
        string path = temp.Combine("settings.json");
        var page = new SettingsPageViewModel(SettingsService.Load(path), new NoDialogs(), action => action());

        page.BspPackPath = "  ";
        page.PrePackCommand = "echo hi";
        page.InstallByDefault = false;

        PorterSettings saved = PorterSettings.Load(path);
        Assert.Null(saved.BspPackPath);
        Assert.Equal("echo hi", saved.PrePackCommand);
        Assert.False(saved.InstallByDefault);
        Assert.Null(page.SaveError);
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple, params FileFilter[] filters) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedName, params FileFilter[] filters) => Task.FromResult<string?>(null);
    }
}
