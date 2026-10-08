namespace MomentumPorter.Tests;

using MomentumPorter.Steam;

/// <summary>
/// A temp folder deleted when the test ends.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MomentumPorterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>A fake Momentum install (with a momentum/maps folder) to install into instead of the real one.</summary>
    public string CreateMomentumInstall()
    {
        string install = Combine("Momentum Mod");
        Directory.CreateDirectory(System.IO.Path.Combine(install, "momentum", "maps"));
        return install;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class KzMaps
{
    /// <summary>A map from the Steam install of Kreedz Climbing, or null when it isn't installed.</summary>
    public static string? Find(string name)
    {
        GameLocation? kz = new SteamLocator().Find(KnownGame.KreedzClimbing);
        if (kz?.ContentDirectory is null)
            return null;

        string path = Path.Combine(kz.ContentDirectory, "maps", name + ".bsp");
        return File.Exists(path) ? path : null;
    }
}
