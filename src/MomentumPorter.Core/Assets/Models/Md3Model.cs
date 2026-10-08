namespace MomentumPorter.Assets.Models;

using System.Numerics;
using System.Text;

/// <summary>
/// A Quake 3 MD3 model: vertex-animated surfaces and tags (attachment points) per frame. Coordinates are Quake's,
/// which are Source's too (x forward, y left, z up, about the same units).
/// </summary>
public sealed class Md3Model
{
    /// <param name="Axis">The tag's x, y and z axes in model space</param>
    public sealed record Tag(string Name, Vector3 Origin, Vector3[] Axis)
    {
        /// <summary>Takes a point in the tagged model's space to this model's space.</summary>
        public Vector3 Transform(Vector3 point) => Origin + Axis[0] * point.X + Axis[1] * point.Y + Axis[2] * point.Z;

        public Vector3 TransformNormal(Vector3 normal) => Vector3.Normalize(Axis[0] * normal.X + Axis[1] * normal.Y + Axis[2] * normal.Z);
    }

    /// <param name="Shaders">Shader (texture) names; the first is the one drawn</param>
    /// <param name="Positions">Each frame's vertex positions</param>
    /// <param name="Normals">Each frame's vertex normals</param>
    /// <param name="TexCoords">Texture coordinates, t down from the top like Quake's</param>
    /// <param name="Triangles">Vertex indices, three per triangle</param>
    public sealed record Surface(string Name, string[] Shaders, Vector3[][] Positions, Vector3[][] Normals, Vector2[] TexCoords, int[] Triangles);

    public string Name { get; private init; } = "";

    public int FrameCount { get; private init; }

    /// <summary>Each frame's tags.</summary>
    public Tag[][] Tags { get; private init; } = [];

    public List<Surface> Surfaces { get; } = [];

    public Tag? FindTag(string name, int frame = 0)
    {
        return frame < Tags.Length ? Tags[frame].FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) : null;
    }

    /// <exception cref="InvalidDataException">Not an MD3 (IDP3 version 15)</exception>
    public static Md3Model Read(byte[] data)
    {
        if (data.Length < 108 || Encoding.ASCII.GetString(data, 0, 4) != "IDP3" || BitConverter.ToInt32(data, 4) != 15)
            throw new InvalidDataException("not an MD3 (IDP3 version 15)");

        int frameCount = Int(data, 76);
        int tagCount = Int(data, 80);
        int surfaceCount = Int(data, 84);
        int tagOffset = Int(data, 96);
        int surfaceOffset = Int(data, 100);

        var tags = new Tag[frameCount][];
        for (int frame = 0; frame < frameCount; frame++)
        {
            tags[frame] = new Tag[tagCount];
            for (int t = 0; t < tagCount; t++)
            {
                int offset = tagOffset + (frame * tagCount + t) * 112;
                tags[frame][t] = new Tag(String(data, offset, 64), Vec(data, offset + 64), [Vec(data, offset + 76), Vec(data, offset + 88), Vec(data, offset + 100)]);
            }
        }

        var model = new Md3Model { Name = String(data, 8, 64), FrameCount = frameCount, Tags = tags };
        int surface = surfaceOffset;
        for (int s = 0; s < surfaceCount; s++)
        {
            if (Encoding.ASCII.GetString(data, surface, 4) != "IDP3")
                throw new InvalidDataException($"surface {s} has a bad header");

            string name = String(data, surface + 4, 64);
            int surfaceFrames = Int(data, surface + 72);
            int shaderCount = Int(data, surface + 76);
            int vertexCount = Int(data, surface + 80);
            int triangleCount = Int(data, surface + 84);
            int trianglesAt = surface + Int(data, surface + 88);
            int shadersAt = surface + Int(data, surface + 92);
            int texCoordsAt = surface + Int(data, surface + 96);
            int verticesAt = surface + Int(data, surface + 100);

            string[] shaders = Enumerable.Range(0, shaderCount).Select(i => String(data, shadersAt + i * 68, 64)).ToArray();
            int[] triangles = Enumerable.Range(0, triangleCount * 3).Select(i => Int(data, trianglesAt + i * 4)).ToArray();
            Vector2[] texCoords = Enumerable.Range(0, vertexCount)
                .Select(i => new Vector2(BitConverter.ToSingle(data, texCoordsAt + i * 8), BitConverter.ToSingle(data, texCoordsAt + i * 8 + 4)))
                .ToArray();

            var positions = new Vector3[surfaceFrames][];
            var normals = new Vector3[surfaceFrames][];
            for (int frame = 0; frame < surfaceFrames; frame++)
            {
                positions[frame] = new Vector3[vertexCount];
                normals[frame] = new Vector3[vertexCount];
                for (int v = 0; v < vertexCount; v++)
                {
                    int offset = verticesAt + (frame * vertexCount + v) * 8;
                    positions[frame][v] = new Vector3(BitConverter.ToInt16(data, offset), BitConverter.ToInt16(data, offset + 2), BitConverter.ToInt16(data, offset + 4)) / 64f;
                    normals[frame][v] = DecodeNormal(BitConverter.ToUInt16(data, offset + 6));
                }
            }

            model.Surfaces.Add(new Surface(name, shaders, positions, normals, texCoords, triangles));
            surface += Int(data, surface + 104);
        }

        return model;
    }

    // Latitude in the high byte, longitude in the low one, each a 256th of a turn
    private static Vector3 DecodeNormal(ushort packed)
    {
        float lat = (packed >> 8 & 0xFF) * (2 * MathF.PI / 256f);
        float lng = (packed & 0xFF) * (2 * MathF.PI / 256f);
        return new Vector3(MathF.Cos(lat) * MathF.Sin(lng), MathF.Sin(lat) * MathF.Sin(lng), MathF.Cos(lng));
    }

    private static int Int(byte[] data, int offset) => BitConverter.ToInt32(data, offset);

    private static Vector3 Vec(byte[] data, int offset) =>
        new(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));

    private static string String(byte[] data, int offset, int length)
    {
        int end = Array.IndexOf(data, (byte)0, offset, length);
        return Encoding.ASCII.GetString(data, offset, (end >= 0 ? end : offset + length) - offset);
    }
}
