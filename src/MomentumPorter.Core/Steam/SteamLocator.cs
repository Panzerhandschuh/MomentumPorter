namespace MomentumPorter.Steam;

using System.Text.RegularExpressions;

/// <summary>
/// A game whose files the porter reads or writes.
/// </summary>
/// <param name="AppId">Steam app id, or 0 if the game isn't found by app id</param>
/// <param name="InstallFolder">Folder name under steamapps/common, used when there's no app id or no manifest</param>
/// <param name="ContentFolder">Folder inside the install with the game's content (gameinfo.txt, pak files)</param>
public sealed record KnownGame(string Id, string Name, int AppId, string InstallFolder, string ContentFolder)
{
    public static readonly KnownGame HalfLife = new("hl1", "Half-Life", 70, "Half-Life", "valve");
    public static readonly KnownGame CounterStrike16 = new("cs16", "Counter-Strike 1.6", 10, "Half-Life", "cstrike");
    public static readonly KnownGame TeamFortressClassic = new("tfc", "Team Fortress Classic", 20, "Half-Life", "tfc");
    public static readonly KnownGame HalfLife2 = new("hl2", "Half-Life 2", 220, "Half-Life 2", "hl2");
    public static readonly KnownGame CounterStrikeSource = new("css", "Counter-Strike: Source", 240, "Counter-Strike Source", "cstrike");
    public static readonly KnownGame TeamFortress2 = new("tf2", "Team Fortress 2", 440, "Team Fortress 2", "tf");
    // Momentum mounts the legacy (Source 1) csgo folder, which the CS2 install keeps next to game/csgo
    public static readonly KnownGame CounterStrikeGlobalOffensive = new("csgo", "Counter-Strike: Global Offensive", 730, "Counter-Strike Global Offensive", "csgo");
    public static readonly KnownGame Quake3 = new("q3", "Quake III Arena", 2200, "Quake 3 Arena", "baseq3");
    public static readonly KnownGame QuakeLive = new("ql", "Quake Live", 282440, "Quake Live", "baseq3");
    public static readonly KnownGame KreedzClimbing = new("kzmod", "Kreedz Climbing", 0, "KreedzClimbing", "kz");
    public static readonly KnownGame Momentum = new("momentum", "Momentum Mod", 0, "Momentum Mod", "momentum");
    public static readonly KnownGame MomentumPlaytest = new("momentum_playtest", "Momentum Mod Playtest", 0, "Momentum Mod Playtest", "momentum");

    public static readonly IReadOnlyList<KnownGame> All =
    [
        HalfLife, CounterStrike16, TeamFortressClassic, HalfLife2, CounterStrikeSource, TeamFortress2,
        CounterStrikeGlobalOffensive, Quake3, QuakeLive, KreedzClimbing, Momentum, MomentumPlaytest,
    ];
}

/// <summary>
/// Where a game is installed.
/// </summary>
/// <param name="InstallDirectory">steamapps/common/&lt;game&gt;</param>
/// <param name="ContentDirectory">The folder with gameinfo.txt / pak files, or null if it's missing</param>
public sealed record GameLocation(KnownGame Game, string InstallDirectory, string? ContentDirectory);

/// <summary>
/// Finds Steam and the games in its libraries without asking the user.
/// </summary>
public sealed class SteamLocator
{
    private static readonly Regex KeyValue = new("\"([^\"]+)\"\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    private readonly string? _steamRoot;
    private List<string>? _libraries;

    public SteamLocator() : this(FindSteamRoot()) { }

    public SteamLocator(string? steamRoot)
    {
        _steamRoot = steamRoot;
    }

    public string? SteamRoot => _steamRoot;

    /// <summary>
    /// Every library folder (the ones holding a steamapps folder), the Steam install first.
    /// </summary>
    public IReadOnlyList<string> Libraries => _libraries ??= ReadLibraries();

    public GameLocation? Find(KnownGame game)
    {
        foreach (string library in Libraries)
        {
            string common = Path.Combine(library, "steamapps", "common");

            string? install = null;
            if (game.AppId > 0)
            {
                string manifest = Path.Combine(library, "steamapps", $"appmanifest_{game.AppId}.acf");
                if (File.Exists(manifest) && ParseVdfPairs(File.ReadAllText(manifest)).TryGetValue("installdir", out string? installDir))
                    install = Path.Combine(common, installDir);
            }

            install ??= Path.Combine(common, game.InstallFolder);
            if (!Directory.Exists(install))
                continue;

            string content = Path.Combine(install, game.ContentFolder);
            return new GameLocation(game, install, Directory.Exists(content) ? content : null);
        }

        return null;
    }

    public IReadOnlyList<GameLocation> FindAll()
    {
        return KnownGame.All.Select(Find).Where(x => x is not null).ToList()!;
    }

    /// <summary>
    /// Momentum Mod, preferring a release install over the playtest.
    /// </summary>
    public GameLocation? FindMomentum()
    {
        return Find(KnownGame.Momentum) ?? Find(KnownGame.MomentumPlaytest);
    }

    private List<string> ReadLibraries()
    {
        var libraries = new List<string>();
        if (_steamRoot is null)
            return libraries;

        libraries.Add(_steamRoot);

        string vdf = Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (string path in ParseLibraryPaths(File.ReadAllText(vdf)))
            {
                if (Directory.Exists(path) && !libraries.Any(x => string.Equals(Path.GetFullPath(x), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
                    libraries.Add(path);
            }
        }

        return libraries;
    }

    /// <summary>
    /// The "path" values of libraryfolders.vdf, unescaped.
    /// </summary>
    public static IEnumerable<string> ParseLibraryPaths(string vdf)
    {
        foreach (Match match in KeyValue.Matches(vdf))
        {
            if (match.Groups[1].Value.Equals("path", StringComparison.OrdinalIgnoreCase))
                yield return match.Groups[2].Value.Replace("\\\\", "\\");
        }
    }

    /// <summary>
    /// Flattens a simple KeyValues file to its string pairs (first occurrence wins).
    /// </summary>
    public static Dictionary<string, string> ParseVdfPairs(string vdf)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in KeyValue.Matches(vdf))
            pairs.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Replace("\\\\", "\\"));

        return pairs;
    }

    public static string? FindSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
            {
                string? value = Microsoft.Win32.Registry.GetValue(key, key.Contains("HKEY_CURRENT_USER") ? "SteamPath" : "InstallPath", null) as string;
                if (value is not null && Directory.Exists(value))
                    return Path.GetFullPath(value);
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string defaultPath = Path.Combine(programFiles, "Steam");
            return Directory.Exists(defaultPath) ? defaultPath : null;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string candidate in new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam", "Library/Application Support/Steam" })
        {
            string path = Path.Combine(home, candidate);
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }
}
