namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Options;

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
/// Options for porting a weapon from another game into a Momentum weapon slot as a custom pack.
/// </summary>
public sealed class WeaponPackOptions
{
    [PortOption("Source game", "The game the weapon comes from: css, tf2, csgo, hl2 (Momentum mounts these, so only scripts are written), cs16, hl1 (GoldSrc, models and sounds are converted) or q3.", Order = 0)]
    public string Game { get; set; } = "css";

    [PortOption("Weapon", "The weapon's id in the catalog (e.g. ak47, rocketlauncher). List them with `weapon --list`.", Order = 10)]
    public string Weapon { get; set; } = "";

    [PortOption("Momentum weapon", "The Momentum weapon it replaces (pistol, machinegun, shotgun, sniper, knife, grenade, rocketlauncher, stickylauncher, concgrenade or a df_ weapon). Defaults to the weapon's best fit.", Order = 20)]
    public string? Slot { get; set; }

    [PortOption("Pack name", "Name of the pack folder, as it would sit in momentum/custom/. Defaults to momentumporter, which every weapon pack shares: packs in one folder merge the soundscript and particle manifest they'd otherwise each replace, while packs in separate folders hide each other's.", Order = 30)]
    public string? PackName { get; set; }

    [PortOption("Output folder", "Folder the pack is written into (the pack is a folder inside it). Copy it into momentum/custom/ to use it.", Order = 40, Path = PathKind.Folder)]
    public string OutputDirectory { get; set; } = "";

    [PortOption("Sounds", "Replace the weapon's sounds with the source game's.", Group = "Weapon", Order = 0)]
    public bool Sounds { get; set; } = true;

    [PortOption("Arms", "Which arms are drawn on the view model. Auto keeps the arms the source game's models have, and gives CS:GO's armless view models CS:GO's arms. Momentum draws Momentum's arms, which only fit Momentum's own view models.", Group = "Weapon", Order = 10)]
    public WeaponArms Arms { get; set; } = WeaponArms.Auto;

    [PortOption("Projectile model", "Use the source game's projectile model (rocket, stickybomb, grenade) where it has one.", Group = "Weapon", Order = 20)]
    public bool Projectile { get; set; } = true;

    [PortOption("Fit world model", "Recompile the source game's world (third person) model so it attaches to Momentum's player's hand. Other games' world models are rigged to their own player skeletons and otherwise sit at the player's feet.", Group = "Weapon", Order = 25)]
    public bool FitWorldModel { get; set; } = true;

    [PortOption("Particles", "Use the source game's particle effects (explosions, trails) where the catalog lists them.", Group = "Weapon", Order = 30)]
    public bool Particles { get; set; } = true;

    [PortOption("Gun FOV", "Quake 3 only: the FOV Quake 3's gun is drawn with. Quake 3 draws it with the world's FOV (cg_fov) and lowers it at FOVs above 90; Momentum draws view models at fov_viewmodel 54, which hides most of a Quake 3 gun. Empty uses your fov_desired.", Group = "Quake 3", Order = 0)]
    public int? GunFov { get; set; }

    [PortOption("Set it in Defrag modes", "Quake 3 only, for packs written into momentum/custom/: adds a marked block to cfg/df_cpm.cfg, df_vq3.cfg and df_vtg.cfg that runs the pack's view model config, and one to cfg/game.cfg that puts your fov_viewmodel and viewmodel offsets back in other modes.", Group = "Quake 3", Order = 10)]
    public bool SetInDefragModes { get; set; } = true;

    [PortOption("Momentum install", "Momentum Mod's install folder (the one holding momentum/ and bin/). Found through Steam when empty.", Group = "Advanced", Order = 0, Path = PathKind.Folder, Advanced = true)]
    public string? MomentumDirectory { get; set; }

    [PortOption("Source game folder", "The source game's content folder (e.g. cstrike, tf, baseq3). Found through Steam when empty.", Group = "Advanced", Order = 10, Path = PathKind.Folder, Advanced = true)]
    public string? GameDirectory { get; set; }

    [PortOption("studiomdl", "studiomdl.exe used to compile converted models. Defaults to Momentum's.", Group = "Advanced", Order = 20, Path = PathKind.File, Advanced = true)]
    public string? StudioMdlPath { get; set; }
}
