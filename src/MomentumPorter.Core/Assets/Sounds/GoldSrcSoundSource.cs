namespace MomentumPorter.Assets.Sounds;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Steam;

/// <summary>
/// A GoldSrc game's player movement sounds (Half-Life, CS 1.6). GoldSrc has no soundscripts: the player code picks
/// sound/player/pl_&lt;type&gt;N.wav by the texture type under the player (concrete, metal, dirt, vent, grate, tile, CS 1.6's
/// snow; slosh and wade in water, ladder on ladders), one foot playing 1 and 3 and the other 2 and 4. Momentum's
/// surfaces are matched to those types by name. Jumps and landings play the footstep, so there are no sounds of
/// their own.
/// </summary>
public sealed partial class GoldSrcSoundSource : PlayerSoundSource
{
    public const string DefaultSet = "step";

    private readonly GameContent _content;
    private readonly Dictionary<string, List<(int Number, string File)>> _sets = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="content">The game's files; disposed with this</param>
    public GoldSrcSoundSource(SourceGameInfo game, GameContent content, string path)
    {
        if (game.Engine != ContentEngine.GoldSrc)
            throw new ArgumentException($"{game.Name} isn't a GoldSrc game", nameof(game));

        Game = game;
        Path = path;
        _content = content;
        foreach (string file in content.List("sound/player/"))
        {
            Match match = StepFile().Match(file);
            if (!match.Success)
                continue;

            string set = match.Groups[1].Value.ToLowerInvariant();
            if (!_sets.TryGetValue(set, out List<(int, string)>? files))
                _sets[set] = files = [];
            files.Add((int.Parse(match.Groups[2].Value), ContentPath.Normalize(file)));
        }
    }

    public override SourceGameInfo Game { get; }

    public override string Path { get; }

    /// <summary>The footstep sets found (step, metal, dirt...).</summary>
    public IEnumerable<string> Sets => _sets.Keys;

    /// <exception cref="DirectoryNotFoundException">The game isn't installed</exception>
    public static GoldSrcSoundSource Open(SourceGameInfo game, SteamLocator steam, string? contentDirectory = null)
    {
        string directory = contentDirectory ?? SourceGames.LocateContentDirectory(game, steam);
        GameContent content = SourceGames.Open(game, steam, contentDirectory: directory);
        try
        {
            return new GoldSrcSoundSource(game, content, directory);
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    public override GameSound? GetJump(string? character) => null;

    /// <summary>GoldSrc plays the footstep on landing, so landings get the concrete footsteps (both feet's).</summary>
    public override (GameSound? Soft, GameSound? Hard) GetLand(string? character)
    {
        GameSound? land = FootstepSet(DefaultSet, null);
        return (land, land);
    }

    public override IEnumerable<GameSound> GetFootsteps(string entryName, string? character, PlayerSoundPackOptions options)
    {
        bool left = !entryName.EndsWith("Right", StringComparison.OrdinalIgnoreCase);
        string set = ClassifyFootstep(entryName, options.SurfaceFootsteps);
        if (FootstepSet(set, left) is { } sound)
            yield return sound;
        if (set != DefaultSet && FootstepSet(DefaultSet, left) is { } fallback)
            yield return fallback;
    }

    public override string DescribeFootsteps(string? character, PlayerSoundPackOptions options)
    {
        return $"Footsteps: {Game.Name}'s pl_ sets ({string.Join(", ", _sets.Keys.Order(StringComparer.Ordinal))})";
    }

    public override byte[]? ReadSound(string path, out string? problem)
    {
        problem = null;
        return _content.Read(path);
    }

    public override void Dispose() => _content.Dispose();

    /// <summary>
    /// The GoldSrc footstep set (the pl_&lt;set&gt; files) for one of Momentum's footstep sounds, by its surface name.
    /// </summary>
    public static string ClassifyFootstep(string soundName, bool surfaceFootsteps)
    {
        if (!surfaceFootsteps)
            return DefaultSet;

        string surface = soundName.Split('.')[0].ToLowerInvariant();
        if (surface == "ladder")
            return "ladder";
        if (surface.Contains("wade"))
            return "wade";
        if (surface.Contains("water") || surface.Contains("slosh"))
            return "slosh";
        if (surface.Contains("vent") || surface.Contains("duct"))
            return "duct";
        if (surface.Contains("grate") || surface.Contains("chainlink"))
            return "grate";
        if (surface.Contains("metal") || surface is "grenade" or "weapon")
            return "metal";
        if (surface.Contains("snow"))
            return "snow";
        if (surface.Contains("tile"))
            return "tile";
        if (surface.Contains("dirt") || surface.Contains("mud") || surface.Contains("grass") || surface.Contains("gravel")
            || surface.Contains("sand") || surface.Contains("slime") || surface.Contains("foliage"))
            return "dirt";

        return DefaultSet;
    }

    /// <summary>
    /// One foot's files of a set (odd numbers for the left, even for the right), or both feet's when
    /// <paramref name="left"/> is null. Half-Life only plays 1 to 4 of a set (pl_tile5 is never played); CS 1.6's snow
    /// has six.
    /// </summary>
    private GameSound? FootstepSet(string set, bool? left)
    {
        if (!_sets.TryGetValue(set, out List<(int Number, string File)>? files))
            return null;

        int last = set == "snow" ? int.MaxValue : 4;
        List<GameSoundFile> picked = files
            .Where(x => x.Number <= last && (left is null || (x.Number % 2 == 1) == left))
            .OrderBy(x => x.Number)
            .Select(x => new GameSoundFile(x.File, $"porter/{Game.Id}/{x.File["sound/".Length..].ToLowerInvariant()}"))
            .ToList();
        return picked.Count == 0 ? null : new GameSound($"pl_{set}", picked, "PITCH_NORM");
    }

    [GeneratedRegex(@"^sound/player/pl_(step|metal|dirt|duct|grate|tile|slosh|wade|ladder|snow)(\d+)\.wav$", RegexOptions.IgnoreCase)]
    private static partial Regex StepFile();
}
