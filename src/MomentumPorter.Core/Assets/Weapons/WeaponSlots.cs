namespace MomentumPorter.Assets.Weapons;

/// <summary>
/// A Momentum weapon a ported weapon can replace (<c>scripts/weapon_momentum_&lt;Id&gt;.txt</c>).
/// </summary>
/// <param name="ProjectileKey">The ModelData key of the projectile it fires or throws, if it has its own model</param>
/// <param name="ShotFromPlayer">
/// Momentum plays the weapon's shot (FX_FireBullets: its single_shot sound) with the player as the source rather than
/// the weapon, so a weapon script's SoundData Overrides only apply to it on Momentum builds with
/// docs/engine-patches/gamemode-weapon-scripts.patch.
/// </param>
public sealed record WeaponSlot(string Id, string Name, string? ProjectileKey = null, bool ShotFromPlayer = false)
{
    public string ScriptPath => $"scripts/weapon_momentum_{Id}.txt";

    /// <summary>
    /// The script the engine uses for this weapon in one game mode (with docs/engine-patches/gamemode-weapon-scripts.patch),
    /// or <see cref="ScriptPath"/>, the one for every mode, when <paramref name="mode"/> is null.
    /// </summary>
    public string ScriptPathFor(GameMode? mode) => mode is null ? ScriptPath : $"scripts/{mode.Id}/weapon_momentum_{Id}.txt";

    /// <summary>The server's copy, which names the soundscript entry for each of the weapon's sounds.</summary>
    public string ServerScriptPath => $"scripts/server_weapon/weapon_momentum_{Id}.txt";
}

public static class WeaponSlots
{
    public static readonly IReadOnlyList<WeaponSlot> All =
    [
        new("pistol", "Pistol", ShotFromPlayer: true),
        new("machinegun", "Machine gun", ShotFromPlayer: true),
        new("shotgun", "Shotgun", ShotFromPlayer: true),
        new("sniper", "Sniper", ShotFromPlayer: true),
        new("knife", "Knife"),
        new("grenade", "Grenade"),
        new("rocketlauncher", "Rocket launcher", "rocket"),
        new("stickylauncher", "Stickybomb launcher", "sticky"),
        new("concgrenade", "Concussion grenade"),
        new("df_bfg", "Defrag BFG", "bfg_rocket"),
        new("df_grenadelauncher", "Defrag grenade launcher", "grenade"),
        new("df_knife", "Defrag gauntlet"),
        new("df_lightninggun", "Defrag lightning gun"),
        new("df_machinegun", "Defrag machine gun"),
        new("df_plasmagun", "Defrag plasma gun", "plasma"),
        new("df_railgun", "Defrag railgun"),
        new("df_rocketlauncher", "Defrag rocket launcher", "rocket"),
        new("df_shotgun", "Defrag shotgun"),
    ];

    public static WeaponSlot? Find(string id) => All.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
