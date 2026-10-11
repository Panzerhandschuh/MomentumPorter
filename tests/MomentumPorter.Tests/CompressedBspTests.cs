namespace MomentumPorter.Tests;

using LibBSP;
using MomentumPorter.Steam;
using Xunit;

/// <summary>
/// Compressed output has to pass Momentum's map submission, which takes a BSP as compressed only when every lump with
/// data but the game lump and the pakfile has its uncompressed size set (website libs/formats/bsp/src/bsp-header.ts).
/// Uses an installed Source map (KZ Mod's kz_dance, else a Counter-Strike: Source map); nothing runs without one.
/// </summary>
public class CompressedBspTests
{
    private const int GameLumpIndex = 35;
    private const int PakFileIndex = 40;

    [Fact]
    public void CompressedMapPassesTheWebsitesCheck()
    {
        if (FindSourceMap() is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        string temp = Path.Combine(Path.GetTempPath(), $"compressed_{Guid.NewGuid():N}.bsp");
        try
        {
            new BSPWriter(bsp).WriteBSP(temp, true);

            Assert.Empty(GetUncompressedLumps(temp));
            var reloaded = new BSP(new FileInfo(temp));
            Assert.Equal(bsp.Entities.Count, reloaded.Entities.Count);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    // The lumps the website would call uncompressed: data but no uncompressed size (fourCC)
    private static List<int> GetUncompressedLumps(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        Assert.Equal("VBSP"u8.ToArray(), reader.ReadBytes(4));
        reader.ReadInt32();

        var uncompressed = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            reader.ReadInt32();
            int length = reader.ReadInt32();
            reader.ReadInt32();
            int fourCC = reader.ReadInt32();
            if (i is not (GameLumpIndex or PakFileIndex) && length > 0 && fourCC <= 0)
                uncompressed.Add(i);
        }

        return uncompressed;
    }

    private static string? FindSourceMap()
    {
        var steam = new SteamLocator();
        if (steam.Find(KnownGame.KreedzClimbing)?.ContentDirectory is { } kz && File.Exists(Path.Combine(kz, "maps", "kz_dance.bsp")))
            return Path.Combine(kz, "maps", "kz_dance.bsp");

        if (steam.Find(KnownGame.CounterStrikeSource)?.ContentDirectory is { } css && Directory.Exists(Path.Combine(css, "maps")))
            return Directory.EnumerateFiles(Path.Combine(css, "maps"), "*.bsp").FirstOrDefault();

        return null;
    }
}
