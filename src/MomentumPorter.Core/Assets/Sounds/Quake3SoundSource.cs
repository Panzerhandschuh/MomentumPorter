namespace MomentumPorter.Assets.Sounds;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Common;
using MomentumPorter.Steam;

/// <summary>
/// Quake 3's (or Quake Live's) player sounds, read from its pk3s: the characters under sound/player/, their footstep
/// types and the shared footstep and landing sounds.
/// </summary>
public sealed partial class Quake3SoundSource : IDisposable
{
    public const string PlayerSoundFolder = "sound/player";
    public const string FootstepFolder = "sound/player/footsteps";

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

    /// <summary>The pk3 or folder the sounds come from.</summary>
    public string Path { get; }

    /// <summary>Characters with a jump sound, lower case, sorted.</summary>
    public IReadOnlyList<string> Characters { get; }

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
    /// returned. <paramref name="foundOgg"/> tells whether there's an .ogg version instead (Quake Live's footsteps).
    /// </summary>
    public byte[]? ReadSound(string path, out bool foundOgg)
    {
        foundOgg = false;
        string wav = System.IO.Path.ChangeExtension(path, ".wav");
        byte[]? bytes = Files.TryReadAllBytes(wav);
        if (bytes is null)
            foundOgg = Files.Exists(System.IO.Path.ChangeExtension(path, ".ogg"));

        return bytes;
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

    public void Dispose() => Files.Dispose();

    [GeneratedRegex(@"^\s*footsteps\s+(\w+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex FootstepsLine();
}
