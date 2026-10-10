namespace MomentumPorter.Settings;

using System.Text.Json;
using System.Text.Json.Serialization;
using MomentumPorter.Steam;

/// <summary>
/// Machine-specific settings shared by the desktop app and the CLI: where the tools and games are.
/// Null paths mean "find it automatically".
/// </summary>
public sealed class PorterSettings
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>%AppData%/MomentumPorter/settings.json</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MomentumPorter", "settings.json");

    /// <summary>
    /// Momentum Mod's install folder (the one holding <c>momentum/</c>). Null to find it in the Steam libraries.
    /// </summary>
    public string? MomentumDirectory { get; set; }

    /// <summary>
    /// Kreedz Climbing's install folder (the one holding <c>kz/</c>), where KZ maps' assets and soundscapes come from.
    /// Null to find it in the Steam libraries.
    /// </summary>
    public string? KreedzClimbingDirectory { get; set; }

    /// <summary>
    /// studiomdl.exe, which compiles the models GoldSrc maps and converted weapons use. Null for Momentum's own
    /// (bin/win64/studiomdl.exe in its install), see <see cref="ResolveStudiomdlPath"/>.
    /// </summary>
    public string? StudiomdlPath { get; set; }

    /// <summary>Folder for jobs' temporary files. Null for the system temp folder.</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>Where finished maps go when they aren't installed into Momentum. Null for <see cref="DefaultOutputDirectory"/>.</summary>
    public string? OutputDirectory { get; set; }

    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MomentumPorter", "maps");

    public string ResolveOutputDirectory() => string.IsNullOrWhiteSpace(OutputDirectory) ? DefaultOutputDirectory : OutputDirectory;

    /// <summary>
    /// Kreedz Climbing's content folder (<c>&lt;install&gt;/kz</c>), or null if KZ can't be found.
    /// The override may point at the install folder or the content folder.
    /// </summary>
    public string? ResolveKreedzClimbingContentDirectory(SteamLocator? steam = null)
    {
        if (!string.IsNullOrWhiteSpace(KreedzClimbingDirectory))
        {
            string dir = Path.GetFullPath(KreedzClimbingDirectory.Trim());
            string content = Path.Combine(dir, KnownGame.KreedzClimbing.ContentFolder);
            if (File.Exists(Path.Combine(content, "gameinfo.txt")))
                return content;

            return File.Exists(Path.Combine(dir, "gameinfo.txt")) ? dir : null;
        }

        return (steam ?? new SteamLocator()).Find(KnownGame.KreedzClimbing)?.ContentDirectory;
    }

    /// <summary>
    /// The studiomdl to compile models with: the override, else Momentum's <c>bin/win64/studiomdl.exe</c>, or null
    /// when neither Momentum nor the file is found.
    /// </summary>
    public string? ResolveStudiomdlPath(SteamLocator? steam = null)
    {
        if (!string.IsNullOrWhiteSpace(StudiomdlPath))
            return StudiomdlPath.Trim();

        if (ResolveMomentumContentDirectory(steam) is not { } content || Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(content)) is not { } install)
            return null;

        string path = Path.Combine(install, "bin", "win64", "studiomdl.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Momentum's maps folder (<c>&lt;install&gt;/momentum/maps</c>), or null if Momentum can't be found.
    /// </summary>
    public string? ResolveMomentumMapsDirectory(SteamLocator? steam = null)
    {
        string? content = ResolveMomentumContentDirectory(steam);
        return content is null ? null : Path.Combine(content, "maps");
    }

    /// <summary>
    /// Momentum's content folder (<c>&lt;install&gt;/momentum</c>), or null if Momentum can't be found.
    /// The override may point at the install folder, the content folder or its maps folder.
    /// </summary>
    public string? ResolveMomentumContentDirectory(SteamLocator? steam = null)
    {
        if (!string.IsNullOrWhiteSpace(MomentumDirectory))
        {
            string dir = Path.GetFullPath(MomentumDirectory.Trim());
            if (Directory.Exists(Path.Combine(dir, "momentum")))
                return Path.Combine(dir, "momentum");

            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Equals("maps", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return Directory.Exists(dir) ? dir : null;
        }

        return (steam ?? new SteamLocator()).FindMomentum()?.ContentDirectory;
    }

    public PorterSettings Clone()
    {
        return JsonSerializer.Deserialize<PorterSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
    }

    /// <summary>
    /// Reads settings, falling back to defaults when the file is missing. A file that can't be parsed throws.
    /// </summary>
    public static PorterSettings Load(string path)
    {
        if (!File.Exists(path))
            return new PorterSettings();

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PorterSettings>(stream, JsonOptions) ?? new PorterSettings();
    }

    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        // Write then swap, so a crash mid-write doesn't lose the old settings
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
