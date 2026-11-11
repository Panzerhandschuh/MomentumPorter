namespace MomentumPorter.Assets.Particles;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The particle systems a .pcf defines, by name: the <c>DmeParticleSystemDefinition</c> elements of a binary or
/// keyvalues2 DMX file. Only the element headers are read.
/// </summary>
public static partial class ParticleSystemNames
{
    /// <exception cref="InvalidDataException">The file isn't a DMX file this can read</exception>
    public static List<string> Read(byte[] data)
    {
        int headerEnd = Array.IndexOf(data, (byte)0);
        int lineEnd = Array.IndexOf(data, (byte)'\n');
        int end = headerEnd < 0 ? lineEnd : lineEnd < 0 ? headerEnd : Math.Min(headerEnd, lineEnd);
        if (end < 0)
            throw new InvalidDataException("No DMX header");

        Match header = HeaderRegex().Match(Encoding.ASCII.GetString(data, 0, end));
        if (!header.Success)
            throw new InvalidDataException("No DMX header");

        string encoding = header.Groups[1].Value;
        int version = int.Parse(header.Groups[2].Value);
        return encoding.Equals("binary", StringComparison.OrdinalIgnoreCase)
            ? ReadBinary(data, version)
            : TextDefinitionRegex().Matches(Encoding.Latin1.GetString(data)).Select(x => x.Groups[1].Value).ToList();
    }

    // Layout as in Strata's dmxloader.cpp: v2 added the string table, v4 element names in it, v5 int symbols
    private static List<string> ReadBinary(byte[] data, int version)
    {
        using var reader = new BinaryReader(new MemoryStream(data), Encoding.Latin1);
        try
        {
            while (reader.ReadByte() != 0)
            {
            }

            string[]? strings = null;
            if (version >= 2)
            {
                int count = version >= 4 ? reader.ReadInt32() : reader.ReadInt16();
                strings = new string[count];
                for (int i = 0; i < count; i++)
                    strings[i] = ReadString(reader);
            }

            string Symbol() => strings![version >= 5 ? reader.ReadInt32() : reader.ReadInt16()];

            int elements = reader.ReadInt32();
            var names = new List<string>();
            for (int i = 0; i < elements; i++)
            {
                string type = strings is null ? ReadString(reader) : Symbol();
                string name = strings is not null && version >= 4 ? Symbol() : ReadString(reader);
                reader.BaseStream.Position += 16;
                if (type == "DmeParticleSystemDefinition")
                    names.Add(name);
            }

            return names;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("Truncated or corrupt DMX file", ex);
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (byte b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            bytes.Add(b);

        return Encoding.Latin1.GetString(bytes.ToArray());
    }

    [GeneratedRegex(@"encoding (\S+) (\d+)")]
    private static partial Regex HeaderRegex();

    [GeneratedRegex("\"DmeParticleSystemDefinition\"\\s*\\{\\s*\"id\"\\s*\"elementid\"\\s*\"[^\"]*\"\\s*\"name\"\\s*\"string\"\\s*\"([^\"]*)\"")]
    private static partial Regex TextDefinitionRegex();
}
