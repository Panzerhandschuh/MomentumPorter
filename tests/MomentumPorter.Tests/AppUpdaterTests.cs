namespace MomentumPorter.Tests;

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MomentumPorter.Updates;
using Xunit;

public class AppUpdaterTests
{
    // Trimmed from api.github.com/repos/{owner}/{repo}/releases/latest
    private const string ReleaseJson = """
        {
          "html_url": "https://github.com/Panzerhandschuh/MomentumPorter/releases/tag/v1.2.0",
          "tag_name": "v1.2.0",
          "name": "Better weapons",
          "draft": false,
          "prerelease": false,
          "body": "- Fixed things",
          "assets": [
            {
              "name": "MomentumPorter-linux-x64-v1.2.0.7z",
              "size": 10,
              "digest": null,
              "browser_download_url": "https://example.com/linux.7z"
            },
            {
              "name": "MomentumPorter-win-x64-v1.2.0.7z",
              "size": 1234,
              "digest": "sha256:9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08",
              "browser_download_url": "https://example.com/win.7z"
            }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0.0")]
    [InlineData("v1.2.0-beta.1", "1.2.0")]
    [InlineData("v1.2.0.7", "1.2.0")]
    public void ParsesTags(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), AppUpdater.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("release-1")]
    public void IgnoresTagsThatArentVersions(string? tag)
    {
        Assert.Null(AppUpdater.ParseVersion(tag));
    }

    [Fact]
    public void ReadsTheWindowsArchive()
    {
        AppRelease release = AppUpdater.ParseRelease(ReleaseJson)!;

        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("Better weapons", release.Name);
        Assert.Equal("- Fixed things", release.Notes);
        Assert.Equal("MomentumPorter-win-x64-v1.2.0.7z", release.AssetName);
        Assert.Equal(new Uri("https://example.com/win.7z"), release.DownloadUrl);
        Assert.Equal(1234, release.Size);
        Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", release.Sha256);
        Assert.Equal("https://github.com/Panzerhandschuh/MomentumPorter/releases/tag/v1.2.0", release.PageUrl);
    }

    [Fact]
    public void SkipsReleasesItCantInstall()
    {
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson.Replace("\"prerelease\": false", "\"prerelease\": true")));
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson.Replace("\"draft\": false", "\"draft\": true")));
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson.Replace("win-x64", "osx-arm64")));
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson.Replace("\"v1.2.0\"", "\"nightly\"")));
    }

    [Fact]
    public async Task OffersOnlyNewerReleases()
    {
        using var client = new HttpClient(new FakeHandler(_ => Json(ReleaseJson)));

        Assert.NotNull(await AppUpdater.FindUpdateAsync(client, new Version(1, 1, 9), "https://feed.test/latest"));
        Assert.NotNull(await AppUpdater.FindUpdateAsync(client, new Version(0, 9, 0, 0), "https://feed.test/latest"));
        // An assembly's four part version is the same as the tag's three
        Assert.Null(await AppUpdater.FindUpdateAsync(client, new Version(1, 2, 0, 0), "https://feed.test/latest"));
        Assert.Null(await AppUpdater.FindUpdateAsync(client, new Version(1, 10, 0), "https://feed.test/latest"));
    }

    [Fact]
    public async Task NoReleasesIsNoUpdate()
    {
        using var client = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await AppUpdater.FindUpdateAsync(client, new Version(1, 0, 0), "https://feed.test/latest"));
    }

    [Fact]
    public async Task DownloadsAndChecksTheHash()
    {
        using var temp = new TempFolder();
        byte[] content = Encoding.UTF8.GetBytes("pretend this is an archive");
        using var client = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }));
        var release = new AppRelease(new Version(1, 2, 0), "1.2.0", "", "", "a.7z", new Uri("https://example.com/a.7z"), content.Length,
            Convert.ToHexStringLower(SHA256.HashData(content)));
        var reported = new List<double>();

        string path = temp.Combine("sub", "a.7z");
        await AppUpdater.DownloadAsync(client, release, path, new SyncProgress(reported.Add));

        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.Equal(1, reported[^1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdater.DownloadAsync(client, release with { Sha256 = new string('0', 64) }, path));
        await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdater.DownloadAsync(client, release with { Size = content.Length + 1 }, path));
    }

    [Fact]
    public async Task SwapsTheNewFilesIn()
    {
        using var temp = new TempFolder();
        string install = temp.Combine("MomentumPorter");
        Write(install, AppUpdater.UiExecutableName, "old ui");
        Write(install, "MomentumPorter-CLI.exe", "old cli");
        Write(install, Path.Combine("bspPack", "bspPack.exe"), "old bspPack");
        Write(install, "notes I keep here.txt", "mine");

        string archive = temp.Combine("release.zip");
        CreateRelease(archive, ("MomentumPorter/" + AppUpdater.UiExecutableName, "new ui"), ("MomentumPorter/MomentumPorter-CLI.exe", "new cli"),
            ("MomentumPorter/bspPack/bspPack.exe", "new bspPack"), ("MomentumPorter/Q3Content/new.txt", "added"));

        string staging = Path.Combine(install, AppUpdater.StagingFolderName);
        string extracted = AppUpdater.Extract(archive, Path.Combine(staging, "files"));
        Assert.Equal(Path.Combine(staging, "files", "MomentumPorter"), extracted);

        // The running program is renamed, not overwritten: it stays open, though it can be renamed
        string oldUi = Path.Combine(install, AppUpdater.UiExecutableName + ".updater-old");
        await using (new FileStream(Path.Combine(install, AppUpdater.UiExecutableName), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            AppUpdater.Apply(extracted, install);

        // Until the old program has exited, its file can't be deleted
        await using (new FileStream(oldUi, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(AppUpdater.CleanUp(install));

        Assert.Equal("new ui", Read(install, AppUpdater.UiExecutableName));
        Assert.Equal("new cli", Read(install, "MomentumPorter-CLI.exe"));
        Assert.Equal("new bspPack", Read(install, Path.Combine("bspPack", "bspPack.exe")));
        Assert.Equal("added", Read(install, Path.Combine("Q3Content", "new.txt")));
        Assert.Equal("mine", Read(install, "notes I keep here.txt"));

        Assert.True(await AppUpdater.CleanUpAsync(install, TimeSpan.FromSeconds(5)));
        Assert.False(Directory.Exists(staging));
        Assert.Empty(Directory.EnumerateFiles(install, "*.updater-old", SearchOption.AllDirectories));
        Assert.Equal(5, Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void PutsTheOldFilesBackWhenOneCantBeReplaced()
    {
        using var temp = new TempFolder();
        string install = temp.Combine("MomentumPorter");
        Write(install, AppUpdater.UiExecutableName, "old ui");
        Write(install, "MomentumPorter-CLI.exe", "old cli");
        Write(install, "README.md", "old readme");

        string source = temp.Combine("new");
        Write(source, AppUpdater.UiExecutableName, "new ui");
        Write(source, "MomentumPorter-CLI.exe", "new cli");
        Write(source, "README.md", "new readme");
        Write(source, Path.Combine("Assets", "added.txt"), "added");

        // Open without sharing delete, so it can't be renamed
        using (File.Open(Path.Combine(install, "MomentumPorter-CLI.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => AppUpdater.Apply(source, install));

        Assert.Equal("old ui", Read(install, AppUpdater.UiExecutableName));
        Assert.Equal("old cli", Read(install, "MomentumPorter-CLI.exe"));
        Assert.Equal("old readme", Read(install, "README.md"));
        Assert.False(File.Exists(Path.Combine(install, "Assets", "added.txt")));
        Assert.Empty(Directory.EnumerateFiles(install, "*.updater-old", SearchOption.AllDirectories));
    }

    [Fact]
    public void ExtractsTheReleaseScriptsSolid7z()
    {
        // publish-release.ps1's archive: solid LZMA2, with the BCJ filter on executables
        string sevenZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
        string exe = Path.Combine(AppContext.BaseDirectory, "bspPack", "bspPack.exe");
        if (!File.Exists(sevenZip) || !File.Exists(exe))
            return;

        using var temp = new TempFolder();
        string folder = temp.Combine("pack", "MomentumPorter");
        Directory.CreateDirectory(Path.Combine(folder, "bspPack"));
        File.Copy(exe, Path.Combine(folder, AppUpdater.UiExecutableName));
        File.Copy(exe, Path.Combine(folder, "bspPack", "bspPack.exe"));
        File.WriteAllText(Path.Combine(folder, "README.md"), "readme");

        string archive = temp.Combine("release.7z");
        using (Process process = Process.Start(new ProcessStartInfo(sevenZip, ["a", "-t7z", "-mx=7", archive, "MomentumPorter"])
               {
                   WorkingDirectory = temp.Combine("pack"),
                   RedirectStandardOutput = true,
                   UseShellExecute = false,
               })!)
        {
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        string extracted = AppUpdater.Extract(archive, temp.Combine("out"));

        Assert.Equal(File.ReadAllBytes(exe), File.ReadAllBytes(Path.Combine(extracted, AppUpdater.UiExecutableName)));
        Assert.Equal(File.ReadAllBytes(exe), File.ReadAllBytes(Path.Combine(extracted, "bspPack", "bspPack.exe")));
        Assert.Equal("readme", File.ReadAllText(Path.Combine(extracted, "README.md")));
    }

    private static void CreateRelease(string path, params (string Name, string Content)[] files)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in files)
        {
            using StreamWriter writer = new(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private static void Write(string folder, string relative, string content)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string folder, string relative) => File.ReadAllText(Path.Combine(folder, relative));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    // Progress<T> posts to the thread pool; this reports straight away
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
