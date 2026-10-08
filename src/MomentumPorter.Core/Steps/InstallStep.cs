namespace MomentumPorter.Steps;

using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;

/// <summary>
/// What <see cref="InstallStep"/> did, left in the job's context.
/// </summary>
/// <param name="InstalledPath">Where the map was installed, or null if it wasn't</param>
/// <param name="Skipped">The user chose to keep the map that was already installed</param>
public sealed record InstallResult(string? InstalledPath, bool Skipped);

/// <summary>
/// Copies the finished map, and the files the conversion wrote next to it (<see cref="PortContext.ExtraOutputs"/>, e.g.
/// zones/local/&lt;map&gt;.json), into Momentum Mod's maps folder, the only place a map job writes outside its work folder.
/// When a map of that name is already installed and the options don't say to overwrite, the user is asked
/// (<see cref="IUserPrompts"/>); with nobody to ask, the existing map is kept.
/// </summary>
public sealed class InstallStep : IPortStep
{
    public string Name => "Install into Momentum";

    public async Task RunAsync(PortJobContext context)
    {
        MapPortState state = context.Get<MapPortState>();
        PorterSettings settings = context.TryGet<PorterSettings>() ?? new PorterSettings();

        if (!File.Exists(state.OutputPath))
            throw new PortStepException($"{state.OutputPath} doesn't exist, there's nothing to install");

        string? contentDirectory = settings.ResolveMomentumContentDirectory();
        if (contentDirectory is null || !Directory.Exists(contentDirectory))
        {
            throw new PortStepException(string.IsNullOrWhiteSpace(settings.MomentumDirectory)
                ? "Momentum Mod wasn't found in your Steam libraries. Set its folder in Settings."
                : $"Momentum Mod wasn't found at {settings.MomentumDirectory}. Check its folder in Settings.");
        }

        string mapsDirectory = Path.Combine(contentDirectory, "maps");
        Directory.CreateDirectory(mapsDirectory);

        string fileName = state.MapName + ".bsp";
        string destination = Path.Combine(mapsDirectory, fileName);
        if (File.Exists(destination) && !state.Options.Overwrite)
        {
            bool overwrite = await context.Prompts().ConfirmAsync($"{fileName} already exists in Momentum's maps folder ({mapsDirectory}). Overwrite it?").ConfigureAwait(false);
            if (!overwrite)
            {
                context.Log.Warn($"Skipped installing {fileName}, it's already in {mapsDirectory}");
                context.Set(new InstallResult(null, Skipped: true));
                return;
            }
        }

        context.Cancellation.ThrowIfCancellationRequested();

        // Files the conversion wrote next to the map (e.g. GOKZ zones in zones/local/<map>.json) keep their layout
        string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(state.OutputPath))!;
        var copies = new List<(string Source, string Destination)> { (state.OutputPath, destination) };
        foreach (ExtraOutput extra in state.PortContext?.ExtraOutputs ?? [])
        {
            string source = WriteBspStep.GetExtraOutputPath(outputDirectory, extra.RelativePath);
            if (!File.Exists(source))
                throw new PortStepException($"{source} doesn't exist, it should have been written with the map");

            copies.Add((source, WriteBspStep.GetExtraOutputPath(mapsDirectory, extra.RelativePath)));
        }

        // Copy everything next to its destination first and only then swap the files in, so a cancelled or failed
        // copy never leaves a broken or half-installed map behind
        var partials = new List<string>();
        try
        {
            foreach ((string source, string target) in copies)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string partial = target + ".partial";
                partials.Add(partial);
                await CopyAsync(source, partial, context.Cancellation).ConfigureAwait(false);
            }

            // Extra files first, so the map never shows up without them
            for (int i = copies.Count - 1; i >= 0; i--)
                File.Move(partials[i], copies[i].Destination, overwrite: true);
        }
        catch
        {
            foreach (string partial in partials)
                TryDelete(partial);

            throw;
        }

        context.Log.Info($"Installed {fileName} into {mapsDirectory}");
        foreach ((_, string target) in copies.Skip(1))
            context.Log.Info($"Installed {Path.GetRelativePath(mapsDirectory, target)} into {mapsDirectory}");

        context.Set(new InstallResult(destination, Skipped: false));
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellation)
    {
        await using FileStream input = File.OpenRead(source);
        await using FileStream output = File.Create(destination);
        await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
