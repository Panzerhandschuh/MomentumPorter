namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Options;

public enum JumpSoundMode
{
    /// <summary>Leave Momentum's jump sound.</summary>
    MomentumDefault,

    /// <summary>The game's jump sound (Quake 3: the character's jump1.wav). Games without one keep Momentum's.</summary>
    Game,

    /// <summary>No jump sound.</summary>
    None,
}

public enum LandSoundMode
{
    MomentumDefault,

    /// <summary>
    /// The game's landing sounds: Quake 3's land1.wav for soft landings and the character's fall1.wav for hard ones,
    /// CS:GO's landing sound, and the footsteps for games that play those on landing.
    /// </summary>
    Game,

    /// <summary>No landing sounds.</summary>
    None,
}

public enum FootstepMode
{
    MomentumDefault,

    /// <summary>The game's footsteps on every surface.</summary>
    Game,

    /// <summary>No footsteps.</summary>
    Silent,
}

public enum SprintSoundMode
{
    /// <summary>The game's sprint sound: Half-Life 2's suit sprint for HL2. The other games have none and keep Momentum's.</summary>
    Game,

    /// <summary>Leave Momentum's sprint sound.</summary>
    MomentumDefault,

    /// <summary>Half-Life 2's suit sprint sound whatever the game, from Momentum's own copy of HL2.</summary>
    HalfLife2,

    /// <summary>No sprint sound.</summary>
    None,
}

/// <summary>
/// Which of Quake 3's footstep sets to use. In Quake 3 it's a property of the character (models/players/x/animation.cfg).
/// </summary>
public enum Quake3FootstepType
{
    /// <summary>The character's own, from its animation.cfg.</summary>
    Character,
    Normal,
    Boot,
    Energy,
    Flesh,
    Mech,
}

/// <summary>
/// Options for a pack that gives the player another game's movement sounds (jump, land, footsteps).
/// </summary>
public sealed class PlayerSoundPackOptions
{
    /// <summary>Options only Quake 3 uses.</summary>
    public static readonly IReadOnlyList<string> Quake3Only = [nameof(FootstepType), nameof(Quake3Path)];

    /// <summary>Options Quake 3 doesn't use.</summary>
    public static readonly IReadOnlyList<string> NotQuake3 = [nameof(GameDirectory)];

    /// <summary>A <see cref="PlayerSoundSources.Games"/> id.</summary>
    [PortOption("Game", "The game whose movement sounds to use: q3 (Quake 3), css (CS:S), csgo (CS:GO), tf2, hl2, cs16 (CS 1.6) or hl1 (Half-Life).", Group = "Sounds", Order = -10)]
    public string Game { get; set; } = "q3";

    /// <summary>Folder name under sound/player/ in Quake 3's pk3s (ranger, sarge, ...), or CS:GO's team (ct, t). Null for the game's default.</summary>
    [PortOption("Character", "Quake 3: the character whose jump and fall sounds to use (sound/player/<character>/ in the pk3s; ranger when empty). CS:GO: the team whose footsteps to use (ct or t).", Group = "Sounds", Order = 0)]
    public string? Character { get; set; }

    [PortOption("Jump sound", "Game plays the game's jump sound: Quake 3's is the character's jump1.wav. The other games play the footstep when jumping, as Momentum does, so they keep Momentum's (Pure mutes it). Momentum only plays it in Defrag modes unless mom_movement_sound_jump is on.", Group = "Sounds", Order = 10)]
    public JumpSoundMode JumpSound { get; set; } = JumpSoundMode.Game;

    [PortOption("Land sound", "Game plays the game's landing sounds: Quake 3's land1.wav on soft landings and the character's fall1.wav on hard ones, CS:GO's landing sound, and the game's footsteps for the games that play those on landing. None mutes both. Momentum only plays them in Defrag modes unless mom_movement_sound_land is on.", Group = "Sounds", Order = 20)]
    public LandSoundMode LandSound { get; set; } = LandSoundMode.Game;

    [PortOption("Footsteps", "Game replaces every surface's footsteps with the game's (Quake 3: metal surfaces clank, water splashes). Silent mutes them and writes a cfg setting snd_volume_footsteps 0. Momentum plays them when walking, jumping and landing.", Group = "Sounds", Order = 30)]
    public FootstepMode Footsteps { get; set; } = FootstepMode.Game;

    [PortOption("Sprint sound", "The sound when sprint starts in Accelerated Hop (MomPlayer.SprintStart; mom_ahop_sprint_sound turns it off). Game uses Half-Life 2's suit sprint for HL2 and keeps Momentum's for the other games (Pure mutes it). HalfLife2 uses Half-Life 2's with any game's sounds; it comes from Momentum's own copy of HL2, so HL2 needn't be installed.", Group = "Sounds", Order = 35)]
    public SprintSoundMode SprintSound { get; set; } = SprintSoundMode.Game;

    [PortOption("Pure", "Also mute the sounds the game doesn't have: air jump, power slide and wall run, and the jump and sprint sounds for games without them (unless set to another sound).", Group = "Sounds", Order = 40)]
    public bool Pure { get; set; }

    [PortOption("Footstep type", "Quake 3 only: which footstep set to use. Character uses the character's own (boot, energy, flesh, mech or normal).", Group = "Sounds", Order = 50, Advanced = true)]
    public Quake3FootstepType FootstepType { get; set; } = Quake3FootstepType.Character;

    [PortOption("Surface footsteps", "Use the game's footsteps for each surface (Quake 3: metal clanks and water splashes; the others: their own metal, dirt, grass... sounds). Off plays its default (concrete) footsteps everywhere.", Group = "Sounds", Order = 60, Advanced = true)]
    public bool SurfaceFootsteps { get; set; } = true;

    [PortOption("Volume", "Volume of the game's jump and land sounds (0 to 1). Momentum's own jump sound is 0.1. Footsteps play at the volume Momentum's movement gives them.", Group = "Sounds", Order = 70, Min = 0, Max = 1, Advanced = true)]
    public float Volume { get; set; } = 0.35f;

    /// <summary>Null for <c>&lt;game&gt;_sounds[_&lt;character&gt;]</c>.</summary>
    [PortOption("Pack name", "Name of the pack's folder or VPK in Momentum's custom folder. Defaults to <game>_sounds, plus the character or team (q3_sounds_ranger, csgo_sounds_ct).", Group = "Output", Order = 0)]
    public string? PackName { get; set; }

    [PortOption("Build a VPK", "Write the pack as one .vpk (faster for the game to load) instead of a folder.", Group = "Output", Order = 10)]
    public bool OutputAsVpk { get; set; } = true;

    /// <summary>Null for Momentum's custom folder.</summary>
    [PortOption("Output folder", "Where to write the pack. Defaults to Momentum's custom folder, which the game loads packs from.", Group = "Output", Order = 20, Path = PathKind.Folder, Advanced = true)]
    public string? OutputDirectory { get; set; }

    [PortOption("Replace existing pack", "Replace a pack of the same name that an earlier build wrote.", Group = "Output", Order = 30, Advanced = true)]
    public bool Overwrite { get; set; } = true;

    /// <summary>Null to find Quake 3 (then Quake Live) through Steam.</summary>
    [PortOption("Quake 3 folder", "Quake 3's baseq3 folder (or a single pk3). Found through Steam when empty; Quake Live works too.", Group = "Game folders", Order = 0, Path = PathKind.Folder, Advanced = true)]
    public string? Quake3Path { get; set; }

    /// <summary>Null to find the game through Steam.</summary>
    [PortOption("Game folder", "The game's content folder: cstrike (CS:S or CS 1.6), csgo, tf, hl2 or valve (Half-Life). Found through Steam when empty.", Group = "Game folders", Order = 5, Path = PathKind.Folder, Advanced = true)]
    public string? GameDirectory { get; set; }

    /// <summary>Null to find Momentum through Steam.</summary>
    [PortOption("Momentum folder", "Momentum Mod's install or its momentum folder. Found through Steam when empty.", Group = "Game folders", Order = 10, Path = PathKind.Folder, Advanced = true)]
    public string? MomentumDirectory { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsQuake3 => Game.Trim().Equals("q3", StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder the game's sounds come from as set, or null to find it.</summary>
    public string? ResolveGameDirectory()
    {
        string? directory = IsQuake3 ? Quake3Path : GameDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    /// <summary>
    /// The character the pack uses: the one set, else Quake 3's ranger or CS:GO's ct. Null for the games that have one
    /// set of sounds, whatever is set.
    /// </summary>
    public string? ResolveCharacter()
    {
        string? fallback = Game.Trim().ToLowerInvariant() switch
        {
            "q3" => "ranger",
            "csgo" => "ct",
            _ => null,
        };
        if (fallback is null)
            return null;

        return string.IsNullOrWhiteSpace(Character) ? fallback : Character.Trim().ToLowerInvariant();
    }

    public string ResolvePackName()
    {
        if (!string.IsNullOrWhiteSpace(PackName))
            return PackName.Trim();

        string? character = ResolveCharacter();
        return $"{Game.Trim().ToLowerInvariant()}_sounds{(character is null ? "" : "_" + character)}";
    }
}
