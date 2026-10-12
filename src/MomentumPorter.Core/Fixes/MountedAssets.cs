namespace MomentumPorter.Fixes;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Porting;
using MomentumPorter.Settings;
using MomentumPorter.Steam;

/// <summary>
/// Lumper's "Remove Game Assets" job: files a map packs that Momentum mounts anyway from CS:S, CS:GO, TF2, Portal 2 or
/// HL2 are dead weight (and Valve's to boot), so the submission docs have them removed. A packed file goes when a
/// mounted game has the same path with the same bytes; one that differs is a deliberate replacement and stays, with a
/// warning. Only the games installed can be checked.
/// </summary>
public static class MountedAssets
{
    /// <summary>A game Momentum mounts, with its files open.</summary>
    public sealed record MountedGame(string Name, GameContent Content) : IDisposable
    {
        public void Dispose() => Content.Dispose();
    }

    // Packed folders that are the map's own even when a path matches (cubemaps, the map's scripts)
    private static readonly string[] MapOwnPrefixes = ["materials/maps/", "maps/", "resource/overviews/"];

    /// <summary>
    /// Opens the mounted games that are installed; the ones that aren't are logged.
    /// </summary>
    public static List<MountedGame> OpenMountedGames(SteamLocator steam, PorterSettings settings, IPortLog log)
    {
        var games = new List<MountedGame>();
        string? momentum = settings.ResolveMomentumContentDirectory(steam) is { } content
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(content))
            : null;

        foreach (SourceGameInfo game in new[] { SourceGames.CounterStrikeSource, SourceGames.CounterStrikeGlobalOffensive, SourceGames.TeamFortress2, SourceGames.HalfLife2 })
        {
            try
            {
                games.Add(new MountedGame(game.Name, SourceGames.Open(game, steam, momentum, settings.ResolveGameContentDirectory(game))));
            }
            catch (DirectoryNotFoundException)
            {
                log.Info($"{game.Name} isn't installed, so assets the map packs from it can't be removed");
            }
        }

        string? portal2 = steam.Find(KnownGame.Portal2)?.ContentDirectory;
        string portal2Vpk = portal2 is null ? "" : Path.Combine(portal2, "pak01_dir.vpk");
        if (File.Exists(portal2Vpk))
            games.Add(new MountedGame(KnownGame.Portal2.Name, new GameContent(KnownGame.Portal2.Name, [new VpkReader(portal2Vpk)])));
        else
            log.Info($"{KnownGame.Portal2.Name} isn't installed, so assets the map packs from it can't be removed");

        return games;
    }

    /// <summary>
    /// Removes the packed files the mounted games have identical copies of. Returns how many were removed.
    /// </summary>
    public static int Remove(PortContext context, IReadOnlyList<MountedGame> games, CancellationToken cancellation = default)
    {
        if (games.Count == 0)
            return 0;

        var removed = new Dictionary<string, List<string>>();
        var kept = new List<(string Path, string Game)>();
        foreach (string path in context.ListPakFiles())
        {
            cancellation.ThrowIfCancellationRequested();
            if (MapOwnPrefixes.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                continue;

            MountedGame? owner = games.FirstOrDefault(x => x.Content.Exists(path));
            if (owner is null)
                continue;

            byte[]? packed = context.ReadPakFileBytes(path);
            byte[]? mounted = owner.Content.Read(path);
            if (packed is null || mounted is null)
                continue;

            if (packed.AsSpan().SequenceEqual(mounted))
            {
                if (!removed.TryGetValue(owner.Name, out List<string>? paths))
                    removed[owner.Name] = paths = [];
                paths.Add(path);
            }
            else
            {
                kept.Add((path, owner.Name));
            }
        }

        int count = removed.Values.Sum(x => x.Count);
        if (count > 0)
        {
            var paths = new HashSet<string>(removed.Values.SelectMany(x => x), StringComparer.OrdinalIgnoreCase);
            context.EditPakFiles(zip =>
            {
                foreach (SharpCompress.Archives.Zip.ZipArchiveEntry entry in zip.Entries.Where(x => !x.IsDirectory && x.Key is not null && paths.Contains(x.Key.Replace('\\', '/'))).ToList())
                    zip.RemoveEntry(entry);
            });

            foreach ((string game, List<string> files) in removed)
                context.Report.Change($"packed files removed that Momentum mounts from {game}", files.Count);
        }

        foreach ((string path, string game) in kept)
            context.Report.Warn($"{path} differs from {game}'s copy and was kept; check that the map means to replace it");

        return count;
    }
}
