namespace MomentumPorter.Assets.Weapons;

/// <summary>
/// A Momentum game mode (engine/momentum/mom_system_gamemode.h). <see cref="Id"/> is the mode's config name without
/// its extension (<c>kz16</c> for <c>cfg/kz16.cfg</c>), which is also the folder the engine looks in for the mode's
/// own weapon scripts (<c>scripts/kz16/weapon_momentum_pistol.txt</c>, see docs/engine-patches).
/// </summary>
public sealed record GameMode(string Id, string Name)
{
    public string ConfigFile => Id + ".cfg";

    public bool IsDefrag => Id.StartsWith("df_", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

public static class GameModes
{
    public static readonly IReadOnlyList<GameMode> All =
    [
        new("surf", "Surf"),
        new("bhop", "Bhop"),
        new("bhophl", "Bhop (HL1)"),
        new("kz", "Climb (Momentum)"),
        new("kzt", "Climb (KZT)"),
        new("kz16", "Climb (1.6)"),
        new("rj", "Rocket Jump"),
        new("sj", "Sticky Jump"),
        new("ahop", "Ahop"),
        new("conc", "Conc"),
        new("df_cpm", "Defrag CPM"),
        new("df_vq3", "Defrag VQ3"),
        new("df_vtg", "Defrag VTG"),
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
