namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Options;

public enum JumpSoundMode
{
    /// <summary>Leave Momentum's jump sound.</summary>
    MomentumDefault,

    /// <summary>The character's jump1.wav.</summary>
    Quake3,
}

public enum LandSoundMode
{
    MomentumDefault,

    /// <summary>land1.wav for soft landings, the character's fall1.wav for hard ones.</summary>
    Quake3,

    /// <summary>No landing sounds.</summary>
    None,
}

public enum FootstepMode
{
    MomentumDefault,

    /// <summary>Quake 3's footsteps on every surface.</summary>
    Quake3,

    /// <summary>No footsteps.</summary>
    Silent,
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
/// Options for a pack that gives the player Quake 3's movement sounds (jump, land, footsteps).
/// </summary>
public sealed class PlayerSoundPackOptions
{
    /// <summary>Folder name under sound/player/ in Quake 3's pk3s (ranger, sarge, ...).</summary>
    [PortOption("Character", "The Quake 3 character whose jump and fall sounds to use (sound/player/<character>/ in the pk3s).", Group = "Sounds", Order = 0)]
    public string Character { get; set; } = "ranger";

    [PortOption("Jump sound", "Quake3 plays the character's jump1.wav when jumping.", Group = "Sounds", Order = 10)]
    public JumpSoundMode JumpSound { get; set; } = JumpSoundMode.Quake3;

    [PortOption("Land sound", "Quake3 plays land1.wav on soft landings and the character's fall1.wav on hard ones. None mutes both.", Group = "Sounds", Order = 20)]
    public LandSoundMode LandSound { get; set; } = LandSoundMode.Quake3;

    [PortOption("Footsteps", "Quake3 replaces every surface's footsteps with Quake 3's (metal surfaces clank, water splashes). Silent mutes them and writes a cfg setting snd_volume_footsteps 0.", Group = "Sounds", Order = 30)]
    public FootstepMode Footsteps { get; set; } = FootstepMode.Quake3;

    [PortOption("Pure Quake 3", "Also mute the sounds Quake 3 doesn't have: air jump, sprint, power slide and wall run.", Group = "Sounds", Order = 40)]
    public bool PureQuake3 { get; set; }

    [PortOption("Footstep type", "Which Quake 3 footstep set to use. Character uses the character's own (boot, energy, flesh, mech or normal).", Group = "Sounds", Order = 50, Advanced = true)]
    public Quake3FootstepType FootstepType { get; set; } = Quake3FootstepType.Character;

    [PortOption("Surface footsteps", "Use Quake 3's metal (clank) and water (splash) footsteps on metal and water surfaces, as Quake 3 does.", Group = "Sounds", Order = 60, Advanced = true)]
    public bool SurfaceFootsteps { get; set; } = true;

    [PortOption("Volume", "Volume of the Quake 3 jump and land sounds (0 to 1). Momentum's own jump sound is 0.1.", Group = "Sounds", Order = 70, Min = 0, Max = 1, Advanced = true)]
    public float Volume { get; set; } = 0.35f;

    /// <summary>Null for <c>q3_sounds_&lt;character&gt;</c>.</summary>
    [PortOption("Pack name", "Name of the pack's folder or VPK in Momentum's custom folder. Defaults to q3_sounds_<character>.", Group = "Output", Order = 0)]
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

    /// <summary>Null to find Momentum through Steam.</summary>
    [PortOption("Momentum folder", "Momentum Mod's install or its momentum folder. Found through Steam when empty.", Group = "Game folders", Order = 10, Path = PathKind.Folder, Advanced = true)]
    public string? MomentumDirectory { get; set; }

    public string ResolvePackName() => string.IsNullOrWhiteSpace(PackName) ? $"q3_sounds_{Character.ToLowerInvariant()}" : PackName.Trim();
}
