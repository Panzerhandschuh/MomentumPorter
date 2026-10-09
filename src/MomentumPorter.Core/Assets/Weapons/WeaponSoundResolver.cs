namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Assets.Vpk;

/// <summary>
/// The entry a weapon should play for a sound, or why it can't.
/// </summary>
/// <param name="Entry">Soundscript entry to put in the weapon script's Overrides, or null if none works</param>
/// <param name="Added">The pack defines the entry (copied from the source game, or made from waves)</param>
public sealed record ResolvedSound(string? Entry, bool Added, string? Problem = null);

/// <summary>
/// Works out which soundscript entries a ported weapon can play in Momentum, and which the pack has to define.
/// Momentum only loads the soundscripts in its manifest, and a name defined twice keeps its first definition, so
/// an entry of the source game can be missing (CS:GO, or HL2/TF2 entries Momentum didn't copy) or hidden by a
/// Momentum entry of the same name (Momentum's <c>Weapon_Pistol.Single</c> hides HL2's and TF2's). Missing entries
/// are copied under their own name, hidden ones under <c>Porter_&lt;game&gt;.&lt;name&gt;</c>.
/// </summary>
public sealed class WeaponSoundResolver
{
    // Soundscript keys kept when copying an entry; operator stacks and the like are specific to the source engine branch
    private static readonly string[] CopiedKeys = ["channel", "volume", "pitch", "soundlevel", "CompatibilityAttenuation", "attenuation", "wave", "rndwave"];

    private readonly SoundscriptLibrary _momentum;
    private readonly SoundscriptLibrary _sourceGame;
    private readonly CatalogGame _game;
    private readonly Func<string, bool> _soundFileExists;
    private readonly Dictionary<string, KeyValuesNode> _added = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="momentum">What Momentum loads (<see cref="SoundscriptLibrary.LoadMomentum"/>)</param>
    /// <param name="sourceGame">The source game's own soundscripts</param>
    /// <param name="soundFileExists">Whether a game path (sound/...) exists where Momentum can load it</param>
    public WeaponSoundResolver(SoundscriptLibrary momentum, SoundscriptLibrary sourceGame, CatalogGame game, Func<string, bool> soundFileExists)
    {
        _momentum = momentum;
        _sourceGame = sourceGame;
        _game = game;
        _soundFileExists = soundFileExists;
    }

    /// <summary>The entries the pack has to define, in the order they were added.</summary>
    public IReadOnlyCollection<KeyValuesNode> AddedEntries => _added.Values;

    public string RenamedEntry(string name) => $"Porter_{_game.Id}.{name}";

    /// <summary>
    /// The entry to play for one of the source game's soundscript entries. An empty name means silence.
    /// </summary>
    public ResolvedSound Resolve(string name)
    {
        if (name.Length == 0)
            return new ResolvedSound("", false);

        if (_added.ContainsKey(name))
            return new ResolvedSound(name, true);

        SoundEntry? loaded = _momentum.Find(name);
        SoundEntry? original = _sourceGame.Find(name);

        if (loaded is not null && (original is null || IsGamesOwn(loaded) || SameWaves(loaded, original)))
            return new ResolvedSound(name, false);

        if (original is null)
            return new ResolvedSound(null, false, $"no soundscript entry {name} in {_game.Name}'s soundscripts or Momentum's");

        string newName = loaded is null ? name : RenamedEntry(name);
        if (_added.ContainsKey(newName))
            return new ResolvedSound(newName, true);

        KeyValuesNode copy = Copy(original.Node, newName);
        string? missing = SoundscriptLibrary.GetWaves(copy).Select(ContentPath.SoundFile).FirstOrDefault(x => !_soundFileExists(x));
        if (missing is not null)
            return new ResolvedSound(null, false, $"{name} plays {missing}, which isn't in {_game.Name}'s files");

        _added[newName] = copy;
        return new ResolvedSound(newName, true);
    }

    /// <summary>
    /// Copies the source game's entries starting with a prefix (e.g. <c>Weapon_AK47.</c>) that Momentum doesn't load,
    /// so the view model's animation events (reload, deploy sounds) play. Entries a Momentum entry hides can't be
    /// renamed, since the model names them; those are returned.
    /// </summary>
    public IReadOnlyList<string> AddByPrefix(string prefix)
    {
        var hidden = new List<string>();
        foreach (SoundEntry entry in _sourceGame.Entries.Where(x => x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            SoundEntry? loaded = _momentum.Find(entry.Name);
            if (loaded is not null)
            {
                // Entries the weapon plays itself (its shot) are renamed through Overrides already
                if (!IsGamesOwn(loaded) && !SameWaves(loaded, entry) && !_added.ContainsKey(RenamedEntry(entry.Name)))
                    hidden.Add(entry.Name);

                continue;
            }

            Resolve(entry.Name);
        }

        return hidden;
    }

    /// <summary>
    /// Defines an entry that plays one of the waves (game paths under sound/, already in the pack) at random.
    /// </summary>
    /// <param name="role">The weapon sound it's for (single_shot, explosion, ...), which picks the channel and level</param>
    public string AddWaves(string name, string role, IReadOnlyList<string> waves)
    {
        var entry = new KeyValuesNode(name);
        entry.Set("channel", role switch
        {
            "single_shot" or "charged_shot" => "CHAN_WEAPON",
            "explosion" or "fizzle" or "fizzle_alt" or "bounce" => "CHAN_STATIC",
            "trail" => "CHAN_STREAM",
            _ => "CHAN_ITEM",
        });
        entry.Set("volume", "1.0");
        // Explosions happen away from the player, so they need distance falloff. Momentum's own use SNDLVL_GUNFIRE
        // (140 dB), which Source plays at full volume for tens of thousands of units, so every explosion sounds like
        // it went off at the player. Quake-engine games fade their sounds out linearly, to silence at ~1300 units
        // (two thirds at 500, a quarter at 1000); 80 dB is the Source level closest to that (full volume to ~360
        // units, 72% at 500, 36% at 1000, 27% at 1300).
        entry.Set("soundlevel", role switch
        {
            "single_shot" or "charged_shot" => "SNDLVL_GUNFIRE",
            "explosion" => "SNDLVL_80dB",
            _ => "SNDLVL_NORM",
        });
        entry.Set("pitch", "PITCH_NORM");
        var relative = waves.Select(x => ")" + ContentPath.Normalize(x)["sound/".Length..]).ToList();
        if (relative.Count == 1)
        {
            entry.Set("wave", relative[0]);
        }
        else
        {
            KeyValuesNode random = entry.GetOrAddBlock("rndwave");
            foreach (string wave in relative)
                random.Children.Add(new KeyValuesNode("wave", wave));
        }

        _added[name] = entry;
        return name;
    }

    // An entry from Momentum's copy of the game's soundscript is the game's own (e.g. Weapon_AK47.Single from game_sounds_weapons_css.txt)
    private bool IsGamesOwn(SoundEntry loaded) => _game.MomentumSoundscripts.Contains(loaded.File, StringComparer.OrdinalIgnoreCase);

    private static bool SameWaves(SoundEntry a, SoundEntry b)
    {
        static IEnumerable<string> Files(SoundEntry entry) => entry.Waves.Select(ContentPath.SoundFile).Order(StringComparer.OrdinalIgnoreCase);
        return Files(a).SequenceEqual(Files(b), StringComparer.OrdinalIgnoreCase);
    }

    private static KeyValuesNode Copy(KeyValuesNode original, string name)
    {
        var copy = new KeyValuesNode(name);
        foreach (KeyValuesNode child in original.Children.Where(x => CopiedKeys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
            copy.Children.Add(child.Clone());

        foreach (KeyValuesNode wave in copy.Children.SelectMany(x => x.IsBlock ? x.Children : [x]).Where(x => x.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) && !x.IsBlock))
            wave.Value = NormalizeWave(wave.Value!);

        return copy;
    }

    // CS:GO's ` and ~ wave prefixes aren't understood by every branch; the others are Source 2013's
    private static string NormalizeWave(string wave) => wave.TrimStart('`', '~');
}
