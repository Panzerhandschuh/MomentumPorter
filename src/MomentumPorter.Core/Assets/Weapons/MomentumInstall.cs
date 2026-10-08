namespace MomentumPorter.Assets.Weapons;

using MomentumPorter.Steam;

/// <summary>
/// A Momentum Mod install, read only: its base scripts and tools. Packs are never written into it.
/// </summary>
public sealed class MomentumInstall
{
    public MomentumInstall(string installDirectory)
    {
        InstallDirectory = Path.GetFullPath(installDirectory);
        GameDirectory = Path.Combine(InstallDirectory, "momentum");
        if (!File.Exists(Path.Combine(GameDirectory, "gameinfo.txt")))
            throw new DirectoryNotFoundException($"{InstallDirectory} isn't a Momentum Mod install (no momentum/gameinfo.txt)");
    }

    /// <summary>steamapps/common/Momentum Mod (Playtest)</summary>
    public string InstallDirectory { get; }

    /// <summary>The <c>momentum</c> folder, with gameinfo.txt</summary>
    public string GameDirectory { get; }

    public string StudioMdlPath => Path.Combine(InstallDirectory, "bin", "win64", "studiomdl.exe");

    /// <summary>
    /// Finds Momentum through Steam (a release install first, then the playtest).
    /// </summary>
    public static MomentumInstall? Find(SteamLocator steam)
    {
        foreach (KnownGame game in new[] { KnownGame.Momentum, KnownGame.MomentumPlaytest })
        {
            foreach (string content in Assets.Vpk.SourceGames.FindContentDirectories(steam, game))
            {
                if (File.Exists(Path.Combine(content, "gameinfo.txt")))
                    return new MomentumInstall(Path.GetDirectoryName(content)!);
            }
        }

        return null;
    }

    /// <summary>
    /// A loose file of the base game (e.g. <c>scripts/weapon_momentum_pistol.txt</c>), or null.
    /// </summary>
    public string? ReadText(string path)
    {
        string file = Path.Combine(GameDirectory, Assets.Vpk.ContentPath.Normalize(path).Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    public string ReadRequiredText(string path)
    {
        return ReadText(path) ?? throw new FileNotFoundException($"Momentum's {path} is missing; verify the game's files", path);
    }
}
