namespace MomentumPorter.Options;

using System.ComponentModel;

/// <summary>Which maps get the bhop trigger fix (<see cref="Review.BhopTriggers"/>). The descriptions are the app's labels.</summary>
public enum BhopTriggerFix
{
    /// <summary>Maps named bhop_*.</summary>
    [Description("bhop_ maps only")]
    BhopMapsOnly,

    /// <summary>Every map (but KZ Mod's), e.g. kz_bhop_* hybrids or bhop maps missing their prefix.</summary>
    [Description("On")]
    On,

    [Description("Off")]
    Off,
}

/// <summary>
/// Options for porting a Source engine map (KZ Mod, GOKZ) with a game profile.
/// Quake 3 and GoldSrc maps have their own options, see the BSPConvert integration.
/// Only the options with a <see cref="PortOptionAttribute"/> are on the app's Maps page; the rest are the command
/// line's (the app detects the game, always packs KZ maps, installs into Momentum and replaces what's there).
/// </summary>
public sealed class MapPortOptions
{
    /// <summary>Game profile name (<c>GameProfiles.All</c>), or null to detect it from the map's entities.</summary>
    public string? Game { get; set; }

    [PortOption("Compress output", "LZMA compress the ported map's lumps and packed files, like bspzip -repack -compress.", Order = 0)]
    public bool Compress { get; set; } = true;

    [PortOption("Fix bhop triggers", "Make the triggers on bhop blocks that rename the player for a filtered teleport only do it once they land, so jumping up a slope or onto a ledge next to a block doesn't teleport them. By default only maps named bhop_* get it; pick On for bhop maps named otherwise (e.g. kz_bhop_*), or Off for maps it breaks. KZ Mod maps never need it.", Order = 1)]
    public BhopTriggerFix FixBhopTriggers { get; set; } = BhopTriggerFix.BhopMapsOnly;

    /// <summary>
    /// The source game's folder holding gameinfo.txt, for game files the map relies on (e.g. shared soundscapes).
    /// Null for the folder above the input's maps folder.
    /// </summary>
    public string? GameDirectory { get; set; }

    /// <summary>
    /// Run bspPack after porting so a Kreedz Climbing map carries the assets it needs from KZ, which Momentum doesn't
    /// mount (the other games are mounted, so their maps are never packed). Always on in the app, since a KZ map
    /// isn't playable without it; the command line turns it off because it only ports.
    /// </summary>
    public bool Pack { get; set; } = true;

    /// <summary>Copy the finished map into Momentum Mod's maps folder; otherwise it's left where the job wrote it.</summary>
    public bool Install { get; set; } = true;

    /// <summary>
    /// Replace a map of the same name that is already in Momentum's game folder. Off, Source maps ask first and
    /// Quake 3 and GoldSrc maps stop before copying anything.
    /// </summary>
    public bool Overwrite { get; set; } = true;
}
