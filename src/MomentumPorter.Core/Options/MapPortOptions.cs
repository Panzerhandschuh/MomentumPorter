namespace MomentumPorter.Options;

/// <summary>
/// Options for porting a Source engine map (KZ Mod, GOKZ) with a game profile.
/// Quake 3 and GoldSrc maps have their own options, see the BSPConvert integration.
/// Only the options with a <see cref="PortOptionAttribute"/> are on the app's Maps page; the rest are the command
/// line's (the app detects the game, always installs into Momentum and replaces what's there).
/// </summary>
public sealed class MapPortOptions
{
    /// <summary>Game profile name (<c>GameProfiles.All</c>), or null to detect it from the map's entities.</summary>
    public string? Game { get; set; }

    [PortOption("Compress output", "LZMA compress the ported map's lumps and packed files, like bspzip -repack -compress.", Order = 0)]
    public bool Compress { get; set; } = true;

    /// <summary>
    /// The source game's folder holding gameinfo.txt, for game files the map relies on (e.g. shared soundscapes).
    /// Null for the folder above the input's maps folder.
    /// </summary>
    public string? GameDirectory { get; set; }

    [PortOption("Pack assets", "Run bspPack after porting so the map carries the assets it needs from the source game.", Order = 10)]
    public bool Pack { get; set; } = true;

    /// <summary>Copy the finished map into Momentum Mod's maps folder; otherwise it's left where the job wrote it.</summary>
    public bool Install { get; set; } = true;

    /// <summary>
    /// Replace a map of the same name that is already in Momentum's game folder. Off, Source maps ask first and
    /// Quake 3 and GoldSrc maps stop before copying anything.
    /// </summary>
    public bool Overwrite { get; set; } = true;
}
