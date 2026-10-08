namespace PlayerModelSpike;

/// <summary>
/// Source bone name -> target (Momentum) bone name. File format, one bone per line:
/// <code>
/// source_bone   target_bone   [aim=source_child] [place=target_bone]   // comment
/// source_bone   -                                   // dropped: its weights go to the nearest mapped ancestor
/// </code>
/// Source bones that aren't listed are dropped too. aim= picks the child whose joint the bone points at when there are
/// several mapped children (e.g. the chest points at the neck, not at a clavicle); aim=- keeps the bone unturned. place= puts the source joint on another
/// target bone's joint than the one that gets the weights, for chains whose joints don't line up one to one (ValveBiped's
/// Spine4 starts at the base of the neck but its vertices belong with Momentum's chest bone).
/// </summary>
public sealed class BoneMap
{
    public Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Aims { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Places { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static BoneMap Read(string path)
    {
        var map = new BoneMap();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line[..comment];
            var parts = Tokenize(line);
            if (parts.Count < 2)
                continue;

            if (parts[1] != "-")
                map.Targets[parts[0]] = parts[1];
            foreach (string extra in parts.Skip(2))
            {
                if (extra.StartsWith("aim=", StringComparison.OrdinalIgnoreCase))
                    map.Aims[parts[0]] = extra[4..];
                else if (extra.StartsWith("place=", StringComparison.OrdinalIgnoreCase))
                    map.Places[parts[0]] = extra[6..];
            }
        }

        return map;
    }

    /// <summary>Splits on whitespace; double quotes group names with spaces (GoldSrc "Bip01 L Thigh"), also after aim=</summary>
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                    tokens.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
            tokens.Add(current.ToString());
        return tokens;
    }
}
