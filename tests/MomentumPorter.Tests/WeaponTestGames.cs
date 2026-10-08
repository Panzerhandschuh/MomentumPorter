namespace MomentumPorter.Tests;

using MomentumPorter.Assets.Vpk;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Steam;

/// <summary>
/// The installed games weapon tests read, or null when they aren't installed (those tests then do nothing, since
/// game files can't be shipped as fixtures).
/// </summary>
internal static class WeaponTestGames
{
    private static readonly Lazy<SteamLocator> Steam = new(() => new SteamLocator());
    private static readonly Lazy<MomentumInstall?> MomentumInstallLazy = new(() => MomentumInstall.Find(Steam.Value));

    public static SteamLocator Locator => Steam.Value;

    public static MomentumInstall? Momentum => MomentumInstallLazy.Value;

    public static GameContent? Open(string gameId)
    {
        try
        {
            return SourceGames.Open(SourceGames.Find(gameId)!, Steam.Value, Momentum?.InstallDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static string TempDirectory(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "MomentumPorterTests", $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
