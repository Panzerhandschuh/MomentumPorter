namespace MomentumPorter.Tests;

using MomentumPorter.Options;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
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
            KreedzClimbingDirectory = @"D:\KreedzClimbing",
            StudiomdlPath = @"C:\tools\studiomdl.exe",
        };

        settings.Save(path);
        PorterSettings loaded = PorterSettings.Load(path);

        Assert.Equal(settings.MomentumDirectory, loaded.MomentumDirectory);
        Assert.Equal(settings.KreedzClimbingDirectory, loaded.KreedzClimbingDirectory);
        Assert.Equal(settings.StudiomdlPath, loaded.StudiomdlPath);
        Assert.Null(PorterSettings.Load(temp.Combine("missing.json")).StudiomdlPath);
    }

    [Fact]
    public void KreedzClimbingIsTheOverrideElseSteams()
    {
        using var temp = new TempFolder();
        string install = temp.Combine("KZ");
        string content = Path.Combine(install, "kz");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "gameinfo.txt"), "\"GameInfo\" {}");

        // The install folder or the kz folder itself
        Assert.Equal(content, new PorterSettings { KreedzClimbingDirectory = install }.ResolveKreedzClimbingContentDirectory());
        Assert.Equal(content, new PorterSettings { KreedzClimbingDirectory = content }.ResolveKreedzClimbingContentDirectory());
        Assert.Null(new PorterSettings { KreedzClimbingDirectory = temp.Combine("nope") }.ResolveKreedzClimbingContentDirectory());

        // Without an override, KreedzClimbing/kz in the Steam libraries
        string steam = temp.Combine("Steam");
        string steamContent = Path.Combine(steam, "steamapps", "common", "KreedzClimbing", "kz");
        Directory.CreateDirectory(steamContent);
        Assert.Equal(steamContent, new PorterSettings().ResolveKreedzClimbingContentDirectory(new SteamLocator(steam)));
        Assert.Null(new PorterSettings().ResolveKreedzClimbingContentDirectory(new SteamLocator(steamRoot: null)));
    }

    [Fact]
    public void StudiomdlIsTheOverrideElseMomentums()
    {
        using var temp = new TempFolder();
        string install = temp.CreateMomentumInstall();
        string studiomdl = Path.Combine(install, "bin", "win64", "studiomdl.exe");

        Assert.Equal(@"C:\tools\studiomdl.exe", new PorterSettings { StudiomdlPath = @"C:\tools\studiomdl.exe", MomentumDirectory = install }.ResolveStudiomdlPath());
        Assert.Null(new PorterSettings { MomentumDirectory = install }.ResolveStudiomdlPath());

        Directory.CreateDirectory(Path.GetDirectoryName(studiomdl)!);
        File.WriteAllBytes(studiomdl, []);
        Assert.Equal(studiomdl, new PorterSettings { MomentumDirectory = install }.ResolveStudiomdlPath());
        Assert.Equal(studiomdl, new PorterSettings { MomentumDirectory = Path.Combine(install, "momentum") }.ResolveStudiomdlPath());
        Assert.Null(new PorterSettings { MomentumDirectory = temp.Combine("nope") }.ResolveStudiomdlPath());
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

        page.KreedzClimbingDirectory = "  ";
        page.StudiomdlPath = @"C:\tools\studiomdl.exe";

        PorterSettings saved = PorterSettings.Load(path);
        Assert.Null(saved.KreedzClimbingDirectory);
        Assert.Equal(@"C:\tools\studiomdl.exe", saved.StudiomdlPath);
        Assert.StartsWith(@"C:\tools\studiomdl.exe doesn't exist", page.StudiomdlStatus);
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
