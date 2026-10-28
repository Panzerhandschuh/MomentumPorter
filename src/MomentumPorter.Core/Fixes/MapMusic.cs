namespace MomentumPorter.Fixes;

using System.Text.RegularExpressions;
using LibBSP;
using MomentumPorter.Assets.Common;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;

public enum MusicGuess
{
    /// <summary>Long enough that it's music (or a long ambient track, which the slider may as well control).</summary>
    Music,

    /// <summary>Could be either; someone has to listen.</summary>
    Maybe,

    /// <summary>Short: a sound effect, most likely.</summary>
    Effect,
}

/// <summary>
/// A sound file packed in a map.
/// </summary>
/// <param name="Path">Its path in the pak file, e.g. "sound/badges/rush_hour.mp3"</param>
/// <param name="Length">How long it plays, or null when its headers couldn't be read</param>
/// <param name="Uses">What in the map plays it ("soundscape badges.4", "ambient_generic "music", plays everywhere")</param>
/// <param name="Reason">Why it got its guess, for the review</param>
public sealed record PackedSound(string Path, long Size, TimeSpan? Length, IReadOnlyList<string> Uses, MusicGuess Guess, string Reason)
{
    /// <summary>The name the map plays it by, relative to sound/ ("badges/rush_hour.mp3").</summary>
    public string Name => Path[MapMusic.SoundFolder.Length..];

    /// <summary>Its folder under sound/, or "" for one directly in it.</summary>
    public string Folder => Name.LastIndexOf('/') is var slash and >= 0 ? Name[..slash] : "";

    public bool IsReferenced => Uses.Count > 0;

    public string LengthText => MapMusic.FormatLength(Length);
}

/// <summary>
/// The sounds a map packs outside sound/music/, and how many it already has in there.
/// </summary>
public sealed record MapSounds(IReadOnlyList<PackedSound> Sounds, int InMusicFolder)
{
    public IEnumerable<PackedSound> Music => Sounds.Where(x => x.Guess == MusicGuess.Music);

    public IEnumerable<PackedSound> Maybe => Sounds.Where(x => x.Guess == MusicGuess.Maybe);

    public PackedSound? Find(string path) => Sounds.FirstOrDefault(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What <see cref="MapMusic.Apply"/> changed.</summary>
/// <param name="Moved">Pak paths before and after</param>
/// <param name="Skipped">Sounds left where they were, and why</param>
/// <param name="References">Entity keys, outputs and packed text files whose paths were rewritten</param>
public sealed record MusicChanges(IReadOnlyList<(string From, string To)> Moved, IReadOnlyList<string> Removed, IReadOnlyList<string> Skipped, int References);

/// <summary>
/// Momentum's music volume slider (snd_volume_music) only controls sounds whose path has "music/" in it: the "Music"
/// rule in scripts/soundmixers.txt, which the engine matches anywhere in the name. The submission docs have porters
/// move a map's music into sound/music/ in Lumper; this finds the music and does the move, rewriting every path the
/// map plays it by (entities, outputs, soundscapes, soundscripts, VScript).
/// </summary>
/// <remarks>
/// Only the length tells music apart from the files: short music loops and sound effects are packed side by side and
/// played the same way. So only sounds of <see cref="MusicLength"/> or longer move on their own; the rest are a
/// guess for someone to listen to (the app's Sounds review), whose picks come in as a <see cref="MapMusicChoice"/>.
/// </remarks>
public static class MapMusic
{
    public const string SoundFolder = "sound/";
    public const string MusicFolder = "sound/music/";

    /// <summary>Sounds this long are music. Across the CS:S and Momentum maps checked, nearly all were.</summary>
    public static readonly TimeSpan MusicLength = TimeSpan.FromMinutes(2);

    /// <summary>Between this and <see cref="MusicLength"/> they're music loops and ambience alike.</summary>
    public static readonly TimeSpan MaybeLength = TimeSpan.FromSeconds(30);

    /// <summary>Shorter sounds count as maybes only next to music (surf_christmas's 7-23 second carol loops).</summary>
    public static readonly TimeSpan FolderMaybeLength = TimeSpan.FromSeconds(5);

    private static readonly string[] SoundExtensions = [".wav", ".mp3", ".ogg"];

    // Packed text files that can name sounds: soundscapes, soundscripts, configs, VScript
    private static readonly string[] TextExtensions = [".txt", ".cfg", ".nut"];

    private const long MaxTextFileSize = 4 * 1024 * 1024;

    /// <summary>Whether the engine puts a sound on the music slider, by its pak path.</summary>
    public static bool IsInMusicFolder(string path)
    {
        string name = path.Replace('\\', '/');
        if (name.StartsWith(SoundFolder, StringComparison.OrdinalIgnoreCase))
            name = name[SoundFolder.Length..];
        return name.Contains("music/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where a sound goes: sound/badges/x.mp3 to sound/music/badges/x.mp3, as the docs' Lumper steps do.</summary>
    public static string MusicPath(string path)
    {
        string name = path.Replace('\\', '/')[SoundFolder.Length..];
        return MusicFolder + name;
    }

    public static string FormatLength(TimeSpan? length)
    {
        if (length is not { } value)
            return "?";

        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.TotalSeconds >= 10 ? value.ToString(@"m\:ss") : $"{value.TotalSeconds:0.0}s";
    }

    /// <summary>
    /// Lists the map's packed sounds outside sound/music/ with what plays them and a guess at which are music.
    /// </summary>
    public static MapSounds Scan(PortContext context)
    {
        if (context.Bsp.PakFile is not { Data.Length: > 0 } pak)
            return new MapSounds([], 0);

        var found = new List<(string Path, long Size, TimeSpan? Length)>();
        var texts = new List<(string Path, string Text)>();
        int inMusicFolder = 0;
        using (var zip = (ZipArchive)ZipArchive.OpenArchive(new MemoryStream(pak.Data)))
        {
            foreach (ZipArchiveEntry entry in zip.Entries.Where(x => !x.IsDirectory && x.Key is not null))
            {
                string path = entry.Key!.Replace('\\', '/');
                if (IsSound(path))
                {
                    if (IsInMusicFolder(path))
                    {
                        inMusicFolder++;
                        continue;
                    }

                    using Stream stream = entry.OpenEntryStream();
                    found.Add((path, entry.Size, SoundLength.Read(stream, entry.Size)));
                }
                else if (IsText(path) && entry.Size < MaxTextFileSize)
                {
                    texts.Add((path, Entity.TextEncoding.GetString(PortContext.ReadEntry(entry))));
                }
            }
        }

        List<Entity> entities = context.Entities;
        var uses = found.ToDictionary(x => x.Path, x => FindUses(x.Path, entities, texts), StringComparer.OrdinalIgnoreCase);

        // Folders with a sound long enough to be music
        HashSet<string> musicFolders = found
            .Where(x => x.Length >= MaybeLength)
            .Select(x => FolderOf(x.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sounds = found
            .Select(x =>
            {
                IReadOnlyList<string> used = uses[x.Path];
                (MusicGuess guess, string reason) = Guess(x.Length, used.Count > 0, musicFolders.Contains(FolderOf(x.Path)));
                return new PackedSound(x.Path, x.Size, x.Length, used, guess, reason);
            })
            .OrderBy(x => x.Guess)
            .ThenByDescending(x => x.Length ?? TimeSpan.MaxValue)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MapSounds(sounds, inMusicFolder);
    }

    private static (MusicGuess Guess, string Reason) Guess(TimeSpan? length, bool referenced, bool besideMusic)
    {
        if (length is not { } value)
            return (MusicGuess.Maybe, "couldn't read how long it is");

        string text = FormatLength(value);
        if (value >= MusicLength)
        {
            // A long sound nothing plays may replace one of the game's own (moving it would undo that), or be unused
            return referenced
                ? (MusicGuess.Music, $"{text} long")
                : (MusicGuess.Maybe, $"{text} long, but nothing in the map plays it (it may replace a game sound)");
        }

        if (value >= MaybeLength)
            return (MusicGuess.Maybe, $"{text} long, could be music or ambience");

        if (value >= FolderMaybeLength && besideMusic)
            return (MusicGuess.Maybe, $"{text}, in a folder with longer sounds");

        return (MusicGuess.Effect, $"{text}, likely a sound effect");
    }

    /// <summary>
    /// What the map moves and removes: what's ticked in its Sounds review, which (for sounds the review hasn't saved a
    /// pick for) is the ones long enough to be music.
    /// </summary>
    public static (List<string> Move, List<string> Remove) Decide(MapSounds sounds, MapMusicChoice? choice)
    {
        var move = new List<string>();
        var remove = new List<string>();
        foreach (PackedSound sound in sounds.Sounds)
        {
            if (choice?.IsRemoved(sound.Path) == true)
                remove.Add(sound.Path);
            else if (IsTicked(sound, choice))
                move.Add(sound.Path);
        }

        return (move, remove);
    }

    /// <summary>
    /// Whether the Sounds review shows a sound ticked as music: as picked, or (with no pick) when it's long enough.
    /// </summary>
    public static bool IsTicked(PackedSound sound, MapMusicChoice? choice)
    {
        if (choice?.IsRemoved(sound.Path) == true || choice?.IsNotMusic(sound.Path) == true)
            return false;

        return choice?.IsMusic(sound.Path) == true || sound.Guess == MusicGuess.Music;
    }

    /// <summary>
    /// Moves sounds into sound/music/ and removes others (e.g. copyrighted music), rewriting the paths the map plays
    /// the moved ones by. A sound whose new path is already packed stays where it is.
    /// </summary>
    public static MusicChanges Apply(PortContext context, IReadOnlyCollection<string> move, IReadOnlyCollection<string> remove)
    {
        var moved = new List<(string From, string To)>();
        var removed = new List<string>();
        var skipped = new List<string>();
        int references = 0;
        if (move.Count == 0 && remove.Count == 0)
            return new MusicChanges(moved, removed, skipped, references);

        context.EditPakFiles(zip =>
        {
            foreach (string path in remove)
            {
                if (PortContext.FindEntry(zip, path) is { } entry)
                {
                    removed.Add(entry.Key!.Replace('\\', '/'));
                    zip.RemoveEntry(entry);
                }
            }

            foreach (string path in move)
            {
                if (PortContext.FindEntry(zip, path) is not { } entry)
                    continue;

                string from = entry.Key!.Replace('\\', '/');
                string to = MusicPath(from);
                if (PortContext.FindEntry(zip, to) is not null)
                {
                    skipped.Add($"{from}: {to} is already packed");
                    continue;
                }

                byte[] bytes = PortContext.ReadEntry(entry);
                zip.RemoveEntry(entry);
                zip.AddEntry(to, new MemoryStream(bytes), closeStream: true);
                moved.Add((from, to));
            }

            if (moved.Count == 0)
                return;

            List<Regex> patterns = moved.Select(x => ReferencePattern(x.From[SoundFolder.Length..])).ToList();
            foreach (ZipArchiveEntry entry in zip.Entries.Where(x => !x.IsDirectory && x.Key is not null && IsText(x.Key.Replace('\\', '/'))).ToList())
            {
                if (entry.Size >= MaxTextFileSize)
                    continue;

                string text = Entity.TextEncoding.GetString(PortContext.ReadEntry(entry));
                string rewritten = Rewrite(text, patterns);
                if (rewritten == text)
                    continue;

                string key = entry.Key!;
                zip.RemoveEntry(entry);
                zip.AddEntry(key, new MemoryStream(Entity.TextEncoding.GetBytes(rewritten)), closeStream: true);
                references++;
            }

            foreach (Entity entity in context.Entities)
            {
                foreach (string key in entity.Keys.ToList())
                {
                    string value = entity[key];
                    string rewritten = Rewrite(value, patterns);
                    if (rewritten != value)
                    {
                        entity[key] = rewritten;
                        references++;
                    }
                }

                for (int i = 0; i < entity.connections.Count; i++)
                {
                    Entity.EntityConnection connection = entity.connections[i];
                    string param = connection.param ?? "";
                    string rewritten = Rewrite(param, patterns);
                    if (rewritten != param)
                    {
                        entity.connections[i] = connection with { param = rewritten };
                        references++;
                    }
                }
            }
        });

        return new MusicChanges(moved, removed, skipped, references);
    }

    /// <summary>
    /// What in the map plays a sound: entity keys (ambient_generic's message...), outputs ("play badges/x.mp3"),
    /// soundscapes and other packed text files.
    /// </summary>
    private static List<string> FindUses(string path, List<Entity> entities, List<(string Path, string Text)> texts)
    {
        string name = path[SoundFolder.Length..];
        string file = name[(name.LastIndexOf('/') + 1)..];
        Regex pattern = ReferencePattern(name);
        var uses = new List<string>();

        foreach (Entity entity in entities)
        {
            string label = Label(entity);
            foreach (string key in entity.Keys)
            {
                string value = entity[key];
                if (!value.Contains(file, StringComparison.OrdinalIgnoreCase) || !pattern.IsMatch(value))
                    continue;

                if (entity.ClassName.Equals("ambient_generic", StringComparison.OrdinalIgnoreCase) && key.Equals("message", StringComparison.OrdinalIgnoreCase))
                {
                    int flags = int.TryParse(entity["spawnflags"], out int parsed) ? parsed : 0;
                    // 1 Play everywhere, 32 Is NOT looped
                    uses.Add($"{label}, {((flags & 1) != 0 ? "plays everywhere" : "positional")}{((flags & 32) != 0 ? "" : ", looping")}");
                }
                else
                {
                    uses.Add($"{label} {key}");
                }
            }

            foreach (Entity.EntityConnection connection in entity.connections)
            {
                if (connection.param?.Contains(file, StringComparison.OrdinalIgnoreCase) == true && pattern.IsMatch(connection.param))
                    uses.Add($"{label} {connection.name}");
            }
        }

        foreach ((string textPath, string text) in texts)
        {
            if (!text.Contains(file, StringComparison.OrdinalIgnoreCase) || !pattern.IsMatch(text))
                continue;

            string textFile = textPath[(textPath.LastIndexOf('/') + 1)..];
            if (textFile.StartsWith("soundscapes", StringComparison.OrdinalIgnoreCase) && SoundscapeNames(textPath, text, pattern) is { Count: > 0 } soundscapes)
                uses.AddRange(soundscapes.Select(x => $"soundscape {x}"));
            else if (textFile.EndsWith(".nut", StringComparison.OrdinalIgnoreCase))
                uses.Add($"VScript {textPath}");
            else if (textFile.Contains("sounds", StringComparison.OrdinalIgnoreCase))
                uses.Add($"soundscript {textPath}");
            else
                uses.Add(textPath);
        }

        return uses.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> SoundscapeNames(string path, string text, Regex pattern)
    {
        try
        {
            return SoundscapeFile.Parse(path, text).Soundscapes.Where(x => pattern.IsMatch(x.Text)).Select(x => x.Name).ToList();
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static string Label(Entity entity)
    {
        string name = entity["targetname"];
        return string.IsNullOrEmpty(name) ? entity.ClassName : $"{entity.ClassName} \"{name}\"";
    }

    /// <summary>
    /// Matches a sound's name wherever the map can give it: on its own or after sound/, with either slash, after a
    /// sound character (#, *, ), ^...) or a command ("play "), in any case. Not inside a longer path or name.
    /// </summary>
    private static Regex ReferencePattern(string name)
    {
        string path = string.Join(@"[/\\]+", name.Split('/').Select(Regex.Escape));
        return new Regex($@"(?<![\w.\-/\\])((?:sound[/\\]+)?)({path})(?![\w.\-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Rewrite(string text, List<Regex> patterns)
    {
        foreach (Regex pattern in patterns)
            text = pattern.Replace(text, x => x.Groups[1].Value + "music/" + x.Groups[2].Value);
        return text;
    }

    private static bool IsSound(string path) =>
        path.StartsWith(SoundFolder, StringComparison.OrdinalIgnoreCase) && SoundExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    private static bool IsText(string path) =>
        !path.StartsWith(SoundFolder, StringComparison.OrdinalIgnoreCase) && TextExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    private static string FolderOf(string path) => path.LastIndexOf('/') is var slash and > 0 ? path[..slash] : "";
}
