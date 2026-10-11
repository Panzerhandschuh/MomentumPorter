namespace MomentumPorter.Assets.Weapons;

/// <summary>
/// A Momentum game mode (engine/momentum/mom_system_gamemode.h). <see cref="Id"/> is the mode's config name without
/// its extension (<c>kz16</c> for <c>cfg/kz16.cfg</c>), which is also the folder the engine looks in for the mode's
/// own weapon scripts (<c>scripts/kz16/weapon_momentum_pistol.txt</c>).
/// </summary>
/// <param name="SlotIds">The Momentum weapons the mode lets the player have (CGameMode_*::WeaponDesirability).</param>
public sealed record GameMode(string Id, string Name, IReadOnlyList<string> SlotIds)
{
    public string ConfigFile => Id + ".cfg";

    public bool IsDefrag => Id.StartsWith("df_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mode's weapons, in Momentum's weapon order.</summary>
    public IReadOnlyList<WeaponSlot> Slots => WeaponSlots.All.Where(x => SlotIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).ToList();

    public override string ToString() => Name;
}

/// <summary>
/// Game modes that share their weapons, picked as one on the Weapons page: the three Defrag modes are one entry.
/// </summary>
/// <param name="DefaultGame">The catalog game the Weapons page switches to for the mode: the game it comes from</param>
/// <param name="Picks">Per game id, Momentum weapon -> weapon id where the game's own loadout isn't the mode's (TF2's Demoman bottle in Sticky Jump)</param>
public sealed record GameModeGroup(
    string Name,
    IReadOnlyList<GameMode> Modes,
    string DefaultGame = "css",
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Picks = null)
{
    /// <summary>The mode's own weapon picks for a game, or null to go by the game's loadout.</summary>
    public IReadOnlyDictionary<string, string>? PicksFor(string gameId) =>
        Picks?.FirstOrDefault(x => x.Key.Equals(gameId, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>The weapons the modes have, in Momentum's weapon order.</summary>
    public IReadOnlyList<WeaponSlot> Slots => WeaponSlots.All.Where(x => Modes.Any(m => m.SlotIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))).ToList();

    /// <summary>The modes as <see cref="WeaponPackOptions.GameModes"/> takes them.</summary>
    public string ModeList => GameModes.ToList(Modes)!;

    public override string ToString() => Name;
}

public static class GameModes
{
    private static readonly string[] ClassicWeapons = ["pistol", "machinegun", "shotgun", "sniper", "grenade", "knife"];
    private static readonly string[] DefragWeapons = ["df_bfg", "df_grenadelauncher", "df_knife", "df_lightninggun", "df_machinegun", "df_plasmagun", "df_railgun", "df_rocketlauncher", "df_shotgun"];

    public static readonly IReadOnlyList<GameMode> All =
    [
        new("surf", "Surf", ClassicWeapons),
        new("bhop", "Bhop", ClassicWeapons),
        new("bhophl", "Bhop (HL1)", ClassicWeapons),
        new("kz", "Climb (Momentum)", ClassicWeapons),
        new("kzt", "Climb (KZT)", ClassicWeapons),
        new("kz16", "Climb (1.6)", ClassicWeapons),
        new("rj", "Rocket Jump", ["rocketlauncher", "shotgun", "knife"]),
        new("sj", "Sticky Jump", ["stickylauncher", "pistol", "knife"]),
        new("ahop", "Ahop", ClassicWeapons),
        new("conc", "Conc", ["concgrenade"]),
        new("df_cpm", "Defrag CPM", DefragWeapons),
        new("df_vq3", "Defrag VQ3", DefragWeapons),
        new("df_vtg", "Defrag VTG", DefragWeapons),
    ];

    /// <summary>
    /// The modes a weapon pack is built for, as the Weapons page offers them: every mode but Climb (Momentum), which
    /// isn't played, with the Defrag modes (which share their weapons) as one entry. Each starts on the game the mode
    /// comes from: Surf and Bhop on CS:S, Bhop (HL1) on Half-Life, Climb (KZT) on CS:GO, Climb (1.6) on CS 1.6, Rocket
    /// Jump on the Soldier's weapons and Sticky Jump on the Demoman's (TF2), Ahop on Half-Life 2, Conc on Half-Life
    /// (until Team Fortress Classic is supported) and Defrag on Quake 3.
    /// </summary>
    public static readonly IReadOnlyList<GameModeGroup> Groups =
    [
        .. All.Where(x => !x.IsDefrag && x.Id != "kz").Select(x => x.Id switch
        {
            "rj" => new GameModeGroup(x.Name, [x], "tf2", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["tf2"] = new Dictionary<string, string> { ["rocketlauncher"] = "rocketlauncher", ["shotgun"] = "shotgun", ["knife"] = "shovel" },
            }),
            "sj" => new GameModeGroup(x.Name, [x], "tf2", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["tf2"] = new Dictionary<string, string> { ["stickylauncher"] = "stickybomblauncher", ["knife"] = "bottle" },
            }),
            "bhophl" => new GameModeGroup(x.Name, [x], "hl1"),
            "kzt" => new GameModeGroup(x.Name, [x], "csgo"),
            "kz16" => new GameModeGroup(x.Name, [x], "cs16"),
            "ahop" => new GameModeGroup(x.Name, [x], "hl2"),
            "conc" => new GameModeGroup(x.Name, [x], "hl1"),
            _ => new GameModeGroup(x.Name, [x]),
        }),
        new GameModeGroup("Defrag", All.Where(x => x.IsDefrag).ToList(), "q3"),
    ];

    public static GameMode? Find(string id) => All.FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The modes in a list of ids separated by commas or spaces, in catalog order and without repeats. Empty means
    /// every mode (the result is empty too).
    /// </summary>
    /// <exception cref="FormatException">An id isn't a mode.</exception>
    public static IReadOnlyList<GameMode> Parse(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
            return [];

        var picked = new HashSet<GameMode>();
        foreach (string id in list.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            picked.Add(Find(id) ?? throw new FormatException($"Unknown game mode \"{id}\". Known: {string.Join(", ", All.Select(x => x.Id))}"));
        }

        return All.Where(picked.Contains).ToList();
    }

    /// <summary>The modes as a list of ids, or null for every mode.</summary>
    public static string? ToList(IEnumerable<GameMode> modes)
    {
        string list = string.Join(",", modes.Select(x => x.Id));
        return list.Length == 0 ? null : list;
    }

    /// <summary>"every mode", or the modes' names.</summary>
    public static string Describe(IReadOnlyList<GameMode> modes) => modes.Count == 0 ? "every mode" : string.Join(", ", modes.Select(x => x.Name));
}
