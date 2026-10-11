namespace MomentumPorter.Updates;

using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

/// <summary>A published release newer than the running porter, and the archive of it to download.</summary>
/// <param name="Sha256">The archive's SHA-256 as GitHub reports it (lowercase hex), or null if it doesn't</param>
public sealed record AppRelease(
    Version Version,
    string Name,
    string Notes,
    string PageUrl,
    string AssetName,
    Uri DownloadUrl,
    long Size,
    string? Sha256);

/// <summary>
/// Updates the porter from its GitHub releases. A release is the archive tools/publish-release.ps1 builds (the
/// MomentumPorter folder 7-zipped), attached to a release tagged with its version (v1.2.0). Updating extracts it next
/// to the running program, swaps the new files in (the running executables can't be overwritten on Windows, but they
/// can be renamed out of the way) and the next start deletes the old files.
/// </summary>
public static class AppUpdater
{
    /// <summary>The latest release that isn't a draft or prerelease.</summary>
    public const string DefaultFeedUrl = "https://api.github.com/repos/Panzerhandschuh/MomentumPorter/releases/latest";

    /// <summary>
    /// Points the updater at another feed with the same format as <see cref="DefaultFeedUrl"/>, for testing updates
    /// against a local server.
    /// </summary>
    public const string FeedUrlVariable = "MOMENTUMPORTER_UPDATE_FEED";

    /// <summary>The program that's started again after an update; a release's files are found by it too.</summary>
    public const string UiExecutableName = "MomentumPorter-UI.exe";

    /// <summary>Where updates are downloaded and extracted, inside the install folder.</summary>
    public const string StagingFolderName = ".update";

    // Lists the files moved aside, for the next start to delete
    private const string CleanupListName = "old-files.txt";
    private const string OldSuffix = ".updater-old";

    /// <summary>The running porter's version, 0.0.0 for a development build.</summary>
    public static Version CurrentVersion => Normalize(typeof(AppUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static bool IsDevelopmentBuild(Version version) => Normalize(version) == new Version(0, 0, 0);

    /// <summary>
    /// The porter is running as a published single-file executable, the only layout a release's files can replace
    /// (a build output's DLLs would be left behind).
    /// </summary>
    public static bool IsSingleFileApp => string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);

    public static string FeedUrl => Environment.GetEnvironmentVariable(FeedUrlVariable) is { Length: > 0 } url ? url : DefaultFeedUrl;

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        // GitHub's API turns away requests without a user agent
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MomentumPorter", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    /// The latest release if it's newer than <paramref name="current"/> and has a Windows archive, otherwise null.
    /// A repository without releases (GitHub answers 404) has no update.
    /// </summary>
    public static async Task<AppRelease?> FindUpdateAsync(HttpClient client, Version current, string? feedUrl = null, CancellationToken token = default)
    {
        using HttpResponseMessage response = await client.GetAsync(feedUrl ?? FeedUrl, token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        AppRelease? latest = ParseRelease(await response.Content.ReadAsStringAsync(token));
        return latest is not null && latest.Version > Normalize(current) ? latest : null;
    }

    /// <summary>
    /// Reads a release from GitHub's API JSON (api.github.com/repos/{owner}/{repo}/releases/latest). Null for a draft
    /// or prerelease, a tag that isn't a version, or a release without a win-x64 archive.
    /// </summary>
    public static AppRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement release = document.RootElement;
        if (Bool(release, "draft") || Bool(release, "prerelease") || ParseVersion(String(release, "tag_name")) is not { } version)
            return null;

        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = String(asset, "name");
            if (name is null || !IsWindowsArchive(name) || String(asset, "browser_download_url") is not { } url)
                continue;

            // GitHub gives assets uploaded since mid 2025 a digest such as "sha256:9f86d0..."
            string? digest = String(asset, "digest");
            string? sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..].ToLowerInvariant()
                : null;

            return new AppRelease(
                version,
                String(release, "name") is { Length: > 0 } title ? title : version.ToString(),
                String(release, "body") ?? "",
                String(release, "html_url") ?? "",
                name,
                new Uri(url),
                asset.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0,
                sha256);
        }

        return null;
    }

    /// <summary>A tag such as v1.2.0, 1.2 or v1.2.0-beta as a three part version, or null if it isn't one.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string text = tag.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(['-', '+', ' ']);
        if (suffix >= 0)
            text = text[..suffix];

        return Version.TryParse(text, out Version? version) ? Normalize(version) : null;
    }

    /// <summary>Downloads the release's archive to <paramref name="path"/>, checking its size and hash when known.</summary>
    /// <param name="progress">The fraction downloaded, 0 to 1</param>
    public static async Task DownloadAsync(HttpClient client, AppRelease release, string path, IProgress<double>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.Size;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (Stream download = await response.Content.ReadAsStreamAsync(token))
        await using (FileStream file = File.Create(path))
        {
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await download.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                hash.AppendData(buffer, 0, read);
                received += read;
                if (total > 0)
                    progress?.Report(Math.Min(1, (double)received / total));
            }
        }

        if (release.Size > 0 && received != release.Size)
            throw new InvalidDataException($"The download was {received} bytes, but {release.AssetName} is {release.Size}");

        if (release.Sha256 is { } expected && Convert.ToHexStringLower(hash.GetHashAndReset()) != expected)
            throw new InvalidDataException($"The downloaded {release.AssetName} doesn't match the hash GitHub has for it");
    }

    /// <summary>
    /// Extracts a release archive (7z or zip) into <paramref name="folder"/> and returns the folder holding its
    /// <see cref="UiExecutableName"/>, the one whose files replace the install's.
    /// </summary>
    public static string Extract(string archivePath, string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(folder);
        using (IArchive archive = ArchiveFactory.Open(archivePath))
        {
            // Reading the entries in order is much faster for solid 7z archives than extracting them one by one
            using IReader reader = archive.ExtractAllEntries();
            reader.WriteAllToDirectory(folder, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
        }

        string? executable = Directory.EnumerateFiles(folder, UiExecutableName, SearchOption.AllDirectories)
            .OrderBy(x => x.Length)
            .FirstOrDefault();
        return Path.GetDirectoryName(executable)
            ?? throw new InvalidDataException($"{Path.GetFileName(archivePath)} doesn't have {UiExecutableName} in it");
    }

    /// <summary>
    /// Moves every file of the extracted release <paramref name="source"/> into <paramref name="installDirectory"/>.
    /// Files being replaced are renamed out of the way first, which works for running executables, and put back if
    /// anything fails. Files the new release doesn't have are left alone. <see cref="CleanUp"/> deletes the old files.
    /// </summary>
    public static void Apply(string source, string installDirectory)
    {
        var moved = new List<(string Target, string? Old)>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList())
            {
                string target = Path.Combine(installDirectory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? old = null;
                if (File.Exists(target))
                {
                    old = target + OldSuffix;
                    File.Delete(old);
                    File.Move(target, old);
                }

                moved.Add((target, old));
                File.Move(file, target);
            }
        }
        catch
        {
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                (string target, string? old) = moved[i];
                try
                {
                    if (old is null)
                    {
                        File.Delete(target);
                    }
                    else if (File.Exists(old))
                    {
                        File.Move(old, target, overwrite: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Keep restoring the rest
                }
            }

            throw;
        }

        string staging = Path.Combine(installDirectory, StagingFolderName);
        Directory.CreateDirectory(staging);
        File.WriteAllLines(Path.Combine(staging, CleanupListName), moved.Where(x => x.Old is not null).Select(x => x.Old!));
    }

    /// <summary>
    /// Deletes what the last update left: the files it replaced and the download. The program that started the update
    /// may still be closing, so its files are retried for a while. Returns whether everything was deleted.
    /// </summary>
    public static async Task<bool> CleanUpAsync(string installDirectory, TimeSpan? wait = null, CancellationToken token = default)
    {
        DateTime giveUp = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            if (CleanUp(installDirectory))
                return true;

            if (DateTime.UtcNow >= giveUp)
                return false;

            await Task.Delay(500, token);
        }
    }

    /// <summary>One attempt at <see cref="CleanUpAsync"/>.</summary>
    public static bool CleanUp(string installDirectory)
    {
        string staging = Path.Combine(installDirectory, StagingFolderName);
        if (!Directory.Exists(staging))
            return true;

        string list = Path.Combine(staging, CleanupListName);
        bool clean = true;
        if (File.Exists(list))
        {
            foreach (string old in File.ReadAllLines(list))
            {
                // Only ever the files Apply renamed
                if (!old.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(old);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    clean = false;
                }
            }
        }

        if (!clean)
            return false;

        try
        {
            Directory.Delete(staging, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsWindowsArchive(string name) =>
        name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)
        && (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static Version Normalize(Version version) => new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
