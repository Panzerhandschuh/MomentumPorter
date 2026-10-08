namespace MomentumPorter.Games.KreedzClimbing;

using System.Text;
using System.Text.RegularExpressions;
using LibBSP;
using MomentumPorter.Porting;

public sealed partial class KreedzClimbingProfile
{
    private const string SoundscapeManifest = "scripts/soundscapes_manifest.txt";

    /// <summary>
    /// KZ loads the soundscape files in its manifest for every map, but Momentum only loads its own and the map's.
    /// Copies the soundscapes the map uses from KZ's files into the map's soundscape file.
    /// </summary>
    private static void ConvertSoundscapes(PortContext context)
    {
        List<string> used = context
            .Entities.Where(x => x.ClassName.StartsWith("env_soundscape", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TryGetValue("soundscape", out string? name) ? name : "")
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (used.Count == 0)
            return;

        if (context.GameDirectory is null)
        {
            context.Report.Warn("KZ's folder wasn't found, so soundscapes from its shared soundscape files weren't copied");
            return;
        }

        // Packed files are searched before the game's folders
        string inputMapFile = $"scripts/soundscapes_{Path.GetFileNameWithoutExtension(context.Bsp.Reader.BspFile.Name)}.txt";
        string? mapText = context.ReadPakFile(inputMapFile) ?? ReadGameFile(context.GameDirectory, inputMapFile);
        var mapSoundscapes = new Dictionary<string, SoundscapeFile.Soundscape>(StringComparer.OrdinalIgnoreCase);
        if (mapText is not null && Parse(context, inputMapFile, mapText) is { } mapFile)
        {
            foreach (SoundscapeFile.Soundscape soundscape in mapFile.Soundscapes)
                mapSoundscapes.TryAdd(soundscape.Name, soundscape);
        }

        Dictionary<string, (SoundscapeFile File, SoundscapeFile.Soundscape Soundscape)> shared = ReadSharedSoundscapes(context);

        var copied = new List<(SoundscapeFile File, SoundscapeFile.Soundscape Soundscape)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(used);
        while (pending.TryDequeue(out string? name))
        {
            if (!visited.Add(name))
                continue;

            List<string> references;
            if (mapSoundscapes.TryGetValue(name, out SoundscapeFile.Soundscape? soundscape))
            {
                references = soundscape.PlaysSoundscapes;
            }
            else if (shared.TryGetValue(name, out var definition))
            {
                // HL2's soundscape files (including KZ's copies of them) are in Momentum's manifest too
                if (IsKzOnlyFile(context.GameDirectory, definition.File.Path))
                    copied.Add(definition);

                references = definition.Soundscape.PlaysSoundscapes;
            }
            else
            {
                context.Report.Warn($"soundscape \"{name}\" isn't defined in KZ either");
                continue;
            }

            foreach (string reference in references)
                pending.Enqueue(reference);
        }

        if (copied.Count == 0)
            return;

        var text = new StringBuilder(mapText?.TrimEnd() ?? "");
        text.Append("\n\n// Copied by MomentumPorter from Kreedz Climbing's shared soundscape files\n");
        foreach ((SoundscapeFile file, SoundscapeFile.Soundscape soundscape) in copied)
            text.Append($"\n// {Path.GetRelativePath(context.GameDirectory, file.Path).Replace('\\', '/')}\n{soundscape.Text}\n");

        context.WritePakFile($"scripts/soundscapes_{context.MapName}.txt", text.ToString().TrimStart());
        context.Report.Change("soundscape copied from KZ's shared soundscape files", copied.Count);
    }

    // Each manifest file is loaded from the first folder that has it, and the first definition of a soundscape wins
    private static Dictionary<string, (SoundscapeFile File, SoundscapeFile.Soundscape Soundscape)> ReadSharedSoundscapes(PortContext context)
    {
        var soundscapes = new Dictionary<string, (SoundscapeFile, SoundscapeFile.Soundscape)>(StringComparer.OrdinalIgnoreCase);
        string? manifest = ReadGameFile(context.GameDirectory!, SoundscapeManifest);
        if (manifest is null)
        {
            context.Report.Warn($"KZ's {SoundscapeManifest} wasn't found");
            return soundscapes;
        }

        foreach (Match match in ManifestFileRegex().Matches(Regex.Replace(manifest, "//[^\n]*", "")))
        {
            string relativePath = match.Groups[1].Value;
            if (FindGameFile(context.GameDirectory!, relativePath) is not { } path)
                continue;

            if (Parse(context, path, File.ReadAllText(path, Entity.TextEncoding)) is not { } file)
                continue;

            foreach (SoundscapeFile.Soundscape soundscape in file.Soundscapes)
                soundscapes.TryAdd(soundscape.Name, (file, soundscape));
        }

        return soundscapes;
    }

    private static SoundscapeFile? Parse(PortContext context, string path, string text)
    {
        try
        {
            return SoundscapeFile.Parse(path, text);
        }
        catch (FormatException e)
        {
            context.Report.Warn($"couldn't read {e.Message}");
            return null;
        }
    }

    private static string? ReadGameFile(string gameDirectory, string relativePath)
    {
        return FindGameFile(gameDirectory, relativePath) is { } path ? File.ReadAllText(path, Entity.TextEncoding) : null;
    }

    // KZ's own folder, then the HL2 content it mounts. Its VPKs don't hold any scripts.
    private static string? FindGameFile(string gameDirectory, string relativePath)
    {
        return new[] { gameDirectory, Hl2Directory(gameDirectory) }
            .Select(x => Path.GetFullPath(Path.Combine(x, relativePath)))
            .FirstOrDefault(File.Exists);
    }

    private static bool IsKzOnlyFile(string gameDirectory, string path)
    {
        string relativePath = Path.GetRelativePath(gameDirectory, path);
        return !relativePath.StartsWith("..") && !File.Exists(Path.Combine(Hl2Directory(gameDirectory), relativePath));
    }

    private static string Hl2Directory(string gameDirectory)
    {
        return Path.Combine(gameDirectory, "..", "hl2");
    }

    [GeneratedRegex("\"file\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ManifestFileRegex();
}
