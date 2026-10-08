namespace MomentumPorter.Assets.Weapons;

/// <summary>
/// A Momentum weapon a ported weapon can replace (<c>scripts/weapon_momentum_&lt;Id&gt;.txt</c>).
/// </summary>
/// <param name="ProjectileKey">The ModelData key of the projectile it fires or throws, if it has its own model</param>
public sealed record WeaponSlot(string Id, string Name, string? ProjectileKey = null)
{
    public string ScriptPath => $"scripts/weapon_momentum_{Id}.txt";

    /// <summary>The server's copy, which names the soundscript entry for each of the weapon's sounds.</summary>
    public string ServerScriptPath => $"scripts/server_weapon/weapon_momentum_{Id}.txt";
}

public static class WeaponSlots
{
    public static readonly IReadOnlyList<WeaponSlot> All =
    [
        new("pistol", "Pistol"),
        new("machinegun", "Machine gun"),
        new("shotgun", "Shotgun"),
        new("sniper", "Sniper"),
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
