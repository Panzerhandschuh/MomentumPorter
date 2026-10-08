namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Options;

public enum WeaponArms
{
    /// <summary>What suits the source game's view models (their own arms, or Momentum's on CS:GO's).</summary>
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

    [PortOption("Pack name", "Name of the pack folder, as it would sit in momentum/custom/. Defaults to <game>_<weapon>_<slot>.", Order = 30)]
    public string? PackName { get; set; }

    [PortOption("Output folder", "Folder the pack is written into (the pack is a folder inside it). Copy it into momentum/custom/ to use it.", Order = 40, Path = PathKind.Folder)]
    public string OutputDirectory { get; set; } = "";

    [PortOption("Sounds", "Replace the weapon's sounds with the source game's.", Group = "Weapon", Order = 0)]
    public bool Sounds { get; set; } = true;

    [PortOption("Arms", "Whether Momentum's arms are drawn on the view model. Auto keeps the arms the source game's models have (CS:GO's have none, so Momentum's are used).", Group = "Weapon", Order = 10)]
    public WeaponArms Arms { get; set; } = WeaponArms.Auto;

    [PortOption("Projectile model", "Use the source game's projectile model (rocket, stickybomb, grenade) where it has one.", Group = "Weapon", Order = 20)]
    public bool Projectile { get; set; } = true;

    [PortOption("Particles", "Use the source game's particle effects (explosions, trails) where the catalog lists them.", Group = "Weapon", Order = 30)]
    public bool Particles { get; set; } = true;

    [PortOption("Momentum install", "Momentum Mod's install folder (the one holding momentum/ and bin/). Found through Steam when empty.", Group = "Advanced", Order = 0, Path = PathKind.Folder, Advanced = true)]
    public string? MomentumDirectory { get; set; }

    [PortOption("Source game folder", "The source game's content folder (e.g. cstrike, tf, baseq3). Found through Steam when empty.", Group = "Advanced", Order = 10, Path = PathKind.Folder, Advanced = true)]
    public string? GameDirectory { get; set; }

    [PortOption("studiomdl", "studiomdl.exe used to compile converted models. Defaults to Momentum's.", Group = "Advanced", Order = 20, Path = PathKind.File, Advanced = true)]
    public string? StudioMdlPath { get; set; }
}
