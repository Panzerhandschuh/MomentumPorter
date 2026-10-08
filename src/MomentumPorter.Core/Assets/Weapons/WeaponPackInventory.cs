namespace MomentumPorter.Assets.Weapons;

/// <summary>A weapon script in a pack: which Momentum weapon it replaces, in which mode (null: every mode), and what it is.</summary>
public sealed record WeaponPackEntry(WeaponSlot Slot, GameMode? Mode, string Description);

/// <summary>
/// Reads which weapons a pack folder holds from its weapon scripts (<c>scripts/weapon_momentum_*.txt</c> for every
/// mode, <c>scripts/&lt;mode&gt;/weapon_momentum_*.txt</c> per mode). The description is the script's first comment
/// line, which the porter writes as "&lt;weapon&gt; from &lt;game&gt; as Momentum's &lt;slot&gt; in &lt;modes&gt;".
/// </summary>
public static class WeaponPackInventory
{
    private const string MadeBy = ", made by MomentumPorter";

    public static IReadOnlyList<WeaponPackEntry> Read(string packDirectory)
    {
        var entries = new List<WeaponPackEntry>();
        string scripts = Path.Combine(packDirectory, "scripts");
        if (!Directory.Exists(scripts))
            return entries;

        foreach (WeaponSlot slot in WeaponSlots.All)
        {
            Add(entries, slot, null, Path.Combine(scripts, $"weapon_momentum_{slot.Id}.txt"));
            foreach (GameMode mode in GameModes.All)
                Add(entries, slot, mode, Path.Combine(scripts, mode.Id, $"weapon_momentum_{slot.Id}.txt"));
        }

        return entries;
    }

    private static void Add(List<WeaponPackEntry> entries, WeaponSlot slot, GameMode? mode, string path)
    {
        if (!File.Exists(path))
            return;

        string description = "a weapon script";
        try
        {
            if (File.ReadLines(path).FirstOrDefault() is { } first && first.TrimStart().StartsWith("//"))
            {
                description = first.TrimStart().TrimStart('/').Trim();
                int madeBy = description.IndexOf(MadeBy, StringComparison.OrdinalIgnoreCase);
                if (madeBy > 0)
                    description = description[..madeBy];

                int asSlot = description.IndexOf(" as Momentum's ", StringComparison.OrdinalIgnoreCase);
                if (asSlot > 0)
                    description = description[..asSlot];
            }
        }
        catch (IOException)
        {
        }

        entries.Add(new WeaponPackEntry(slot, mode, description));
    }
}
