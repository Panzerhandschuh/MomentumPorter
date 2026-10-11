namespace MomentumPorter.Tools;

using System.Diagnostics;
using MomentumPorter.Assets.Vpk;
using MomentumPorter.Pipeline;
using MomentumPorter.Settings;
using MomentumPorter.Steam;
using ValvePak;

/// <summary>
/// What bspPack packs a map's assets from: a stub game folder whose gameinfo.txt searches only the source game's own
/// content, and loose files of that game to leave out.
/// </summary>
/// <param name="Name">The game, for the log</param>
/// <param name="GameFolder">The stub game folder (the one with gameinfo.txt) for bspPack's config.ini</param>
/// <param name="ExcludeFiles">Full paths for ResourceConfig.ini's [ExcludeFiles]</param>
public sealed record BspPackGame(string Name, string GameFolder, IReadOnlyList<string> ExcludeFiles);

/// <summary>
/// Sets up the games the bundled bspPack packs from, so it needs no setting up by hand. Everything lives in a folder
/// the porter owns:
/// <list type="bullet">
/// <item><c>bin/win64</c>: a junction to Momentum's <c>bin/win64</c>, since bspPack runs bspzip from
/// <c>&lt;game folder&gt;/../bin/win64</c> and the source games don't have a 64-bit one there.</item>
/// <item><c>&lt;profile&gt;/gameinfo.txt</c>: searches only the game's own content, so assets Momentum already has (its
/// own, HL2's, the games it mounts) aren't packed.</item>
/// <item><c>&lt;profile&gt;/vpk</c>: the game's VPKs extracted, since bspPack only packs loose files. Extracted once, and
/// again when the VPKs change.</item>
/// </list>
/// </summary>
public sealed class BspPackGames(string rootDirectory)
{
    // Where bspPack looks for bspzip, next to the game folder
    private static string BinFolder => OperatingSystem.IsWindows() ? "win64" : "linux64";

    // Folders of loose game files that maps use assets from
    private static readonly string[] AssetFolders = ["materials", "models", "sound", "particles", "scripts", "resource"];

    /// <summary>%LocalAppData%/MomentumPorter/bspPack</summary>
    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MomentumPorter", "bspPack");

    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    /// <summary>
    /// Sets up the game a profile's maps pack assets from.
    /// </summary>
    /// <param name="profile">The game profile's name (see <see cref="Porting.GameProfiles"/>)</param>
    /// <param name="gameDirectory">The game folder the map came from, if any, for when the game isn't found otherwise</param>
    /// <param name="momentumInstall">Momentum's install folder, for its bspzip</param>
    /// <param name="settings">Where the games are, when they aren't found in the Steam libraries</param>
    public BspPackGame Prepare(string profile, string? gameDirectory, string momentumInstall, PorterSettings settings, SteamLocator steam, IPortLog log, CancellationToken cancellation = default)
    {
        LinkBspZip(momentumInstall);
        return profile.ToLowerInvariant() switch
        {
            "kzmod" => PrepareKreedzClimbing(gameDirectory, settings, steam, log, cancellation),
            // Momentum mounts the other games (CS:S, CS:GO), so their maps aren't packed at all
            _ => throw new PortStepException($"{profile} maps don't pack assets; only Kreedz Climbing maps do"),
        };
    }

    private BspPackGame PrepareKreedzClimbing(string? gameDirectory, PorterSettings settings, SteamLocator steam, IPortLog log, CancellationToken cancellation)
    {
        bool overridden = !string.IsNullOrWhiteSpace(settings.KreedzClimbingDirectory);
        string content = settings.ResolveKreedzClimbingContentDirectory(steam)
            ?? (!overridden && HasFile(gameDirectory, "kz_misc_dir.vpk") ? gameDirectory : null)
            ?? throw new PortStepException(overridden
                ? $"{settings.KreedzClimbingDirectory} doesn't look like a Kreedz Climbing install (no kz/gameinfo.txt). Fix its folder in Settings."
                : "Kreedz Climbing wasn't found in your Steam libraries; bspPack packs KZ maps' assets from it. Set its folder in Settings.");
        string install = Path.GetDirectoryName(Path.GetFullPath(content))!;

        // KZ searches its own VPKs, then HL2's, then its loose files (kz/gameinfo.txt). Momentum mounts HL2 itself.
        string[] vpks = ["kz_textures", "kz_sounds", "kz_misc"];
        string[] hl2Vpks = ["hl2_english", "hl2_textures", "hl2_sound_vo_english", "hl2_sound_misc", "hl2_misc"];
        return PrepareGame(
            "kzmod",
            "Kreedz Climbing",
            content,
            vpks.Select(x => Path.Combine(content, x + "_dir.vpk")).Where(File.Exists).ToArray(),
            hl2Vpks.Select(x => Path.Combine(install, "hl2", x + "_dir.vpk")).Where(File.Exists).ToArray(),
            log,
            cancellation);
    }

    /// <param name="vpks">The game's VPKs to extract, searched before its loose files</param>
    /// <param name="shadowingVpks">VPKs the game searches before its loose files that Momentum mounts too. Loose files
    /// with the same path are never loaded by the game, so they're excluded rather than packed.</param>
    private BspPackGame PrepareGame(string id, string name, string content, string[] vpks, string[] shadowingVpks, IPortLog log, CancellationToken cancellation)
    {
        string folder = Path.Combine(RootDirectory, id);
        Directory.CreateDirectory(folder);

        var searchPaths = new List<string>();
        if (vpks.Length > 0)
        {
            string extracted = Path.Combine(folder, "vpk");
            ExtractVpks(vpks, extracted, name, log, cancellation);
            searchPaths.Add(extracted);
        }

        searchPaths.Add(Path.GetFullPath(content));
        WriteGameInfo(folder, name, searchPaths);
        return new BspPackGame(name, folder, FindShadowedLooseFiles(Path.GetFullPath(content), shadowingVpks));
    }

    private static void WriteGameInfo(string folder, string name, IEnumerable<string> searchPaths)
    {
        var lines = new List<string>
        {
            "\"GameInfo\"",
            "{",
            $"\tgame\t\"{name} (bspPack)\"",
            "",
            "\t// Written by MomentumPorter: bspPack packs assets from the game's own content only",
            "\tFileSystem",
            "\t{",
            "\t\tSearchPaths",
            "\t\t{",
        };
        lines.AddRange(searchPaths.Select(x => $"\t\t\tgame+mod\t\"{x}\""));
        lines.AddRange(["\t\t}", "\t}", "}"]);
        File.WriteAllLines(Path.Combine(folder, "gameinfo.txt"), lines);
    }

    /// <summary>
    /// The game's loose asset files that a VPK searched before them has too.
    /// </summary>
    public static List<string> FindShadowedLooseFiles(string content, IEnumerable<string> shadowingVpks)
    {
        var shadowing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string vpk in shadowingVpks)
        {
            using var reader = new VpkReader(vpk);
            shadowing.UnionWith(reader.List());
        }

        var shadowed = new List<string>();
        if (shadowing.Count == 0)
            return shadowed;

        foreach (string top in AssetFolders)
        {
            string dir = Path.Combine(content, top);
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (shadowing.Contains(Path.GetRelativePath(content, file).Replace('\\', '/')))
                    shadowed.Add(file);
            }
        }

        shadowed.Sort(StringComparer.OrdinalIgnoreCase);
        return shadowed;
    }

    /// <summary>
    /// Extracts the VPKs into one folder (the first VPK with a file wins), unless it's already there from the same VPKs.
    /// Extracts next to it first, so a cancelled extraction never leaves a half-full folder behind.
    /// </summary>
    public static void ExtractVpks(IReadOnlyList<string> vpks, string destination, string name, IPortLog log, CancellationToken cancellation)
    {
        string stampPath = destination + ".stamp";
        string stamp = string.Join('\n', vpks.Select(x =>
        {
            var info = new FileInfo(x);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }));
        if (Directory.Exists(destination) && File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            return;

        string partial = destination + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);
        Directory.CreateDirectory(partial);

        var packages = new List<Package>();
        try
        {
            foreach (string vpk in vpks)
            {
                var package = new Package();
                packages.Add(package);
                package.Read(vpk);
            }

            int total = packages.Sum(x => x.Entries?.Values.Sum(e => e.Count) ?? 0);
            log.Info($"Extracting {name}'s VPKs ({total:N0} files) for bspPack, which only packs loose files. This happens once and takes a few minutes.");

            string root = Path.GetFullPath(partial) + Path.DirectorySeparatorChar;
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stopwatch = Stopwatch.StartNew();
            int done = 0;
            int nextReport = 10;
            foreach (Package package in packages)
            {
                foreach (PackageEntry entry in package.Entries?.Values.SelectMany(x => x) ?? [])
                {
                    cancellation.ThrowIfCancellationRequested();
                    done++;

                    string path = entry.GetFullPath();
                    string target = Path.GetFullPath(Path.Combine(partial, path));
                    if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && written.Add(path))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        package.ReadEntry(entry, out byte[] data);
                        File.WriteAllBytes(target, data);
                    }

                    if (done * 100L >= nextReport * (long)total)
                    {
                        log.Info($"Extracted {done:N0} of {total:N0} files ({nextReport}%)");
                        nextReport += 10;
                    }
                }
            }

            log.Info($"Extracted {name}'s VPKs in {stopwatch.Elapsed.TotalSeconds:0}s to {destination}");
        }
        finally
        {
            foreach (Package package in packages)
                package.Dispose();
        }

        File.Delete(stampPath);
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Directory.Move(partial, destination);
        File.WriteAllText(stampPath, stamp);
    }

    /// <summary>
    /// Points <c>bin/win64</c> at Momentum's, for bspPack to find bspzip in.
    /// </summary>
    private void LinkBspZip(string momentumInstall)
    {
        string target = Path.GetFullPath(Path.Combine(momentumInstall, "bin", BinFolder));
        if (Directory.Exists(target) && Directory.ResolveLinkTarget(target, returnFinalTarget: true) is { } linked)
            target = linked.FullName;

        string bspZip = Path.Combine(target, OperatingSystem.IsWindows() ? "bspzip.exe" : "bspzip");
        if (!File.Exists(bspZip))
            throw new PortStepException($"Momentum's bspzip wasn't found ({bspZip}); bspPack needs it to pack maps");

        string link = Path.Combine(RootDirectory, "bin", BinFolder);
        var info = new DirectoryInfo(link);
        if (info.Exists)
        {
            if (info.LinkTarget is { } current && SamePath(current, target))
                return;

            // Deleting a junction deletes the link, not what it points at
            info.Delete(recursive: info.LinkTarget is null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        CreateDirectoryLink(link, target);
    }

    /// <summary>
    /// A junction on Windows, which unlike a symbolic link needs neither admin rights nor developer mode.
    /// </summary>
    public static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
            throw new PortStepException($"Couldn't link {link} to Momentum's {target} for bspPack's bspzip: {error.Trim()}");
    }

    private static bool SamePath(string a, string b)
    {
        static string Normalize(string path)
        {
            foreach (string prefix in new[] { @"\??\", @"\\?\" })
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    path = path[prefix.Length..];
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }

        return string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool HasFile(string? directory, string file) => directory is not null && File.Exists(Path.Combine(directory, file));
}
