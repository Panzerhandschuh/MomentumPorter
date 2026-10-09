namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Steam;

/// <summary>
/// The games player sound packs can take their movement sounds from, and opening them.
/// </summary>
public static class PlayerSoundSources
{
    /// <summary>Quake 3 first (the default), then the Source games, then GoldSrc.</summary>
    public static readonly IReadOnlyList<SourceGameInfo> Games =
    [
        SourceGames.Quake3,
        SourceGames.CounterStrikeSource,
        SourceGames.CounterStrikeGlobalOffensive,
        SourceGames.TeamFortress2,
        SourceGames.HalfLife2,
        SourceGames.CounterStrike16,
        SourceGames.HalfLife,
    ];

    /// <summary>The game with this id (q3, css, csgo, tf2, hl2, cs16, hl1), or null.</summary>
    public static SourceGameInfo? Find(string? id)
    {
        return Games.FirstOrDefault(x => x.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The folder the game's sounds are read from: the one given, or the game's install found through Steam (for
    /// Quake 3, Quake Live when Quake 3 isn't installed). Null if the game isn't installed.
    /// </summary>
    public static string? Locate(SourceGameInfo game, string? directory, SteamLocator? steam = null)
    {
        if (!string.IsNullOrWhiteSpace(directory))
            return directory;

        steam ??= new SteamLocator();
        if (game.Engine == ContentEngine.Quake3)
            return Quake3SoundSource.FindInstalled(steam);

        try
        {
            return SourceGames.LocateContentDirectory(game, steam);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a game's sounds from its folder (see <see cref="Locate"/>).
    /// </summary>
    /// <param name="momentumSurfaces">Momentum's surface properties, which Source games match footsteps through</param>
    public static PlayerSoundSource Open(SourceGameInfo game, string path, SteamLocator steam, SurfaceProperties? momentumSurfaces = null)
    {
        return game.Engine switch
        {
            ContentEngine.Quake3 => Quake3SoundSource.Open(path),
            ContentEngine.GoldSrc => GoldSrcSoundSource.Open(game, steam, path),
            _ => SourceGameSoundSource.Open(game, steam, path, momentumSurfaces),
        };
    }
}
