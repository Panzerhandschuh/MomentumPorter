namespace MomentumPorter.UI.Services;

using System.Diagnostics;
using MomentumPorter.Updates;

/// <summary>
/// Checks for a newer release when the app starts and, if the user wants it, downloads it, swaps it in and restarts.
/// When jobs are running and the user doesn't want them stopped, the update is swapped in when the app closes instead.
/// </summary>
/// <param name="status">Shows a message in the status bar</param>
/// <param name="isBusy">Whether any jobs are running</param>
/// <param name="restart">Stops the jobs, starts the given executable and closes the app</param>
public sealed class UpdateService(
    SettingsService settings,
    IDialogService dialogs,
    Action<string?> status,
    Func<bool> isBusy,
    Action<string> restart)
{
    /// <summary>Set for the program started after an update, to the version it was updated from.</summary>
    public const string UpdatedFromVariable = "MOMENTUMPORTER_UPDATED_FROM";

    // An extracted update waiting for the app to close
    private string? _pending;

    public string InstallDirectory { get; init; } = AppContext.BaseDirectory;

    public Version CurrentVersion { get; init; } = AppUpdater.CurrentVersion;

    /// <summary>
    /// Whether this copy can update itself: a published Windows release, not a development build. Releases only have a
    /// win-x64 archive for the updater, so the Linux build is updated by hand.
    /// </summary>
    public bool CanUpdate => OperatingSystem.IsWindows() && !AppUpdater.IsDevelopmentBuild(CurrentVersion) && AppUpdater.IsSingleFileApp;

    /// <summary>
    /// The status message for a program started after an update, or null. Clears the variable so it isn't passed on.
    /// </summary>
    public string? TakeUpdatedMessage()
    {
        if (Environment.GetEnvironmentVariable(UpdatedFromVariable) is not { Length: > 0 } from)
            return null;

        Environment.SetEnvironmentVariable(UpdatedFromVariable, null);
        return $"Updated Momentum Porter from {from} to {CurrentVersion}";
    }

    /// <summary>
    /// Deletes what the last update left behind, then looks for a newer release unless the settings turn that off.
    /// Failing to reach GitHub is silent; failing to update says so.
    /// </summary>
    public async Task CheckAsync()
    {
        _ = AppUpdater.CleanUpAsync(InstallDirectory);
        if (!CanUpdate || !settings.Settings.CheckForUpdates)
            return;

        AppRelease? release;
        using HttpClient client = AppUpdater.CreateHttpClient();
        try
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            release = await AppUpdater.FindUpdateAsync(client, CurrentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or UriFormatException)
        {
            Trace.WriteLine($"Couldn't check for updates: {ex.Message}");
            return;
        }

        if (release is null || !await dialogs.ConfirmAsync("Update available", Describe(release)))
            return;

        await UpdateAsync(release);
    }

    /// <summary>Swaps in an update that was waiting for the app to close. Call as the app exits.</summary>
    public void ApplyPending()
    {
        if (Interlocked.Exchange(ref _pending, null) is not { } pending)
            return;

        try
        {
            AppUpdater.Apply(pending, InstallDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Apply put the old files back; the next start offers the update again
            Trace.WriteLine($"Couldn't install the update: {ex.Message}");
        }
    }

    private async Task UpdateAsync(AppRelease release)
    {
        string staging = Path.Combine(InstallDirectory, AppUpdater.StagingFolderName);
        string archive = Path.Combine(staging, release.AssetName);
        try
        {
            int shown = -1;
            var progress = new Progress<double>(fraction =>
            {
                int percent = (int)(fraction * 100);
                if (percent != shown)
                    status($"Downloading Momentum Porter {release.Version}... {shown = percent}%");
            });
            status($"Downloading Momentum Porter {release.Version}...");
            using (HttpClient client = AppUpdater.CreateHttpClient())
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                await AppUpdater.DownloadAsync(client, release, archive, progress);
            }

            status($"Extracting Momentum Porter {release.Version}...");
            string extracted = await Task.Run(() => AppUpdater.Extract(archive, Path.Combine(staging, "files")));
            File.Delete(archive);

            if (isBusy() && !await dialogs.ConfirmAsync("Update ready",
                    $"Momentum Porter {release.Version} is ready. Restart now? This stops the jobs that are running.\n\n" +
                    "Choose No to finish them first; the update is installed when you close the porter."))
            {
                _pending = extracted;
                status($"Momentum Porter {release.Version} is installed when you close the porter");
                return;
            }

            await Task.Run(() => AppUpdater.Apply(extracted, InstallDirectory));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or SharpCompress.Common.ArchiveException or InvalidOperationException)
        {
            status(null);
            try
            {
                File.Delete(archive);
            }
            catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
            {
                // The next start's clean up deletes it
            }

            if (await dialogs.ConfirmAsync("Update failed",
                    $"Momentum Porter {release.Version} couldn't be installed: {ex.Message}\n\nOpen the release page to download it yourself?"))
                ShellService.OpenUrl(release.PageUrl);

            return;
        }

        restart(Path.Combine(InstallDirectory, AppUpdater.UiExecutableName));
    }

    private string Describe(AppRelease release)
    {
        string notes = release.Notes.Trim();
        const int maxNotes = 1200;
        if (notes.Length > maxNotes)
            notes = notes[..maxNotes].TrimEnd() + "...";

        string title = release.Name == release.Version.ToString() || release.Name == $"v{release.Version}"
            ? $"Momentum Porter {release.Version}"
            : $"Momentum Porter {release.Version} ({release.Name})";
        return $"{title} is available. You have {CurrentVersion}.\n\n" +
               (notes.Length > 0 ? notes + "\n\n" : "") +
               "Download it and restart?";
    }
}
