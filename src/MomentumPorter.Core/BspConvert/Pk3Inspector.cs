namespace MomentumPorter.BspConvert;

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MomentumPorter.Detection;

/// <summary>
/// A map inside a .pk3/.zip (or a loose .bsp).
/// </summary>
/// <param name="Name">The map's name as BSPConvert sees it: the file name without extension, which is what <see cref="BspConvertOptions.Maps"/> matches</param>
/// <param name="EntryPath">Path inside the archive, or the file's path for a loose BSP</param>
/// <param name="Kind"><see cref="InputKind.Quake3Bsp"/>, <see cref="InputKind.GoldSrcBsp"/>, <see cref="InputKind.SourceBsp"/> or <see cref="InputKind.Unknown"/></param>
public sealed record ArchiveMap(string Name, string EntryPath, long Size, InputKind Kind)
{
    public bool CanConvert => Kind is InputKind.Quake3Bsp or InputKind.GoldSrcBsp;
}

/// <summary>
/// Lists the maps in a Quake 3 .pk3 or a GoldSrc map .zip without extracting it, so a UI can let the user pick
/// which ones to convert (<see cref="BspConvertOptions.Maps"/>). Uses System.IO.Compression, the same zip reader
/// BSPConvert extracts archives with, so it sees exactly the BSPs BSPConvert will.
/// </summary>
public static class Pk3Inspector
{
    public static bool IsArchive(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".pk3", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ArchiveMap> ListMaps(string path)
    {
        if (!IsArchive(path))
        {
            InputKind kind = InputDetector.Detect(path);
            return [new ArchiveMap(Path.GetFileNameWithoutExtension(path), path, new FileInfo(path).Length, kind)];
        }

        var maps = new List<ArchiveMap>();
        using ZipArchive archive = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // BSPConvert finds them with Directory.GetFiles("*.bsp"), which is case-insensitive on Windows
            if (!entry.FullName.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
                continue;

            InputKind kind;
            using (Stream stream = entry.Open())
                kind = DetectBsp(stream);

            maps.Add(new ArchiveMap(Path.GetFileNameWithoutExtension(entry.Name), entry.FullName, entry.Length, kind));
        }

        return maps.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether an input holds Quake 3 maps, i.e. needs Quake 3's base content to convert well.
    /// </summary>
    public static bool HasQuake3Maps(string path)
    {
        return ListMaps(path).Any(x => x.Kind == InputKind.Quake3Bsp);
    }

    // Same rules as InputDetector, on a stream
    private static InputKind DetectBsp(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return InputKind.Unknown;

        string magic = Encoding.ASCII.GetString(header[..4]);
        int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (magic == "VBSP")
            return InputKind.SourceBsp;

        if (magic == "IBSP" && version == 38)
            return InputKind.Unknown;

        if (magic is "IBSP" or "RBSP" or "FAKK" or "EF2!" or "2015")
            return InputKind.Quake3Bsp;

        return BinaryPrimitives.ReadInt32LittleEndian(header[..4]) is 29 or 30 ? InputKind.GoldSrcBsp : InputKind.Unknown;
    }
}
