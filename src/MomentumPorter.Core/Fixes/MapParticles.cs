namespace MomentumPorter.Fixes;

using System.Text;
using System.Text.RegularExpressions;
using LibBSP;
using MomentumPorter.Assets.Particles;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Particle systems the map starts that Momentum never loads. Momentum reads particle files only from its own
/// <c>particles/particles_manifest.txt</c> and the map's <c>maps/&lt;map&gt;_particles.txt</c>, so a TF2 map's
/// <c>info_particle_system</c> using <c>buildingdamage_fire3</c> (TF2's buildingdamage.pcf, which Momentum's manifest
/// doesn't list) draws nothing. The files that define the missing systems are added to a map manifest packed in the
/// map, which the game reads from the pak file at map load.
/// </summary>
public static partial class MapParticles
{
    /// <summary>A group of Momentum's search paths, in the order the game searches them.</summary>
    public sealed record SearchPath(string Name, GameContent Content) : IDisposable
    {
        public void Dispose() => Content.Dispose();
    }

    public sealed record Result(IReadOnlyList<string> AddedFiles, IReadOnlyList<string> FixedSystems, IReadOnlyList<string> Unresolved);

    public const string PakSourceName = "the map";

    // Keys entities name a particle system with (info_particle_system, trigger_particle)
    private static readonly string[] NameKeys = ["effect_name", "particle_name"];

    public static string MapManifestPath(string mapName) => $"maps/{mapName}_particles.txt";

    /// <summary>
    /// Opens Momentum's search paths: its own folder and the VPKs its gameinfo.txt mounts from <c>mount/</c>, then the
    /// games it mounts (Team Fortress 2, Counter-Strike: Source, CS:GO, Portal 2), as far as they're installed.
    /// </summary>
    /// <param name="momentumContent">Momentum's content folder (<c>&lt;install&gt;/momentum</c>)</param>
    public static List<SearchPath> OpenSearchPaths(string momentumContent, SteamLocator steam, PorterSettings settings, IPortLog log)
    {
        string install = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(momentumContent)))!;
        var paths = new List<SearchPath>();
        var momentum = new List<IContentSource> { new FolderSource(momentumContent) };
        foreach (string vpk in MountedVpks(momentumContent, install))
            momentum.Add(new VpkReader(vpk));
        paths.Add(new SearchPath("Momentum", new GameContent("Momentum", momentum)));

        foreach (SourceGameInfo game in new[] { SourceGames.TeamFortress2, SourceGames.CounterStrikeSource, SourceGames.CounterStrikeGlobalOffensive })
        {
            try
            {
                paths.Add(new SearchPath(game.Name, SourceGames.Open(game, steam, contentDirectory: settings.ResolveGameContentDirectory(game))));
            }
            catch (DirectoryNotFoundException)
            {
                log.Info($"{game.Name} isn't installed, so its particle systems can't be checked");
            }
        }

        string? portal2 = steam.Find(KnownGame.Portal2)?.ContentDirectory is { } content ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(content)) : null;
        var portal2Vpks = portal2 is null
            ? []
            : new[] { "update", "portal2_dlc2", "portal2_dlc1", "portal2" }.Select(x => Path.Combine(portal2, x, "pak01_dir.vpk")).Where(File.Exists).ToList();
        if (portal2Vpks.Count > 0)
            paths.Add(new SearchPath(KnownGame.Portal2.Name, new GameContent(KnownGame.Portal2.Name, portal2Vpks.Select(x => (IContentSource)new VpkReader(x)))));
        else
            log.Info($"{KnownGame.Portal2.Name} isn't installed, so its particle systems can't be checked");

        return paths;
    }

    // The game lines of gameinfo.txt's SearchPaths that mount a VPK from the install's mount folder, in order
    private static IEnumerable<string> MountedVpks(string momentumContent, string install)
    {
        string gameInfo = Path.Combine(momentumContent, "gameinfo.txt");
        var vpks = new List<string>();
        if (File.Exists(gameInfo))
        {
            foreach (string line in File.ReadLines(gameInfo))
            {
                Match match = MountedVpkRegex().Match(StripComment(line));
                if (match.Success)
                    vpks.Add(Path.Combine(install, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        string hl2 = Path.Combine(install, "mount", "hl2_dir.vpk");
        if (vpks.Count == 0 && File.Exists(hl2))
            vpks.Add(hl2);

        return vpks.Where(File.Exists);
    }

    /// <summary>
    /// The particle files a manifest lists, without the <c>!</c> that precaches them.
    /// </summary>
    public static List<string> ManifestFiles(string manifest)
    {
        var files = new List<string>();
        foreach (string line in manifest.Split('\n'))
        {
            foreach (Match match in ManifestFileRegex().Matches(StripComment(line)))
                files.Add(match.Groups[1].Value.TrimStart('!').Replace('\\', '/'));
        }

        return files;
    }

    /// <summary>
    /// Adds the files that define the particle systems the map uses, and Momentum doesn't load, to the map's particle
    /// manifest. Returns what was added, and the systems no file was found for.
    /// </summary>
    /// <param name="searchPaths">Momentum's search paths (<see cref="OpenSearchPaths"/>), Momentum's own first</param>
    /// <param name="previousMapName">The map's name before porting, if it's being renamed, to keep its packed manifest</param>
    public static Result Apply(PortContext context, IReadOnlyList<SearchPath> searchPaths, string? previousMapName = null, CancellationToken cancellation = default)
    {
        var used = new List<string>();
        foreach (Entity entity in context.Entities)
        {
            foreach (string key in NameKeys)
            {
                if (entity[key] is { Length: > 0 } name && !used.Contains(name, StringComparer.OrdinalIgnoreCase))
                    used.Add(name);
            }
        }

        if (used.Count == 0)
            return new Result([], [], []);

        var resolver = new Resolver(context, searchPaths);
        string momentumManifest = searchPaths.Count > 0 && searchPaths[0].Content.Read(ParticleManifest.Path) is { } bytes
            ? Encoding.Latin1.GetString(bytes)
            : throw new PortStepException($"Momentum's {ParticleManifest.Path} wasn't found, so the particle systems it loads can't be told");

        // What the game loads before the map's own manifest: Momentum's manifest, read without the map's pak file
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in ManifestFiles(momentumManifest))
            loaded.UnionWith(resolver.Systems(file, withPak: false)?.Names ?? []);

        string mapManifestPath = MapManifestPath(context.MapName);
        string? mapManifest = context.ReadPakFile(mapManifestPath)
            ?? (previousMapName is not null && !previousMapName.Equals(context.MapName, StringComparison.OrdinalIgnoreCase) ? context.ReadPakFile(MapManifestPath(previousMapName)) : null);
        List<string> mapFiles = mapManifest is null ? [] : ManifestFiles(mapManifest);
        foreach (string file in mapFiles)
            loaded.UnionWith(resolver.Systems(file, withPak: true)?.Names ?? []);

        List<string> missing = used.Where(x => !loaded.Contains(x)).ToList();
        if (missing.Count == 0)
            return new Result([], [], []);

        var added = new List<string>();
        var fixedSystems = new List<string>();
        var unresolved = new List<string>();
        Dictionary<string, (string Source, string Path)> found = resolver.FindDefinitions(missing, cancellation);
        foreach (string system in missing)
        {
            if (!found.TryGetValue(system, out (string Source, string Path) definition))
            {
                unresolved.Add(system);
                continue;
            }

            // The game loads a listed path from the first search path that has it, which may not be the file found
            (string Source, IReadOnlyList<string> Names)? loads = resolver.Systems(definition.Path, withPak: true);
            if (loads is null || !loads.Value.Names.Contains(system, StringComparer.OrdinalIgnoreCase))
            {
                context.Report.Warn(
                    $"particle system {system} is in {definition.Source}'s {definition.Path}, but Momentum loads {definition.Path} from {loads?.Source ?? "nowhere"}, which doesn't have it");
                unresolved.Add(system);
                continue;
            }

            fixedSystems.Add(system);
            if (!mapFiles.Contains(definition.Path, StringComparer.OrdinalIgnoreCase) && !added.Contains(definition.Path, StringComparer.OrdinalIgnoreCase))
                added.Add(definition.Path);
        }

        foreach (string system in unresolved.Where(x => !found.ContainsKey(x)))
            context.Report.Warn($"particle system {system} isn't defined by Momentum, the map or the installed games it mounts, so it draws nothing");

        if (added.Count > 0)
        {
            context.WritePakFile(mapManifestPath, WriteManifest(mapFiles.Concat(added)));
            foreach (IGrouping<string, string> source in added.GroupBy(x => resolver.Systems(x, withPak: true)!.Value.Source))
                context.Report.Change($"particle files from {source.Key} listed in {mapManifestPath}", source.Count());
        }

        return new Result(added, fixedSystems, unresolved);
    }

    public static string WriteManifest(IEnumerable<string> files)
    {
        var text = new StringBuilder("particles_manifest\n{\n");
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            text.Append($"\t\"file\"\t\"{file}\"\n");

        return text.Append("}\n").ToString();
    }

    private static string StripComment(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    /// <summary>
    /// Finds particle files the way the game does: the map's pak file first (at map load), then Momentum's search paths.
    /// </summary>
    private sealed class Resolver(PortContext context, IReadOnlyList<SearchPath> searchPaths)
    {
        private readonly Dictionary<(string Source, string Path), IReadOnlyList<string>> _names = [];

        /// <summary>The systems in the file the game would load from this path, and where it's loaded from.</summary>
        public (string Source, IReadOnlyList<string> Names)? Systems(string path, bool withPak)
        {
            if (withPak && context.ReadPakFileBytes(path) is { } packed)
                return (PakSourceName, Parse(PakSourceName, path, packed));

            foreach (SearchPath searchPath in searchPaths)
            {
                if (searchPath.Content.Read(path) is { } data)
                    return (searchPath.Name, Parse(searchPath.Name, path, data));
            }

            return null;
        }

        /// <summary>
        /// Where each system is defined: the first source that has a file defining it, and within a source the file its
        /// own manifest lists over fallbacks such as <c>_dx80</c> copies.
        /// </summary>
        public Dictionary<string, (string Source, string Path)> FindDefinitions(IReadOnlyCollection<string> systems, CancellationToken cancellation)
        {
            var found = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<(string Name, IEnumerable<string> Files, Func<string, byte[]?> Read, string? Manifest)>
            {
                (PakSourceName, context.ListPakFiles(), context.ReadPakFileBytes, null),
            };
            foreach (SearchPath searchPath in searchPaths)
            {
                byte[]? manifest = searchPath.Content.Read(ParticleManifest.Path);
                sources.Add((searchPath.Name, searchPath.Content.List("particles/"), searchPath.Content.Read, manifest is null ? null : Encoding.Latin1.GetString(manifest)));
            }

            foreach ((string name, IEnumerable<string> files, Func<string, byte[]?> read, string? manifest) in sources)
            {
                var listed = new HashSet<string>(manifest is null ? [] : ManifestFiles(manifest), StringComparer.OrdinalIgnoreCase);
                // The game only reads particle files directly in particles/
                List<string> pcfs = files
                    .Select(x => x.Replace('\\', '/'))
                    .Where(x => x.StartsWith("particles/", StringComparison.OrdinalIgnoreCase) && x.IndexOf('/', "particles/".Length) < 0
                        && x.EndsWith(".pcf", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => listed.Contains(x) ? 0 : 1)
                    .ThenBy(x => FallbackRegex().IsMatch(x) ? 1 : 0)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (string pcf in pcfs)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (systems.All(found.ContainsKey))
                        return found;

                    if (read(pcf) is not { } data)
                        continue;

                    foreach (string system in Parse(name, pcf, data))
                    {
                        if (systems.Contains(system, StringComparer.OrdinalIgnoreCase))
                            found.TryAdd(system, (name, pcf));
                    }
                }
            }

            return found;
        }

        private IReadOnlyList<string> Parse(string source, string path, byte[] data)
        {
            if (_names.TryGetValue((source, path.ToLowerInvariant()), out IReadOnlyList<string>? names))
                return names;

            try
            {
                names = ParticleSystemNames.Read(data);
            }
            catch (InvalidDataException)
            {
                names = [];
            }

            _names[(source, path.ToLowerInvariant())] = names;
            return names;
        }
    }

    [GeneratedRegex("^\\s*game[^\\s]*\\s+\"?(mount/[^\"\\s]+\\.vpk)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex MountedVpkRegex();

    [GeneratedRegex("\"file\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ManifestFileRegex();

    [GeneratedRegex(@"_dx[89]0?\.pcf$", RegexOptions.IgnoreCase)]
    private static partial Regex FallbackRegex();
}
