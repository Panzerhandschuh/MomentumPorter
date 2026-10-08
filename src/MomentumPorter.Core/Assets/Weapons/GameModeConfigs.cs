namespace MomentumPorter.Assets.Weapons;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Momentum's per-mode configs: it runs <c>cfg/game.cfg</c> when a map loads (server side, first) and then the game
/// mode's own config (client side, after entities spawn), e.g. <c>cfg/df_cpm.cfg</c>. These are the player's own
/// files, so the porter only adds or replaces a marked block in them and leaves the rest alone.
/// </summary>
public static partial class GameModeConfigs
{
    public const string GameConfig = "game.cfg";

    /// <summary>The Defrag modes' configs (df_cpm.cfg, df_vq3.cfg, df_vtg.cfg).</summary>
    public static readonly IReadOnlyList<string> DefragConfigs = GameModes.All.Where(x => x.IsDefrag).Select(x => x.ConfigFile).ToList();

    private const string BlockEnd = "// <<< MomentumPorter";

    private static string BlockStart(string id) => $"// >>> MomentumPorter: {id} (remove this block to undo)";

    /// <summary>Replaces the block with this id, or appends it.</summary>
    public static string SetBlock(string text, string id, IEnumerable<string> lines)
    {
        string block = string.Join('\n', [BlockStart(id), .. lines, BlockEnd]);
        (int start, int end)? existing = FindBlock(text, id);
        if (existing is { } range)
            return text[..range.start] + block + text[range.end..];

        string trimmed = text.TrimEnd();
        return (trimmed.Length > 0 ? trimmed + "\n\n" : "") + block + "\n";
    }

    /// <summary>The lines of the block with this id, or null.</summary>
    public static IReadOnlyList<string>? GetBlock(string text, string id)
    {
        if (FindBlock(text, id) is not { } range)
            return null;

        return text[range.start..range.end].Split('\n').Select(x => x.Trim()).Skip(1).SkipLast(1).ToList();
    }

    /// <summary>The value a config sets a cvar to (the last time it sets it), or null.</summary>
    public static string? ReadCvar(string text, string name)
    {
        string? value = null;
        foreach (Match match in CvarLine().Matches(text))
        {
            if (match.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                value = match.Groups["value"].Value;
        }

        return value;
    }

    public static float? ReadFloat(string text, string name) =>
        ReadCvar(text, name) is { } value && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : null;

    private static (int start, int end)? FindBlock(string text, string id)
    {
        int start = text.IndexOf(BlockStart(id), StringComparison.Ordinal);
        if (start < 0)
            return null;

        int end = text.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        return end < 0 ? null : (start, end + BlockEnd.Length);
    }

    [GeneratedRegex("""^[ \t]*(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]+"?(?<value>[^"\s;]+)"?""", RegexOptions.Multiline)]
    private static partial Regex CvarLine();
}
