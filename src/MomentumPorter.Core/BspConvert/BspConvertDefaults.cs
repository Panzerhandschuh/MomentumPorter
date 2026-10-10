namespace MomentumPorter.BspConvert;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Detection;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Fills in the BSPConvert options a user would otherwise have to look up, from the games Steam has installed.
/// </summary>
public static class BspConvertDefaults
{
    /// <summary>
    /// Fills <see cref="BspConvertOptions.Wads"/>, <see cref="BspConvertOptions.Studiomdl"/> and
    /// <see cref="BspConvertOptions.OutputDirectory"/> where they're empty and the games are installed.
    /// </summary>
    /// <param name="inputPath">The map being converted, so its own mod's folders are searched first</param>
    /// <param name="settings">For the Half-Life and Counter-Strike 1.6 folders set on the Settings page</param>
    /// <returns>What was filled in, for the log</returns>
    public static IReadOnlyList<string> Fill(BspConvertOptions options, SteamLocator steam, string? inputPath = null, bool output = true, PorterSettings? settings = null)
    {
        var filled = new List<string>();
        GameLocation? momentum = steam.FindMomentum();
        bool goldSrc = inputPath is null || HasGoldSrcMaps(inputPath);

        if (goldSrc && string.IsNullOrWhiteSpace(options.Wads))
        {
            IReadOnlyList<string> wads = FindWadDirectories(steam, inputPath, settings);
            if (wads.Count > 0)
            {
                options.Wads = string.Join(',', wads);
                filled.Add($"WAD folders: {options.Wads}");
            }
        }

        if (goldSrc && string.IsNullOrWhiteSpace(options.Studiomdl) && FindStudiomdl(momentum) is string studiomdl)
        {
            options.Studiomdl = studiomdl;
            filled.Add($"studiomdl: {studiomdl}");
        }

        if (output && string.IsNullOrWhiteSpace(options.OutputDirectory) && momentum?.ContentDirectory is string momentumDir)
        {
            options.OutputDirectory = momentumDir;
            filled.Add($"output: {momentumDir}");
        }

        return filled;
    }

    /// <summary>
    /// The Half-Life mod folders GoldSrc maps take their WADs, skies, sounds and models from: the input's own mod
    /// first (when it's inside a Half-Life install), then Counter-Strike, then Half-Life itself, each followed by
    /// its _downloads folder, where the game keeps what it downloaded from servers. GoldSrc searches a mod before valve too.
    /// Counter-Strike's and Half-Life's folders are the ones set in <paramref name="settings"/> where there are any.
    /// </summary>
    public static IReadOnlyList<string> FindWadDirectories(SteamLocator steam, string? inputPath = null, PorterSettings? settings = null)
    {
        var mods = new List<string>();
        if (inputPath is not null && FindModDirectory(inputPath) is string inputMod)
            mods.Add(inputMod);

        GameLocation? halfLife = steam.Find(KnownGame.HalfLife);
        foreach (SourceGameInfo game in new[] { SourceGames.CounterStrike16, SourceGames.HalfLife })
        {
            if (settings?.ResolveGameContentDirectory(game) is { } set)
                mods.Add(set);
            else if (halfLife is not null)
                mods.Add(Path.Combine(halfLife.InstallDirectory, SourceGames.ContentFolderName(game)));
        }

        var dirs = new List<string>();
        foreach (string mod in mods)
        {
            foreach (string dir in new[] { mod, mod + "_downloads" })
            {
                if (Directory.Exists(dir) && !dirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    dirs.Add(dir);
            }
        }

        return dirs;
    }

    /// <summary>
    /// Momentum's studiomdl (bin/win64/studiomdl.exe), which GoldSrc models are compiled with.
    /// </summary>
    public static string? FindStudiomdl(GameLocation? momentum)
    {
        if (momentum is null)
            return null;

        string path = Path.Combine(momentum.InstallDirectory, "bin", "win64", "studiomdl.exe");
        return File.Exists(path) ? path : null;
    }

    private static bool HasGoldSrcMaps(string inputPath)
    {
        try
        {
            return Pk3Inspector.ListMaps(inputPath).Any(x => x.Kind == InputKind.GoldSrcBsp);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The conversion reports what's wrong with the input
            return false;
        }
    }

    // <mod>/maps/<map>.bsp, where <mod> has a liblist.gam (every GoldSrc mod does)
    private static string? FindModDirectory(string inputPath)
    {
        string? maps = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        if (maps is null || !Path.GetFileName(maps).Equals("maps", StringComparison.OrdinalIgnoreCase))
            return null;

        string? mod = Path.GetDirectoryName(maps);
        return mod is not null && File.Exists(Path.Combine(mod, "liblist.gam")) ? mod : null;
    }
}
