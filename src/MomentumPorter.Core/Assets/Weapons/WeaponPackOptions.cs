namespace MomentumPorter.Assets.Weapons;

public enum WeaponArms
{
    /// <summary>What suits the source game's view models: their own arms, or for CS:GO the game's arms model.</summary>
    Auto,

    /// <summary>Draw Momentum's arms on the weapon (attach_to_hands 1).</summary>
    Momentum,

    /// <summary>No extra arms (attach_to_hands 0).</summary>
    None,
}

/// <summary>
/// Options for porting a weapon from another game into a Momentum weapon slot as a custom pack. The app sets the
/// game, weapon, slot and modes from its lists and keeps the defaults for the rest; the command line exposes them all.
/// </summary>
public sealed class WeaponPackOptions
{
    /// <summary>The game the weapon comes from: css, tf2, csgo, hl2 (Momentum mounts these, so only scripts are written), cs16, hl1 (GoldSrc, models and sounds are converted) or q3.</summary>
    public string Game { get; set; } = "css";

    /// <summary>The weapon's id in the catalog (e.g. ak47, rocketlauncher). List them with <c>weapon --list</c>.</summary>
    public string Weapon { get; set; } = "";

    /// <summary>The Momentum weapon it replaces (pistol, machinegun, shotgun, sniper, knife, grenade, rocketlauncher, stickylauncher, concgrenade or a df_ weapon). Null for the weapon's best fit.</summary>
    public string? Slot { get; set; }

    /// <summary>
    /// The game modes the weapon is used in, as a list of their ids (surf, bhop, bhophl, kz, kzt, kz16, rj, sj, ahop,
    /// conc, df_cpm, df_vq3, df_vtg). Null or empty means every mode. A weapon for some modes is written as
    /// scripts/&lt;mode&gt;/weapon_momentum_&lt;slot&gt;.txt, which Momentum builds with the gamemode-weapon-scripts
    /// engine change (docs/engine-changes.md) use in that mode, so e.g. Climb (1.6) can have a CS 1.6 pistol and
    /// Climb (KZT) a CS:GO one.
    /// </summary>
    public string? GameModes { get; set; }

    /// <summary>
    /// Give the slot back to Momentum in <see cref="GameModes"/> (every mode when empty) instead of porting a weapon
    /// into it: the pack's scripts and sound entries for it there are removed. <see cref="Game"/> and
    /// <see cref="Weapon"/> aren't used.
    /// </summary>
    public bool Restore { get; set; }

    /// <summary>
    /// Name of the pack folder, as it would sit in momentum/custom/. Null for momentumporter, which every weapon pack
    /// shares: packs in one folder merge the soundscript and particle manifest they'd otherwise each replace, while
    /// packs in separate folders hide each other's.
    /// </summary>
    public string? PackName { get; set; }

    /// <summary>
    /// Folder the pack is written into (the pack is a folder inside it). Momentum's custom folder, which the game loads
    /// from, in the app; anywhere else, copy the pack into momentum/custom/ to use it.
    /// </summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>Replace the weapon's sounds with the source game's.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>
    /// Which arms are drawn on the view model. Auto keeps the arms the source game's models have, and gives CS:GO's
    /// armless view models CS:GO's arms. Momentum draws Momentum's arms, which only fit Momentum's own view models.
    /// </summary>
    public WeaponArms Arms { get; set; } = WeaponArms.Auto;

    /// <summary>Use the source game's projectile model (rocket, stickybomb, grenade) where it has one.</summary>
    public bool Projectile { get; set; } = true;

    /// <summary>
    /// Recompile the source game's world (third person) model so it attaches to Momentum's player's hand. Other games'
    /// world models are rigged to their own player skeletons and otherwise sit at the player's feet.
    /// </summary>
    public bool FitWorldModel { get; set; } = true;

    /// <summary>Use the source game's particle effects (explosions, trails) where the catalog lists them.</summary>
    public bool Particles { get; set; } = true;

    /// <summary>
    /// Quake 3 only: the FOV Quake 3's gun is drawn with. Quake 3 draws it with the world's FOV (cg_fov) and lowers it
    /// at FOVs above 90; Momentum draws view models at fov_viewmodel 54, which hides most of a Quake 3 gun. Null uses
    /// the player's fov_desired.
    /// </summary>
    public int? GunFov { get; set; }

    /// <summary>
    /// Quake 3 only, for packs written into momentum/custom/: adds a marked block to the configs of the weapon's game
    /// modes (cfg/df_cpm.cfg, df_vq3.cfg and df_vtg.cfg for a df_ weapon used in every mode) that runs the pack's view
    /// model config, and one to cfg/game.cfg that puts the player's fov_viewmodel and view model offsets back in other modes.
    /// </summary>
    public bool SetInDefragModes { get; set; } = true;

    /// <summary>Momentum Mod's install folder (the one holding momentum/ and bin/). Null to find it through Steam.</summary>
    public string? MomentumDirectory { get; set; }

    /// <summary>The source game's content folder (e.g. cstrike, tf, baseq3). Null to find it through Steam.</summary>
    public string? GameDirectory { get; set; }

    /// <summary>studiomdl.exe used to compile converted models. Null for Momentum's.</summary>
    public string? StudioMdlPath { get; set; }
}
