namespace MomentumPorter.Assets.Sounds;

using MomentumPorter.Assets.Weapons;

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
/// Options for a pack that gives the player another game's movement sounds (jump, land, footsteps). The app only picks
/// the game mode, game and character (and applies <see cref="ApplyGameAccurate"/>); the command line exposes the rest.
/// </summary>
public sealed class PlayerSoundPackOptions
{
    /// <summary>
    /// A <see cref="PlayerSoundSources.Games"/> id: q3 (Quake 3), css (CS:S), csgo (CS:GO), tf2, hl2, cs16 (CS 1.6) or
    /// hl1 (Half-Life).
    /// </summary>
    public string Game { get; set; } = "q3";

    /// <summary>
    /// Quake 3: the character whose jump, fall and footstep sounds to use (its folder under sound/player/ in the pk3s;
    /// ranger when null). CS:GO: the team whose footsteps to use (ct or t).
    /// </summary>
    public string? Character { get; set; }

    // The sound choices below are the command line's; the app always uses the game's sounds for everything
    // (see ApplyGameAccurate) and leaves them off its form.

    /// <summary>
    /// Game plays the game's jump sound: Quake 3's is the character's jump1.wav. The other games play the footstep when
    /// jumping, as Momentum does, so they keep Momentum's (Pure mutes it). Momentum only plays it in Defrag modes unless
    /// mom_movement_sound_jump is on.
    /// </summary>
    public JumpSoundMode JumpSound { get; set; } = JumpSoundMode.Game;

    /// <summary>
    /// Game plays the game's landing sounds: Quake 3's land1.wav on soft landings and the character's fall1.wav on hard
    /// ones, CS:GO's landing sound, and the game's footsteps for the games that play those on landing. None mutes both.
    /// Momentum only plays them in Defrag modes unless mom_movement_sound_land is on.
    /// </summary>
    public LandSoundMode LandSound { get; set; } = LandSoundMode.Game;

    /// <summary>
    /// Game replaces every surface's footsteps with the game's (Quake 3: metal surfaces clank, water splashes). Silent
    /// mutes them and writes a cfg setting snd_volume_footsteps 0. Momentum plays them when walking, jumping and landing.
    /// </summary>
    public FootstepMode Footsteps { get; set; } = FootstepMode.Game;

    /// <summary>
    /// The sound when sprint starts in Accelerated Hop (MomPlayer.SprintStart; mom_ahop_sprint_sound turns it off).
    /// Game uses Half-Life 2's suit sprint for HL2 and keeps Momentum's for the other games (Pure mutes it). HalfLife2
    /// uses Half-Life 2's with any game's sounds; it comes from Momentum's own copy of HL2, so HL2 needn't be installed.
    /// </summary>
    public SprintSoundMode SprintSound { get; set; } = SprintSoundMode.Game;

    /// <summary>
    /// Also mute the sounds the game doesn't have: air jump, power slide and wall run, and the jump and sprint sounds
    /// for games without them (unless set to another sound).
    /// </summary>
    public bool Pure { get; set; }

    /// <summary>Quake 3 only: which footstep set to use. Character uses the character's own (boot, energy, flesh, mech or normal).</summary>
    public Quake3FootstepType FootstepType { get; set; } = Quake3FootstepType.Character;

    /// <summary>
    /// Use the game's footsteps for each surface (Quake 3: metal clanks and water splashes; the others: their own
    /// metal, dirt, grass, wading... sounds). Off plays its default (concrete) footsteps everywhere.
    /// </summary>
    public bool SurfaceFootsteps { get; set; } = true;

    /// <summary>
    /// The game modes the pack is for, as mode ids separated by commas (<see cref="Weapons.GameModes.All"/>: kz16, df_cpm, ...),
    /// or null for every mode. For some modes the pack only holds the changed sounds, in
    /// <c>scripts/&lt;mode&gt;/game_sounds_momentum_player.txt</c>, which the engine loads over Momentum's in that mode;
    /// for every mode it replaces Momentum's soundscript.
    /// </summary>
    public string? GameModes { get; set; }

    /// <summary>Volume of the game's jump and land sounds (0 to 1). Momentum's own jump sound is 0.1. Footsteps play at the volume Momentum's movement gives them.</summary>
    public float Volume { get; set; } = 0.35f;

    /// <summary>
    /// Name of the pack's VPK (or folder) in Momentum's custom folder. Null for <c>&lt;game&gt;_sounds</c>, plus the
    /// character or team (q3_sounds_ranger, csgo_sounds_ct).
    /// </summary>
    public string? PackName { get; set; }

    /// <summary>Write the pack as one .vpk (faster for the game to load) instead of a folder. Always on in the app.</summary>
    public bool OutputAsVpk { get; set; } = true;

    /// <summary>Where to write the pack. Null for Momentum's custom folder, which the game loads packs from.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Replace a pack of the same name that an earlier build wrote. Always on in the app.</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>Quake 3's baseq3 folder (or a single pk3; Quake Live works too). Null to find it through Steam.</summary>
    public string? Quake3Path { get; set; }

    /// <summary>The game's content folder: cstrike (CS:S or CS 1.6), csgo, tf, hl2 or valve (Half-Life). Null to find it through Steam.</summary>
    public string? GameDirectory { get; set; }

    /// <summary>Momentum Mod's install or its momentum folder. Null to find it through Steam; the app takes the Settings page's.</summary>
    public string? MomentumDirectory { get; set; }

    /// <summary>
    /// Makes the pack sound as much like the game as it can: the game's jump, land, footstep and sprint sounds on
    /// every surface, Momentum's sounds the game has nothing like muted, written as a VPK that replaces the last
    /// build. What the app always builds; the command line's defaults are the same apart from Pure.
    /// </summary>
    public void ApplyGameAccurate()
    {
        JumpSound = JumpSoundMode.Game;
        LandSound = LandSoundMode.Game;
        Footsteps = FootstepMode.Game;
        SprintSound = SprintSoundMode.Game;
        Pure = true;
        FootstepType = Quake3FootstepType.Character;
        SurfaceFootsteps = true;
        OutputAsVpk = true;
        Overwrite = true;
    }

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

    /// <summary>The modes in <see cref="GameModes"/>, or empty for every mode.</summary>
    /// <exception cref="FormatException">An id isn't a mode.</exception>
    public IReadOnlyList<GameMode> ResolveGameModes() => Weapons.GameModes.Parse(GameModes);

    /// <summary>
    /// The pack's name: the one set, else <c>&lt;game&gt;_sounds</c> plus the character or team (q3_sounds_ranger,
    /// csgo_sounds_ct) for every mode, or <c>&lt;modes&gt;_sounds</c> for some (kz16_sounds, defrag_sounds), so
    /// rebuilding a mode's sounds from another game replaces its pack rather than adding one that fights it.
    /// </summary>
    public string ResolvePackName()
    {
        if (!string.IsNullOrWhiteSpace(PackName))
            return PackName.Trim();

        // An unknown mode fails the job's first step, which says why
        IReadOnlyList<GameMode> modes;
        try
        {
            modes = ResolveGameModes();
        }
        catch (FormatException)
        {
            modes = [];
        }

        if (modes.Count > 0)
        {
            bool defrag = Weapons.GameModes.All.Where(x => x.IsDefrag).All(modes.Contains) && modes.All(x => x.IsDefrag);
            return $"{(defrag ? "defrag" : string.Join("_", modes.Select(x => x.Id)))}_sounds";
        }

        string? character = ResolveCharacter();
        return $"{Game.Trim().ToLowerInvariant()}_sounds{(character is null ? "" : "_" + character)}";
    }
}
