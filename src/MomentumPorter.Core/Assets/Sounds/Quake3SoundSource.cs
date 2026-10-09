namespace MomentumPorter.Assets.Sounds;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Common;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Steam;

/// <summary>
/// Quake 3's (or Quake Live's) player sounds, read from its pk3s: the characters under sound/player/, their footstep
/// types and the shared footstep and landing sounds.
/// </summary>
public sealed partial class Quake3SoundSource : PlayerSoundSource
{
    public const string PlayerSoundFolder = "sound/player";
    public const string FootstepFolder = "sound/player/footsteps";

    // Pack-relative folders. Shared Q3 sounds get their own folders so they never replace a mounted game's files.
    private const string SharedPackFolder = "player/q3";
    private const string FootstepPackFolder = "player/footsteps/q3";

    private static readonly HashSet<string> NotCharacters = new(StringComparer.OrdinalIgnoreCase) { "announce", "footsteps" };

    private Quake3SoundSource(Pk3Reader files, string path)
    {
        Files = files;
        Path = path;
        Characters = files.List(PlayerSoundFolder)
            .Select(x => x[(PlayerSoundFolder.Length + 1)..].Split('/'))
            .Where(x => x.Length == 2 && !NotCharacters.Contains(x[0]) && x[1].StartsWith("jump1.", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[0].ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public Pk3Reader Files { get; }

    public override SourceGameInfo Game => SourceGames.Quake3;

    /// <summary>The pk3 or folder the sounds come from.</summary>
    public override string Path { get; }

    /// <summary>Characters with a jump sound, lower case, sorted.</summary>
    public override IReadOnlyList<string> Characters { get; }

    public override string? DefaultCharacter => Characters.Contains("ranger") || Characters.Count == 0 ? "ranger" : Characters[0];

    /// <summary>
    /// Opens a pk3, or every pk3 in a game folder (baseq3). An install folder holding baseq3 works too.
    /// </summary>
    public static Quake3SoundSource Open(string path)
    {
        if (File.Exists(path))
            return new Quake3SoundSource(Pk3Reader.Open(path), path);

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{path} doesn't exist");

        string baseq3 = System.IO.Path.Combine(path, "baseq3");
        string folder = Directory.Exists(baseq3) && Directory.GetFiles(path, "*.pk3").Length == 0 ? baseq3 : path;
        if (Directory.GetFiles(folder, "*.pk3").Length == 0)
            throw new FileNotFoundException($"{folder} has no pk3 files");

        return new Quake3SoundSource(Pk3Reader.OpenDirectory(folder), folder);
    }

    /// <summary>
    /// Quake 3's baseq3 folder, or Quake Live's if Quake 3 isn't installed. Null if neither is.
    /// </summary>
    public static string? FindInstalled(SteamLocator? locator = null)
    {
        locator ??= new SteamLocator();
        foreach (KnownGame game in new[] { KnownGame.Quake3, KnownGame.QuakeLive })
        {
            string? content = locator.Find(game)?.ContentDirectory;
            if (content is not null && Directory.GetFiles(content, "*.pk3").Length > 0)
                return content;
        }

        return null;
    }

    /// <summary>
    /// Reads a sound. Quake 3 asks for .wav and also finds .ogg; Momentum can't play Ogg Vorbis, so only WAVs are
    /// returned, and the problem says when there's an .ogg version instead (Quake Live's footsteps).
    /// </summary>
    public override byte[]? ReadSound(string path, out string? problem)
    {
        problem = null;
        string wav = System.IO.Path.ChangeExtension(path, ".wav");
        byte[]? bytes = Files.TryReadAllBytes(wav);
        if (bytes is null && Files.Exists(System.IO.Path.ChangeExtension(path, ".ogg")))
            problem = $"{path} is only there as Ogg Vorbis, which Momentum can't play (Quake Live); use Quake 3's pk3s for it";

        return bytes;
    }

    /// <summary>The character's jump1.wav.</summary>
    public override GameSound GetJump(string? character)
    {
        character ??= DefaultCharacter;
        return new GameSound($"{character}'s jump1.wav", [new GameSoundFile($"{PlayerSoundFolder}/{character}/jump1.wav", $"player/{character}/jump1.wav")]);
    }

    /// <summary>land1.wav on soft landings, the character's fall1.wav on hard ones.</summary>
    public override (GameSound? Soft, GameSound? Hard) GetLand(string? character)
    {
        character ??= DefaultCharacter;
        return (
            new GameSound("land1.wav", [new GameSoundFile($"{PlayerSoundFolder}/land1.wav", $"{SharedPackFolder}/land1.wav")]),
            new GameSound($"{character}'s fall1.wav", [new GameSoundFile($"{PlayerSoundFolder}/{character}/fall1.wav", $"player/{character}/fall1.wav")]));
    }

    /// <summary>
    /// The footstep set for the surface (see <see cref="ClassifyFootstep"/>), then the character's. Each set is four
    /// files (step1..step4); the engine picks one at random each step, as Quake 3 does.
    /// </summary>
    public override IEnumerable<GameSound> GetFootsteps(string entryName, string? character, PlayerSoundPackOptions options)
    {
        string characterSet = CharacterFootstepSet(character, options);
        string set = ClassifyFootstep(entryName, characterSet, options.SurfaceFootsteps);
        yield return FootstepSet(set);
        if (set != characterSet)
            yield return FootstepSet(characterSet);
    }

    public override string DescribeFootsteps(string? character, PlayerSoundPackOptions options)
    {
        return $"Footsteps: {CharacterFootstepSet(character, options)}{(options.FootstepType == Quake3FootstepType.Character ? $" ({character}'s)" : "")}";
    }

    /// <summary>
    /// The Quake 3 footstep set a Momentum footstep sound gets: metal clanks and water splashes (Quake 3's surface
    /// footsteps), flesh is flesh, and everything else uses the character's set.
    /// </summary>
    public static string ClassifyFootstep(string soundName, string characterSet, bool surfaceFootsteps)
    {
        if (!surfaceFootsteps)
            return characterSet;

        string surface = soundName.Split('.')[0].ToLowerInvariant();
        bool woodLadder = soundName.Contains("wood", StringComparison.OrdinalIgnoreCase);
        if (surface.Contains("metal") || surface.Contains("grate") || surface.Contains("vent") || surface.Contains("chainlink")
            || surface is "grenade" or "weapon" || (surface == "ladder" && !woodLadder))
            return "clank";

        if (surface.Contains("water") || surface.Contains("wade") || surface.Contains("slosh"))
            return "splash";

        if (surface.Contains("flesh"))
            return "flesh";

        return characterSet;
    }

    public static string FootstepSetName(Quake3FootstepType type)
    {
        return type switch
        {
            Quake3FootstepType.Boot => "boot",
            Quake3FootstepType.Energy => "energy",
            Quake3FootstepType.Flesh => "flesh",
            Quake3FootstepType.Mech => "mech",
            _ => "step",
        };
    }

    private string CharacterFootstepSet(string? character, PlayerSoundPackOptions options)
    {
        return FootstepSetName(options.FootstepType == Quake3FootstepType.Character ? GetFootstepType(character ?? DefaultCharacter!) : options.FootstepType);
    }

    private static GameSound FootstepSet(string set)
    {
        return new GameSound(set, Enumerable.Range(1, 4).Select(i => new GameSoundFile($"{FootstepFolder}/{set}{i}.wav", $"{FootstepPackFolder}/{set}{i}.wav")).ToList());
    }

    public bool HasCharacter(string character) => Characters.Contains(character, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The character's footstep set from models/players/&lt;character&gt;/animation.cfg ("footsteps boot"), Normal when it doesn't say.
    /// </summary>
    public Quake3FootstepType GetFootstepType(string character)
    {
        string? config = Files.TryReadAllText($"models/players/{character}/animation.cfg");
        if (config is null)
            return Quake3FootstepType.Normal;

        Match match = FootstepsLine().Match(config);
        if (!match.Success)
            return Quake3FootstepType.Normal;

        return match.Groups[1].Value.ToLowerInvariant() switch
        {
            "boot" => Quake3FootstepType.Boot,
            "energy" => Quake3FootstepType.Energy,
            "flesh" => Quake3FootstepType.Flesh,
            "mech" => Quake3FootstepType.Mech,
            _ => Quake3FootstepType.Normal,
        };
    }

    public override void Dispose() => Files.Dispose();

    [GeneratedRegex(@"^\s*footsteps\s+(\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex FootstepsLine();
}
