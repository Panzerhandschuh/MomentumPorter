namespace MomentumPorter.Detection;

using System.Buffers.Binary;
using System.Text;

public enum InputKind
{
    Unknown,
    SourceBsp,
    GoldSrcBsp,
    Quake3Bsp,
    /// <summary>Quake 3 archive (zip), may hold several maps.</summary>
    Pk3Archive,
    /// <summary>Zip that isn't a pk3, e.g. a GoldSrc map release.</summary>
    ZipArchive,
    Vpk,
    StudioModel,
    Wav,
}

/// <summary>
/// Works out what a dropped file is from its extension and header, without loading it.
/// </summary>
public static class InputDetector
{
    public static InputKind Detect(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".bsp":
                return DetectBsp(path);
            case ".pk3":
                return InputKind.Pk3Archive;
            case ".zip":
                return InputKind.ZipArchive;
            case ".vpk":
                return InputKind.Vpk;
            case ".mdl":
                return InputKind.StudioModel;
            case ".wav":
                return InputKind.Wav;
            default:
                return InputKind.Unknown;
        }
    }

    public static bool IsMap(InputKind kind)
    {
        return kind is InputKind.SourceBsp or InputKind.GoldSrcBsp or InputKind.Quake3Bsp or InputKind.Pk3Archive or InputKind.ZipArchive;
    }

    private static InputKind DetectBsp(string path)
    {
        Span<byte> header = stackalloc byte[8];
        using (FileStream stream = File.OpenRead(path))
        {
            if (stream.Read(header) < 8)
                return InputKind.Unknown;
        }

        string magic = Encoding.ASCII.GetString(header[..4]);
        int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (magic == "VBSP")
            return InputKind.SourceBsp;

        // Quake 3 and its derivatives (Raven's RBSP, Call of Duty) all start with a 4 character magic
        // "IBSP" is also Quake 2 (version 38); Quake 3 is 46 (1.1 to 1.32) or 47 (Quake Live, ET)
        if (magic is "IBSP" && version is 38)
            return InputKind.Unknown;

        if (magic is "IBSP" or "RBSP" or "FAKK" or "EF2!" or "2015")
            return InputKind.Quake3Bsp;

        // GoldSrc (30) and Quake 1 (29) BSPs have no magic, just the version
        int bareVersion = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
        if (bareVersion is 29 or 30)
            return InputKind.GoldSrcBsp;

        return InputKind.Unknown;
    }
}
