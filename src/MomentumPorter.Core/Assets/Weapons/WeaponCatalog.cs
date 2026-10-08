namespace MomentumPorter.Assets.Weapons;

using System.Text.Json;
using System.Text.Json.Serialization;
using MomentumPorter.Assets.Vpk;

/// <summary>
/// The weapons the porter knows, per source game, from <c>weapons.json</c> (embedded in Core, so it can be extended
/// without code). Paths are game paths in the source game; sounds are keyed by the role Momentum gives them in its
/// server weapon scripts (single_shot, explosion, deploy, swish, ...).
/// </summary>
public sealed class WeaponCatalog
{
    private const string ResourceName = "MomentumPorter.Assets.Weapons.weapons.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static WeaponCatalog? _default;

    public List<CatalogGame> Games { get; set; } = [];

    /// <summary>The catalog shipped with the porter.</summary>
    public static WeaponCatalog Default => _default ??= LoadEmbedded();

    public static WeaponCatalog LoadEmbedded()
    {
        using Stream stream = typeof(WeaponCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} isn't embedded in {typeof(WeaponCatalog).Assembly.GetName().Name}");
        return Load(stream);
    }

    public static WeaponCatalog Load(Stream stream)
    {
        return JsonSerializer.Deserialize<WeaponCatalog>(stream, JsonOptions) ?? throw new InvalidDataException("The weapon catalog is empty");
    }

    public static WeaponCatalog Load(string json)
    {
        return JsonSerializer.Deserialize<WeaponCatalog>(json, JsonOptions) ?? throw new InvalidDataException("The weapon catalog is empty");
    }

    public CatalogGame? FindGame(string id) => Games.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Problems with the catalog itself (unknown games or slots, missing paths, duplicate ids). Empty if it's fine.
    /// Whether the paths exist in the installed games is checked when a pack is built.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (IGrouping<string, CatalogGame> duplicate in Games.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            problems.Add($"Game {duplicate.Key} is listed {duplicate.Count()} times");

        foreach (CatalogGame game in Games)
        {
            SourceGameInfo? info = SourceGames.Find(game.Id);
            if (info is null)
            {
                problems.Add($"{game.Id}: not a game the porter can open ({string.Join(", ", SourceGames.All.Select(x => x.Id))})");
                continue;
            }

            if (game.AttachToHands is < 0 or > 2)
                problems.Add($"{game.Id}: attachToHands must be 0, 1 or 2");

            foreach (IGrouping<string, CatalogWeapon> duplicate in game.Weapons.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                problems.Add($"{game.Id}: weapon {duplicate.Key} is listed {duplicate.Count()} times");

            foreach (CatalogWeapon weapon in game.Weapons)
            {
                string name = $"{game.Id}/{weapon.Id}";
                if (string.IsNullOrWhiteSpace(weapon.Name))
                    problems.Add($"{name}: no name");

                if (string.IsNullOrWhiteSpace(weapon.View))
                    problems.Add($"{name}: no view model");

                if (weapon.Slots.Count == 0)
                    problems.Add($"{name}: fits no slot");

                foreach (string slot in weapon.Slots.Where(x => WeaponSlots.Find(x) is null))
                    problems.Add($"{name}: unknown slot {slot}");

                string expectedExtension = info.Engine == ContentEngine.Quake3 ? ".md3" : ".mdl";
                foreach (string? model in new[] { weapon.View, weapon.ViewSide, weapon.World, weapon.Projectile })
                {
                    if (model is not null && !model.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{name}: {model} isn't a {expectedExtension} model");
                }

                if (info.MountedByMomentum && weapon.Waves.Count > 0)
                    problems.Add($"{name}: games Momentum mounts should use soundscript entries (sounds), not waves");

                if (!info.MountedByMomentum && weapon.Sounds.Count > 0)
                    problems.Add($"{name}: games Momentum doesn't mount have no soundscripts; use waves");

                foreach ((string role, List<string> waves) in weapon.Waves)
                {
                    if (waves.Count == 0)
                        problems.Add($"{name}: wave list for {role} is empty");

                    foreach (string wave in waves.Where(x => !x.StartsWith("sound/", StringComparison.OrdinalIgnoreCase)))
                        problems.Add($"{name}: wave {wave} should be a game path starting with sound/");
                }
            }
        }

        return problems;
    }
}

public sealed class CatalogGame
{
    /// <summary>A <see cref="SourceGames"/> id (css, tf2, csgo, hl2, cs16, hl1, q3).</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The weapon script's attach_to_hands for this game's view models (see <see cref="WeaponScript.AttachToHands"/>).</summary>
    public int AttachToHands { get; set; }

    /// <summary>Whether the view models are modelled for the right hand (CS:S's are left handed).</summary>
    public bool BuiltRightHanded { get; set; } = true;

    /// <summary>
    /// For attach_to_hands 1: the game's own arms model, written as the weapon script's ModelData "arms" so the
    /// engine bone-merges it instead of Momentum's arms (whose bones a foreign view model doesn't have).
    /// </summary>
    public string? ArmsModel { get; set; }

    /// <summary>
    /// The game's own soundscripts (game paths, read from the game, else from Momentum's folder), for entries that
    /// Momentum doesn't load or that a Momentum entry of the same name hides.
    /// </summary>
    public List<string> Soundscripts { get; set; } = [];

    /// <summary>Momentum's copies of this game's soundscripts (e.g. scripts/game_sounds_weapons_css.txt).</summary>
    public List<string> MomentumSoundscripts { get; set; } = [];

    /// <summary>
    /// Particle files of the game (in content Momentum mounts) that its weapons' ParticleData needs and Momentum's
    /// particle manifest doesn't list; the pack ships a manifest copy with them added.
    /// </summary>
    public List<string> ParticleFiles { get; set; } = [];

    /// <summary>What doesn't carry over for every weapon of this game; added to the report.</summary>
    public List<string> Limitations { get; set; } = [];

    public List<CatalogWeapon> Weapons { get; set; } = [];

    public CatalogWeapon? FindWeapon(string id) => Weapons.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class CatalogWeapon
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The Momentum weapons it can replace, best fit first.</summary>
    public List<string> Slots { get; set; } = [];

    public string View { get; set; } = "";

    /// <summary>The rocket launcher's side-firing view model; the view model if not given.</summary>
    public string? ViewSide { get; set; }

    /// <summary>Third person and dropped model.</summary>
    public string? World { get; set; }

    /// <summary>The model of what it fires or throws, used for the slot's projectile (rocket, sticky, grenade...).</summary>
    public string? Projectile { get; set; }

    /// <summary>Role -> soundscript entry (mounted games). An empty entry silences the sound.</summary>
    public Dictionary<string, string> Sounds { get; set; } = [];

    /// <summary>Role -> waves (game paths under sound/), for games without soundscripts. Several play at random.</summary>
    public Dictionary<string, List<string>> Waves { get; set; } = [];

    /// <summary>
    /// Soundscript entry prefixes (e.g. <c>Weapon_AK47.</c>) whose entries the view model's animations play; copied
    /// into the pack when Momentum doesn't load them.
    /// </summary>
    public List<string> SoundPrefixes { get; set; } = [];

    /// <summary>ParticleData key -> particle system.</summary>
    public Dictionary<string, string> Particles { get; set; } = [];

    /// <summary>Weapon specific limitations, added to the report.</summary>
    public List<string> Notes { get; set; } = [];

    [JsonIgnore]
    public IEnumerable<string> Models => new[] { View, ViewSide, World, Projectile }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase)!;
}
