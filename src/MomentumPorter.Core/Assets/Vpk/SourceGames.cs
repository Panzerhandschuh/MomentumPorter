namespace MomentumPorter.Assets.Vpk;

using MomentumPorter.Steam;

public enum ContentEngine
{
    /// <summary>Source engine; Momentum mounts the game's VPKs, so its assets work as they are.</summary>
    Source,

    /// <summary>GoldSrc (Half-Life 1); models and sounds have to be converted.</summary>
    GoldSrc,

    /// <summary>Quake 3 (id Tech 3); MD3 models and sounds have to be converted.</summary>
    Quake3,
}

/// <summary>
/// A game assets can be ported from, and how to open its files.
/// </summary>
/// <param name="Id">Short id used in the weapon catalog and on the command line (css, tf2, csgo, hl2, cs16, hl1, q3)</param>
/// <param name="MountedByMomentum">Momentum's gameinfo.txt mounts the game, so its paths can be used directly</param>
public sealed record SourceGameInfo(string Id, string Name, ContentEngine Engine, bool MountedByMomentum);

/// <summary>
/// Opens the files of the games weapons and sounds are ported from. For the games Momentum mounts, only what its
/// gameinfo.txt mounts is opened, so a path found here is a path Momentum can load.
/// </summary>
public static class SourceGames
{
    /// <summary>
    /// The legacy (Source 1) CS:GO app, which is what Momentum mounts (app 4465480, folder "csgo legacy").
    /// </summary>
    public static readonly KnownGame CounterStrikeGlobalOffensiveLegacy = new("csgo_legacy", "Counter-Strike: Global Offensive", 4465480, "csgo legacy", "csgo");

    public static readonly SourceGameInfo CounterStrikeSource = new("css", "Counter-Strike: Source", ContentEngine.Source, true);
    public static readonly SourceGameInfo TeamFortress2 = new("tf2", "Team Fortress 2", ContentEngine.Source, true);
    public static readonly SourceGameInfo CounterStrikeGlobalOffensive = new("csgo", "Counter-Strike: Global Offensive", ContentEngine.Source, true);
    public static readonly SourceGameInfo HalfLife2 = new("hl2", "Half-Life 2", ContentEngine.Source, true);
    public static readonly SourceGameInfo CounterStrike16 = new("cs16", "Counter-Strike 1.6", ContentEngine.GoldSrc, false);
    public static readonly SourceGameInfo HalfLife = new("hl1", "Half-Life", ContentEngine.GoldSrc, false);
    public static readonly SourceGameInfo Quake3 = new("q3", "Quake III Arena", ContentEngine.Quake3, false);

    public static readonly IReadOnlyList<SourceGameInfo> All =
    [
        CounterStrikeSource, TeamFortress2, CounterStrikeGlobalOffensive, HalfLife2, CounterStrike16, HalfLife, Quake3,
    ];

    public static SourceGameInfo? Find(string id) => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Opens a game's files.
    /// </summary>
    /// <param name="momentumDirectory">Momentum's install folder (the one holding <c>momentum/</c> and <c>mount/</c>), for HL2, which Momentum ships itself</param>
    /// <param name="contentDirectory">The game's content folder (e.g. <c>cstrike</c>, <c>baseq3</c>) or the install holding it, to use instead of the one found through Steam</param>
    /// <exception cref="DirectoryNotFoundException">The game isn't installed (or the folder given doesn't exist)</exception>
    public static GameContent Open(SourceGameInfo game, SteamLocator steam, string? momentumDirectory = null, string? contentDirectory = null)
    {
        string Locate()
        {
            if (contentDirectory is not null)
            {
                return Directory.Exists(contentDirectory)
                    ? FindContentDirectory(game, contentDirectory) ?? contentDirectory
                    : throw new DirectoryNotFoundException($"{contentDirectory} doesn't exist");
            }

            return LocateContentDirectory(game, steam);
        }

        var sources = new List<IContentSource>();
        try
        {
            switch (game.Id)
            {
                case "css":
                {
                    string dir = Locate();
                    AddVpks(sources, dir, "cstrike_pak");
                    AddFolder(sources, Path.Combine(dir, "download"));
                    break;
                }
                case "tf2":
                {
                    string dir = Locate();
                    AddVpks(sources, dir, "tf2_misc", "tf2_sound_misc", "tf2_sound_vo_english", "tf2_textures");
                    AddFolder(sources, Path.Combine(dir, "download"));
                    break;
                }
                case "csgo":
                {
                    string dir = Locate();
                    AddVpks(sources, dir, "pak01");
                    break;
                }
                case "hl2":
                {
                    // Momentum mounts its own copy of HL2's content (mount/hl2_dir.vpk), not the HL2 install
                    string? momentumVpk = momentumDirectory is null ? null : Path.Combine(momentumDirectory, "mount", "hl2_dir.vpk");
                    if (contentDirectory is null && momentumVpk is not null && File.Exists(momentumVpk))
                    {
                        sources.Add(new VpkReader(momentumVpk));
                    }
                    else
                    {
                        string dir = Locate();
                        AddVpks(sources, dir, "hl2_misc", "hl2_sound_misc", "hl2_textures");
                    }

                    break;
                }
                case "cs16":
                {
                    string dir = Locate();
                    AddGoldSrc(sources, dir);
                    // CS 1.6 uses Half-Life's files for anything it doesn't have
                    string valve = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dir))!, "valve");
                    if (Directory.Exists(valve))
                        AddGoldSrc(sources, valve);
                    break;
                }
                case "hl1":
                    AddGoldSrc(sources, Locate());
                    break;
                case "q3":
                {
                    string dir = Locate();
                    AddFolder(sources, dir);
                    // pak8 overrides pak0
                    foreach (string pk3 in Directory.EnumerateFiles(dir, "*.pk3").OrderByDescending(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
                        sources.Add(new ZipSource(pk3));
                    break;
                }
                default:
                    throw new ArgumentException($"Unknown game {game.Id}", nameof(game));
            }
        }
        catch
        {
            foreach (IContentSource source in sources)
                source.Dispose();
            throw;
        }

        return new GameContent(game.Name, sources);
    }

    /// <summary>
    /// The game's content folder (e.g. <c>cstrike</c>, <c>baseq3</c>): the first install in the Steam libraries whose
    /// content folder has the file the game can't be without. Leftover folders of games that moved to another library
    /// (no manifest, no VPKs) are skipped.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The game isn't installed</exception>
    public static string LocateContentDirectory(SourceGameInfo game, SteamLocator steam)
    {
        (string requiredFile, KnownGame[] candidates) = Install(game);
        foreach (KnownGame candidate in candidates)
        {
            foreach (string found in FindContentDirectories(steam, candidate))
            {
                if (File.Exists(Path.Combine(found, requiredFile)) || Directory.Exists(Path.Combine(found, requiredFile)))
                    return found;
            }
        }

        throw new DirectoryNotFoundException($"{game.Name} isn't installed (looked for {string.Join(", ", candidates.Select(x => $"{x.InstallFolder}/{x.ContentFolder}/{requiredFile}"))} in the Steam libraries)");
    }

    // The file (or folder) a game's content folder can't be without, and the Steam installs it may be in
    private static (string RequiredFile, KnownGame[] Candidates) Install(SourceGameInfo game) => game.Id switch
    {
        "css" => ("cstrike_pak_dir.vpk", new[] { KnownGame.CounterStrikeSource }),
        "tf2" => ("tf2_misc_dir.vpk", [KnownGame.TeamFortress2]),
        "csgo" => ("pak01_dir.vpk", [CounterStrikeGlobalOffensiveLegacy, KnownGame.CounterStrikeGlobalOffensive]),
        "hl2" => ("hl2_misc_dir.vpk", [KnownGame.HalfLife2]),
        "cs16" => ("models", [KnownGame.CounterStrike16]),
        "hl1" => ("models", [KnownGame.HalfLife]),
        "q3" => ("pak0.pk3", [KnownGame.Quake3]),
        _ => throw new ArgumentException($"Unknown game {game.Id}", nameof(game)),
    };

    /// <summary>The name of the game's content folder in its install (cstrike, valve, baseq3...).</summary>
    public static string ContentFolderName(SourceGameInfo game) => Install(game).Candidates[0].ContentFolder;

    /// <summary>
    /// The game's content folder for a folder the user picked, which may be the content folder itself or the install
    /// holding it (Half-Life for cstrike, Quake 3 Arena for baseq3). Null if it's neither. Any pk3 will do for Quake 3,
    /// so Quake Live's baseq3 counts too.
    /// </summary>
    public static string? FindContentDirectory(SourceGameInfo game, string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        string requiredFile = Install(game).RequiredFile;
        bool IsContent(string dir) => game.Engine == ContentEngine.Quake3
            ? Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.pk3").Any()
            : File.Exists(Path.Combine(dir, requiredFile)) || Directory.Exists(Path.Combine(dir, requiredFile));

        string content = Path.Combine(folder, ContentFolderName(game));
        return IsContent(folder) ? folder : IsContent(content) ? content : null;
    }

    /// <summary>
    /// Why <see cref="Open"/> wouldn't find the game's files, or null when it would. Takes the same folders.
    /// </summary>
    public static string? WhyNotFound(SourceGameInfo game, SteamLocator steam, string? momentumDirectory = null, string? contentDirectory = null)
    {
        if (contentDirectory is not null)
        {
            return FindContentDirectory(game, contentDirectory) is null
                ? $"{contentDirectory} isn't {game.Name}'s {ContentFolderName(game)} folder or its install"
                : null;
        }

        // Momentum ships HL2's content itself
        if (game.Id == "hl2" && momentumDirectory is not null && File.Exists(Path.Combine(momentumDirectory, "mount", "hl2_dir.vpk")))
            return null;

        try
        {
            LocateContentDirectory(game, steam);
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return $"{game.Name} wasn't found in your Steam libraries";
        }
    }

    /// <summary>
    /// The game's content folders in every Steam library: the manifest's install folder first, then the default one.
    /// </summary>
    public static IEnumerable<string> FindContentDirectories(SteamLocator steam, KnownGame game)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in steam.Libraries)
        {
            string common = Path.Combine(library, "steamapps", "common");
            var installs = new List<string>();
            string manifest = Path.Combine(library, "steamapps", $"appmanifest_{game.AppId}.acf");
            if (game.AppId > 0 && File.Exists(manifest) && SteamLocator.ParseVdfPairs(File.ReadAllText(manifest)).TryGetValue("installdir", out string? installDir))
                installs.Add(Path.Combine(common, installDir));

            installs.Add(Path.Combine(common, game.InstallFolder));
            foreach (string install in installs)
            {
                string content = Path.Combine(install, game.ContentFolder);
                if (Directory.Exists(content) && seen.Add(Path.GetFullPath(content)))
                    yield return content;
            }
        }
    }

    private static void AddVpks(List<IContentSource> sources, string directory, params string[] names)
    {
        foreach (string name in names)
        {
            string path = Path.Combine(directory, name + "_dir.vpk");
            if (!File.Exists(path))
                path = Path.Combine(directory, name + ".vpk");

            if (File.Exists(path))
                sources.Add(new VpkReader(path));
        }

        if (sources.Count == 0)
            throw new DirectoryNotFoundException($"{directory} has none of the VPKs Momentum mounts ({string.Join(", ", names.Select(x => x + "_dir.vpk"))})");
    }

    private static void AddFolder(List<IContentSource> sources, string directory)
    {
        if (Directory.Exists(directory))
            sources.Add(new FolderSource(directory));
    }

    // Loose files win over pak files, and pak1 over pak0
    private static void AddGoldSrc(List<IContentSource> sources, string directory)
    {
        AddFolder(sources, directory);
        foreach (string pak in Directory.EnumerateFiles(directory, "pak*.pak").OrderByDescending(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
            sources.Add(new PakSource(pak));
    }
}
