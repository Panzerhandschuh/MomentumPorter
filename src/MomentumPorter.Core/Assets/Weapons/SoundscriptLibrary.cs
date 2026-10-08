namespace MomentumPorter.Assets.Weapons;

/// <param name="File">The soundscript file it's defined in, e.g. <c>scripts/game_sounds_weapons_css.txt</c></param>
public sealed record SoundEntry(string Name, KeyValuesNode Node, string File)
{
    /// <summary>
    /// The waves the entry plays (<c>wave</c>, or each <c>wave</c> of <c>rndwave</c>), as written.
    /// </summary>
    public IReadOnlyList<string> Waves => SoundscriptLibrary.GetWaves(Node);
}

/// <summary>
/// Soundscript entries by name, the first definition winning as in the engine (a later file can't replace an entry).
/// </summary>
public sealed class SoundscriptLibrary
{
    private readonly Dictionary<string, SoundEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _files = [];

    public IReadOnlyList<string> Files => _files;

    public int Count => _entries.Count;

    public IEnumerable<SoundEntry> Entries => _entries.Values;

    public void AddFile(string fileName, string text)
    {
        KeyValuesNode document;
        try
        {
            document = KeyValuesNode.ParseDocument(text, fileName);
        }
        catch (FormatException)
        {
            // The engine skips files it can't parse too
            return;
        }

        _files.Add(fileName);
        foreach (KeyValuesNode node in document.Children.Where(x => x.IsBlock))
            _entries.TryAdd(node.Key, new SoundEntry(node.Key, node, fileName));
    }

    public SoundEntry? Find(string name) => _entries.GetValueOrDefault(name);

    public bool Contains(string name) => _entries.ContainsKey(name);

    /// <summary>
    /// Loads the soundscripts Momentum precaches, in its manifest's order, from the loose files of its game folder.
    /// </summary>
    public static SoundscriptLibrary LoadMomentum(MomentumInstall momentum)
    {
        var library = new SoundscriptLibrary();
        KeyValuesNode manifest = KeyValuesNode.Parse(momentum.ReadRequiredText("scripts/game_sounds_manifest.txt"), "game_sounds_manifest.txt");
        foreach (KeyValuesNode entry in manifest.Children.Where(x => !x.IsBlock))
        {
            if (!entry.Key.Equals("precache_file", StringComparison.OrdinalIgnoreCase) && !entry.Key.Equals("preload_file", StringComparison.OrdinalIgnoreCase))
                continue;

            if (momentum.ReadText(entry.Value!) is { } text)
                library.AddFile(Vpk.ContentPath.Normalize(entry.Value!), text);
        }

        return library;
    }

    public static IReadOnlyList<string> GetWaves(KeyValuesNode entry)
    {
        var waves = new List<string>();
        foreach (KeyValuesNode child in entry.Children)
        {
            if (child.Key.Equals("wave", StringComparison.OrdinalIgnoreCase) && !child.IsBlock)
                waves.Add(child.Value!);
            else if (child.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase) && child.IsBlock)
                waves.AddRange(child.FindAll("wave").Where(x => !x.IsBlock).Select(x => x.Value!));
        }

        return waves;
    }
}
