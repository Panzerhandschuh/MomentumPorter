namespace MomentumPorter.Assets.Weapons;

/// <summary>
/// A Momentum weapon script (<c>scripts/weapon_momentum_&lt;slot&gt;.txt</c>): its models, its sound overrides and its
/// particles. Edits keep everything else in the base script (crosshair, HUD textures, bucket...).
/// </summary>
public sealed class WeaponScript
{
    /// <summary>ModelData keys that aren't projectiles.</summary>
    public static readonly IReadOnlyList<string> NonProjectileModelKeys = ["view", "view_side", "world", "spawner", "surprise"];

    private WeaponScript(KeyValuesNode root)
    {
        Root = root;
    }

    /// <summary>The <c>WeaponData</c> block.</summary>
    public KeyValuesNode Root { get; }

    public static WeaponScript Parse(string text, string? fileName = null) => new(KeyValuesNode.Parse(text, fileName));

    public KeyValuesNode ModelData => Root.GetOrAddBlock("ModelData");

    public KeyValuesNode SoundData => Root.GetOrAddBlock("SoundData");

    /// <summary>
    /// How the view model gets arms: 0 none (the model has its own), 1 Momentum's arms are bone-merged onto the
    /// weapon (CS:GO style, Momentum's own models), 2 the world model is merged onto arms (TF2 c_models).
    /// </summary>
    public int AttachToHands
    {
        get => int.TryParse(Root.GetString("attach_to_hands"), out int value) ? value : 0;
        set => Root.Set("attach_to_hands", value.ToString(System.Globalization.CultureInfo.InvariantCulture), after: "weight");
    }

    /// <summary>
    /// Whether the view model is modelled for the right hand. Momentum mirrors it when this differs from
    /// <c>cl_righthand</c>.
    /// </summary>
    public bool BuiltRightHanded
    {
        get => Root.GetString("BuiltRightHanded") != "0";
        set => Root.Set("BuiltRightHanded", value ? "1" : "0");
    }

    public string? GetModel(string key)
    {
        KeyValuesNode? node = ModelData.Find(key);
        return node is null ? null : node.IsBlock ? node.GetString("path") : node.Value;
    }

    public void SetModel(string key, string path) => ModelData.Set(key, path);

    public bool RemoveModel(string key) => ModelData.Remove(key);

    /// <summary>The ModelData keys of projectiles (rocket, sticky, grenade...).</summary>
    public IEnumerable<string> ProjectileModelKeys =>
        ModelData.Children.Select(x => x.Key).Where(x => !NonProjectileModelKeys.Contains(x, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string> SoundOverrides =>
        SoundData.Find("Overrides") is { IsBlock: true } overrides
            ? overrides.Children.Where(x => !x.IsBlock).ToDictionary(x => x.Key, x => x.Value!, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();

    /// <summary>
    /// Plays <paramref name="entry"/> wherever the weapon plays <paramref name="soundscriptEntry"/>. An empty entry
    /// silences it.
    /// </summary>
    public void SetSoundOverride(string soundscriptEntry, string entry) => SoundData.GetOrAddBlock("Overrides").Set(soundscriptEntry, entry);

    public string? GetParticle(string key) => Root.Find("ParticleData")?.GetString(key);

    public void SetParticle(string key, string particleSystem) => Root.GetOrAddBlock("ParticleData").Set(key, particleSystem);

    public string Write(string? header = null)
    {
        string text = Root.Write();
        return header is null ? text : string.Join('\n', header.Split('\n').Select(x => "// " + x)) + "\n" + text;
    }

    /// <summary>
    /// Which soundscript entry the weapon plays for each of its sounds (<c>single_shot</c> -> <c>Weapon_SMG1.Single</c>),
    /// from the server's copy of a script. These entries are what <c>SoundData/Overrides</c> replaces.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadSoundEntries(string serverScriptText, string? fileName = null)
    {
        KeyValuesNode root = KeyValuesNode.Parse(serverScriptText, fileName);
        return root.Find("SoundData") is { IsBlock: true } sounds
            ? sounds.Children.Where(x => !x.IsBlock).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().Value!, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>();
    }
}
