namespace MomentumPorter.Assets.Sounds;

using System.Text;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Steam;

/// <summary>
/// A Source game's player movement sounds (CS:S, CS:GO, TF2, HL2), from its own soundscripts. Footsteps are found by
/// the name Momentum's entry has (the games share most of them, e.g. <c>SolidMetal.StepLeft</c>), then through the
/// surfaces that use the entry in Momentum and what the same surface uses in the game, then the game's default
/// footsteps. CS:GO has a set per team (<c>CT_Concrete.StepLeft</c>) and its own landing sounds
/// (<c>Land_Default.StepLeft</c>); the others have no jump or landing sounds, only footsteps.
/// </summary>
public sealed class SourceGameSoundSource : PlayerSoundSource
{
    private static readonly IReadOnlyList<string> CsgoTeams = ["ct", "t"];

    private readonly GameContent _content;
    private readonly SoundscriptLibrary _soundscripts;
    private readonly SurfaceProperties _surfaces;
    private readonly SurfaceProperties? _momentumSurfaces;

    /// <param name="content">The game's files; disposed with this</param>
    /// <param name="momentumSurfaces">Momentum's surface properties, to find which surfaces use a footstep entry</param>
    public SourceGameSoundSource(SourceGameInfo game, GameContent content, string path, SurfaceProperties? momentumSurfaces = null)
    {
        if (game.Engine != ContentEngine.Source)
            throw new ArgumentException($"{game.Name} isn't a Source game", nameof(game));

        Game = game;
        Path = path;
        _content = content;
        _momentumSurfaces = momentumSurfaces;
        _soundscripts = LoadSoundscripts(content);
        _surfaces = SurfaceProperties.Load(ReadText);
    }

    public override SourceGameInfo Game { get; }

    public override string Path { get; }

    private bool IsCsgo => Game.Id == SourceGames.CounterStrikeGlobalOffensive.Id;

    public override IReadOnlyList<string> Characters => TeamsOf(Game);

    public override string CharacterKind => "team";

    public override string? DefaultCharacter => IsCsgo ? "ct" : null;

    /// <summary>The teams a game has footsteps for (CS:GO's ct and t, CT first), without opening it.</summary>
    public static IReadOnlyList<string> TeamsOf(SourceGameInfo game) => game.Id == SourceGames.CounterStrikeGlobalOffensive.Id ? CsgoTeams : [];

    /// <summary>How many soundscript entries were read, to tell a game whose soundscripts weren't found.</summary>
    public int SoundscriptEntryCount => _soundscripts.Count;

    /// <summary>
    /// Opens the game's files for its player sounds: what <see cref="SourceGames.Open"/> opens, plus the loose content
    /// folder (HL2 keeps its soundscripts there) and the HL2 VPKs next to it that CS:S and TF2 mount for HL2's
    /// footstep WAVs (Momentum doesn't mount those, so the pack has to carry the files anyway).
    /// </summary>
    /// <param name="contentDirectory">The game's content folder (cstrike, csgo, tf, hl2), or null to find it through Steam</param>
    /// <exception cref="DirectoryNotFoundException">The game isn't installed</exception>
    public static SourceGameSoundSource Open(SourceGameInfo game, SteamLocator steam, string? contentDirectory = null, SurfaceProperties? momentumSurfaces = null)
    {
        string directory = contentDirectory ?? SourceGames.LocateContentDirectory(game, steam);
        GameContent opened = SourceGames.Open(game, steam, momentumDirectory: null, contentDirectory: directory);
        var sources = new List<IContentSource>(opened.Sources) { new FolderSource(directory) };
        try
        {
            string hl2 = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(directory))!, "hl2");
            if (game.Id != SourceGames.HalfLife2.Id)
            {
                foreach (string name in new[] { "hl2_sound_misc", "hl2_misc" })
                {
                    string vpk = System.IO.Path.Combine(hl2, name + "_dir.vpk");
                    if (File.Exists(vpk))
                        sources.Add(new VpkReader(vpk));
                }
            }

            return new SourceGameSoundSource(game, new GameContent(game.Name, sources), directory, momentumSurfaces);
        }
        catch
        {
            foreach (IContentSource source in sources)
                source.Dispose();
            throw;
        }
    }

    public override GameSound? GetJump(string? character) => null;

    /// <summary>
    /// CS:GO's <c>Land_Default.StepLeft</c> (its landing on concrete). The other games play the footstep on landing,
    /// so they get their default footsteps.
    /// </summary>
    public override (GameSound? Soft, GameSound? Hard) GetLand(string? character)
    {
        GameSound? land = IsCsgo ? FromEntry("Land_Default.StepLeft") : null;
        land ??= DefaultFootsteps(character);
        return (land, land);
    }

    public override IEnumerable<GameSound> GetFootsteps(string entryName, string? character, PlayerSoundPackOptions options)
    {
        bool left = !entryName.EndsWith("Right", StringComparison.OrdinalIgnoreCase);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Names()
        {
            if (options.SurfaceFootsteps)
            {
                yield return entryName;

                // The surfaces Momentum plays this for, and what the game plays on them
                foreach (string surface in _momentumSurfaces?.SurfacesUsing(entryName, left) ?? [])
                {
                    if (_surfaces.GetStep(surface, left) is { } step)
                        yield return step;
                }
            }

            yield return left ? "Default.StepLeft" : "Default.StepRight";
        }

        foreach (string name in Names())
        {
            foreach (string candidate in WithTeam(name, character))
            {
                if (tried.Add(candidate) && FromEntry(candidate) is { } sound)
                    yield return sound;
            }
        }
    }

    public override string DescribeFootsteps(string? character, PlayerSoundPackOptions options)
    {
        return $"Footsteps: {Game.Name}'s{(IsCsgo ? $" ({character?.ToUpperInvariant()})" : "")}, from {_soundscripts.Count} soundscript entries";
    }

    public override byte[]? ReadSound(string path, out string? problem)
    {
        problem = null;
        return _content.Read(path);
    }

    public override void Dispose() => _content.Dispose();

    /// <summary>
    /// A soundscript entry's waves, put in the pack under sound/porter/&lt;game&gt;/ so another mounted game's file of
    /// the same name (HL2's and CS:S's footsteps share paths) can't stand in for them.
    /// </summary>
    private GameSound? FromEntry(string name)
    {
        if (_soundscripts.Find(name) is not { } entry || entry.Waves.Count == 0)
            return null;

        List<GameSoundFile> files = entry.Waves
            .Select(ContentPath.SoundFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(file => new GameSoundFile(file, $"porter/{Game.Id}/{file["sound/".Length..].ToLowerInvariant()}"))
            .ToList();
        return new GameSound(entry.Name, files, entry.Node.GetString("pitch"), entry.Node.GetString("soundlevel"));
    }

    /// <summary>Both feet's default footsteps, for landings in games without landing sounds.</summary>
    private GameSound? DefaultFootsteps(string? character)
    {
        GameSound? left = WithTeam("Default.StepLeft", character).Select(FromEntry).FirstOrDefault(x => x is not null);
        GameSound? right = WithTeam("Default.StepRight", character).Select(FromEntry).FirstOrDefault(x => x is not null);
        if (left is null || right is null)
            return left ?? right;

        return left with { Label = "default footsteps", Files = [.. left.Files, .. right.Files.Where(x => !left.Files.Contains(x))] };
    }

    // CS:GO's footsteps are per team (CT_Concrete.StepLeft, T_...); its unprefixed names are tried after
    private IEnumerable<string> WithTeam(string name, string? character)
    {
        if (IsCsgo)
            yield return $"{(character?.Equals("t", StringComparison.OrdinalIgnoreCase) == true ? "T" : "CT")}_{name}";

        yield return name;
    }

    private string? ReadText(string path) => _content.Read(path) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    /// <summary>
    /// The soundscripts the game's manifest lists, in its order, then any other scripts/game_sounds*.txt (TF2's
    /// footsteps file isn't in its manifest). The first definition of a name wins, as in the engine.
    /// </summary>
    private static SoundscriptLibrary LoadSoundscripts(GameContent content)
    {
        var library = new SoundscriptLibrary();
        var files = new List<string>();
        if (content.Read("scripts/game_sounds_manifest.txt") is { } manifestBytes)
        {
            try
            {
                KeyValuesNode manifest = KeyValuesNode.Parse(Encoding.UTF8.GetString(manifestBytes), "game_sounds_manifest.txt");
                files.AddRange(manifest.Children
                    .Where(x => !x.IsBlock && (x.Key.Equals("precache_file", StringComparison.OrdinalIgnoreCase) || x.Key.Equals("preload_file", StringComparison.OrdinalIgnoreCase)))
                    .Select(x => ContentPath.Normalize(x.Value!)));
            }
            catch (FormatException)
            {
                // Fall back to every soundscript below
            }
        }

        files.AddRange(content.List("scripts/")
            .Where(x => x.Count(c => c == '/') == 1
                && System.IO.Path.GetFileName(x).StartsWith("game_sounds", StringComparison.OrdinalIgnoreCase)
                && x.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                && !x.EndsWith("game_sounds_manifest.txt", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase));

        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (content.Read(file) is { } bytes)
                library.AddFile(file, Encoding.UTF8.GetString(bytes));
        }

        return library;
    }
}
