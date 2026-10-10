namespace MomentumPorter.Assets.Models;

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// Small edits to a compiled Source studio model (.mdl) that don't need a recompile.
/// </summary>
public static class StudioModelPatcher
{
    private const int LengthOffset = 76;
    private const int CdTextureCountOffset = 212;
    private const int CdTextureIndexOffset = 216;

    /// <summary>
    /// Puts folders in front of the model's <c>$cdmaterials</c>. The engine looks a material up in each folder in
    /// order and takes the first it finds, so a material in one of the new folders wins and the rest still come from
    /// the old ones. The folder names and the new folder list are appended to the file, and the header's offsets and
    /// length point at them; nothing else moves.
    /// </summary>
    /// <param name="directories">Folders under <c>materials/</c>, e.g. <c>porter\css\models\weapons\v_models\knife_t\</c></param>
    public static byte[] PrependMaterialDirectories(byte[] mdl, IReadOnlyList<string> directories)
    {
        if (directories.Count == 0)
            return mdl;

        if (mdl.Length < CdTextureIndexOffset + 4 || BinaryPrimitives.ReadInt32LittleEndian(mdl) != StudioModelHeader.StudioHeaderId)
            throw new InvalidDataException("Not a Source studio model (no IDST header)");

        int count = BinaryPrimitives.ReadInt32LittleEndian(mdl.AsSpan(CdTextureCountOffset));
        int index = BinaryPrimitives.ReadInt32LittleEndian(mdl.AsSpan(CdTextureIndexOffset));
        if (count < 0 || index < 0 || index + count * 4 > mdl.Length)
            throw new InvalidDataException("The model's $cdmaterials list is outside the file");

        using var output = new MemoryStream(mdl.Length + 256);
        output.Write(mdl);
        Align(output);

        var offsets = new List<int>();
        foreach (string directory in directories)
        {
            offsets.Add((int)output.Position);
            output.Write(Encoding.ASCII.GetBytes(directory));
            output.WriteByte(0);
        }

        for (int i = 0; i < count; i++)
            offsets.Add(BinaryPrimitives.ReadInt32LittleEndian(mdl.AsSpan(index + i * 4)));

        Align(output);
        int newIndex = (int)output.Position;
        Span<byte> value = stackalloc byte[4];
        foreach (int offset in offsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(value, offset);
            output.Write(value);
        }

        byte[] result = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(CdTextureCountOffset), offsets.Count);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(CdTextureIndexOffset), newIndex);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(LengthOffset), result.Length);
        return result;
    }

    private static void Align(MemoryStream stream)
    {
        while (stream.Position % 4 != 0)
            stream.WriteByte(0);
    }
}
