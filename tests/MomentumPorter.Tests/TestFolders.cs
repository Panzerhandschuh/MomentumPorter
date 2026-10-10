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
            Delete(Path);
        }
        catch (IOException)
        {
        }
    }

    // Unlinks junctions on its own, since a recursive delete unmounts them with DeleteVolumeMountPoint, which needs admin
    private static void Delete(string directory)
    {
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            var info = new DirectoryInfo(child);
            if (info.LinkTarget is not null)
                info.Delete();
            else
                Delete(child);
        }

        Directory.Delete(directory, recursive: true);
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
