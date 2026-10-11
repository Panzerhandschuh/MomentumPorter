namespace MomentumPorter.Detection;

using LibBSP;
using MomentumPorter.Porting;

/// <summary>
/// Guesses which game a Source engine map was made for, to pick a game profile.
/// </summary>
public static class SourceGameSniffer
{
    // Entity classes only KZ Mod has
    private static readonly HashSet<string> KzModClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "trigger_bhop_teleport",
        "func_brush_kzmod",
        "info_kzmod_start",
        "info_kzmod_end",
        "trigger_kzmod_start",
        "trigger_kzmod_end",
        "func_kzmod_button",
        "logic_kzmod_timer",
    };

    // Entity classes of Counter-Strike (Source and GO) maps; the BSP version tells the two apart
    private static readonly HashSet<string> CounterStrikeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "info_player_counterterrorist",
        "info_player_terrorist",
        "func_buyzone",
        "func_bomb_target",
        "func_hostage_rescue",
        "hostage_entity",
        "info_map_parameters",
    };

    public sealed record Detection(string Profile, string Reason, bool Certain);

    /// <summary>
    /// The profile name (see <see cref="GameProfiles"/>) of the game the map seems to be for, or null.
    /// Entities are the strongest signal; the game folder the map sits in and its name are fallbacks for maps that
    /// only use stock entities.
    /// </summary>
    public static Detection? Detect(BSP bsp, string? gameDirectory = null, string? mapName = null)
    {
        if (DetectFromEntities(bsp) is { } fromEntities)
            return fromEntities;

        if (gameDirectory is not null && DetectFromGameInfo(gameDirectory) is { } fromGame)
            return fromGame;

        if (mapName is not null)
        {
            if (mapName.StartsWith("kz_", StringComparison.OrdinalIgnoreCase))
                return new Detection("kzmod", "the map's name; it has no game specific entities", Certain: false);

            if (mapName.StartsWith("bhop_", StringComparison.OrdinalIgnoreCase) || mapName.StartsWith("surf_", StringComparison.OrdinalIgnoreCase))
                return new Detection("css", "the map's name; it has no game specific entities", Certain: false);
        }

        return null;
    }

    public static string? DetectProfile(BSP bsp) => DetectFromEntities(bsp)?.Profile;

    private static Detection? DetectFromEntities(BSP bsp)
    {
        if (bsp.Entities is not { } entities)
            return null;

        bool kzMod = false;
        bool gokz = false;
        bool counterStrike = false;
        foreach (Entity entity in entities)
        {
            if (KzModClasses.Contains(entity.ClassName))
                kzMod = true;

            if (CounterStrikeClasses.Contains(entity.ClassName))
                counterStrike = true;

            // GOKZ's mapping API is driven purely by targetnames (climb_teleport, climb_startzone, ...)
            if (entity.Name.StartsWith("climb_", StringComparison.OrdinalIgnoreCase))
                gokz = true;
        }

        if (kzMod)
            return new Detection("kzmod", "its KZ Mod entities", Certain: true);

        if (gokz)
            return new Detection("gokz", "its GOKZ mapping API entities", Certain: true);

        if (counterStrike)
        {
            // CS:GO maps are BSP version 21; CS:S (and the other Source 2006/2007/2013 games) stop at 20
            if (bsp.MapType == MapType.Source21)
                return new Detection("gokz", "its Counter-Strike entities in a CS:GO (version 21) map; it has no GOKZ mapping API entities", Certain: false);

            if (bsp.MapType is MapType.Source19 or MapType.Source20)
                return new Detection("css", "its Counter-Strike entities", Certain: true);
        }

        return null;
    }

    private static Detection? DetectFromGameInfo(string gameDirectory)
    {
        string gameInfo = Path.Combine(gameDirectory, "gameinfo.txt");
        if (!File.Exists(gameInfo))
            return null;

        string text = File.ReadAllText(gameInfo);
        if (text.Contains("Kreedz", StringComparison.OrdinalIgnoreCase) || text.Contains("kreedzclimbing", StringComparison.OrdinalIgnoreCase))
            return new Detection("kzmod", "the game folder it's in", Certain: true);

        if (text.Contains("Counter-Strike: Source", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(gameDirectory, "cstrike_pak_dir.vpk")))
            return new Detection("css", "the game folder it's in", Certain: true);

        return null;
    }
}
