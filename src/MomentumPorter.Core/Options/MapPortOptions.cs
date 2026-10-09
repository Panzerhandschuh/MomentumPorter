namespace MomentumPorter.Options;

/// <summary>
/// Options for porting a Source engine map (KZ Mod, GOKZ) with a game profile.
/// Quake 3 and GoldSrc maps have their own options, see the BSPConvert integration.
/// </summary>
public sealed class MapPortOptions
{
    /// <summary>Game profile name (<c>GameProfiles.All</c>), or null to detect it from the map's entities.</summary>
    [PortOption("Source game", "The game the map was made for. Detected from the map's entities when left empty.", Order = 0, Choices = OptionChoices.GameProfile)]
    public string? Game { get; set; }

    [PortOption("Compress output", "LZMA compress the ported map's lumps and packed files, like bspzip -repack -compress.", Order = 10)]
    public bool Compress { get; set; } = true;

    [PortOption("Source game folder", "The source game's folder holding gameinfo.txt, for game files the map relies on (e.g. shared soundscapes). Defaults to the folder above the input's maps folder.", Order = 20, Path = PathKind.Folder, Advanced = true)]
    public string? GameDirectory { get; set; }

    [PortOption("Pack assets", "Run bspPack after porting so the map carries the assets it needs from the source game.", Group = "After porting", Order = 0)]
    public bool Pack { get; set; } = true;

    [PortOption("Install into Momentum", "Copy the finished map into Momentum Mod's maps folder.", Group = "After porting", Order = 10)]
    public bool Install { get; set; } = true;

    [PortOption("Overwrite existing maps", "Replace a map of the same name that is already in Momentum's game folder. Turn off to be asked first (Source maps) or to stop before copying anything (Quake 3 and GoldSrc maps).", Group = "After porting", Order = 20)]
    public bool Overwrite { get; set; } = true;
}
