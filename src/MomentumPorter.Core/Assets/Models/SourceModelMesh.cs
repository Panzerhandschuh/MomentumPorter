namespace MomentumPorter.Assets.Models;

using System.Numerics;

/// <summary>A vertex of a compiled Source model, in model space at the rest pose.</summary>
/// <param name="Bone">The bone with the most weight on the vertex</param>
public readonly record struct SourceVertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord, int Bone);

/// <summary>A triangle, its vertices counterclockwise seen from the front (as SMDs wind them).</summary>
public sealed record SourceTriangle(string Material, SourceVertex A, SourceVertex B, SourceVertex C);

/// <summary>
/// The reference mesh of a compiled Source model (.mdl, .vvd and .dx90.vtx): level of detail 0, the first model of each
/// body part, the first skin family. Enough to recompile the model's geometry with a different skeleton.
/// </summary>
public sealed class SourceModelMesh
{
    private const int VvdVertexSize = 48;
    private const int VtxVertexSize = 9;

    private SourceModelMesh(StudioModelHeader header, IReadOnlyList<SourceTriangle> triangles)
    {
        Header = header;
        Triangles = triangles;
    }

    public StudioModelHeader Header { get; }

    public IReadOnlyList<SourceTriangle> Triangles { get; }

    /// <summary>The triangles a bone or the bones below it hold.</summary>
    public SourceModelMesh OnBranch(int bone)
    {
        bool Below(int b)
        {
            for (; b >= 0 && b < Header.Bones.Count; b = Header.Bones[b].Parent)
            {
                if (b == bone)
                    return true;
            }

            return false;
        }

        return new SourceModelMesh(Header, Triangles.Where(x => Below(x.A.Bone) && Below(x.B.Bone) && Below(x.C.Bone)).ToList());
    }

    /// <exception cref="InvalidDataException">The files aren't a model's mdl, vvd and vtx</exception>
    public static SourceModelMesh Read(byte[] mdl, byte[] vvd, byte[] vtx)
    {
        StudioModelHeader header = StudioModelHeader.Read(mdl);
        List<SourceVertex> vertices = ReadVvd(vvd);
        var triangles = new List<SourceTriangle>();

        int bodyPartCount = BitConverter.ToInt32(vtx, 28);
        int bodyPartOffset = BitConverter.ToInt32(vtx, 32);
        int stripGroupSize = DetectStripGroupSize(vtx, bodyPartOffset, bodyPartCount);

        for (int p = 0; p < Math.Min(bodyPartCount, header.BodyParts.Count); p++)
        {
            StudioBodyPart part = header.BodyParts[p];
            if (part.Models.Count == 0)
                continue;

            StudioSubModel model = part.Models[0];
            if (model.Meshes.Count == 0)
                continue;

            int partHeader = bodyPartOffset + p * 8;
            int modelOffset = partHeader + BitConverter.ToInt32(vtx, partHeader + 4);
            int lodOffset = modelOffset + BitConverter.ToInt32(vtx, modelOffset + 4);
            int meshCount = BitConverter.ToInt32(vtx, lodOffset);
            int meshOffset = lodOffset + BitConverter.ToInt32(vtx, lodOffset + 4);
            int modelFirstVertex = model.VertexIndex / VvdVertexSize;

            for (int m = 0; m < Math.Min(meshCount, model.Meshes.Count); m++)
            {
                StudioMesh mesh = model.Meshes[m];
                int texture = header.SkinFamilies.Count > 0 && mesh.Material < header.SkinFamilies[0].Length ? header.SkinFamilies[0][mesh.Material] : mesh.Material;
                string material = texture < header.Textures.Count ? header.Textures[texture] : "error";
                int meshHeader = meshOffset + m * 9;
                int groupCount = BitConverter.ToInt32(vtx, meshHeader);
                int groupOffset = meshHeader + BitConverter.ToInt32(vtx, meshHeader + 4);

                for (int g = 0; g < groupCount; g++)
                {
                    int group = groupOffset + g * stripGroupSize;
                    int vertOffset = group + BitConverter.ToInt32(vtx, group + 4);
                    int indexCount = BitConverter.ToInt32(vtx, group + 8);
                    int indexOffset = group + BitConverter.ToInt32(vtx, group + 12);

                    // Every strip studiomdl writes nowadays is a triangle list, so the group's indices are triangles
                    for (int i = 0; i + 2 < indexCount; i += 3)
                    {
                        var corners = new SourceVertex[3];
                        for (int k = 0; k < 3; k++)
                        {
                            int index = BitConverter.ToUInt16(vtx, indexOffset + (i + k) * 2);
                            int original = BitConverter.ToUInt16(vtx, vertOffset + index * VtxVertexSize + 4);
                            corners[k] = vertices[modelFirstVertex + mesh.VertexOffset + original];
                        }

                        // vtx triangles wind the other way round from SMD triangles
                        triangles.Add(new SourceTriangle(material, corners[0], corners[2], corners[1]));
                    }
                }
            }
        }

        return new SourceModelMesh(header, triangles);
    }

    private static List<SourceVertex> ReadVvd(byte[] d)
    {
        if (d.Length < 64 || BitConverter.ToInt32(d, 0) != 0x56534449) // "IDSV"
            throw new InvalidDataException("Not a vvd file");

        int lod0Count = BitConverter.ToInt32(d, 16);
        int fixupCount = BitConverter.ToInt32(d, 48);
        int fixupStart = BitConverter.ToInt32(d, 52);
        int vertexStart = BitConverter.ToInt32(d, 56);

        var order = new List<int>();
        if (fixupCount == 0)
        {
            order.AddRange(Enumerable.Range(0, lod0Count));
        }
        else
        {
            // Vertices are stored per LOD range; LOD 0 takes every range whose LOD is >= 0
            for (int f = 0; f < fixupCount; f++)
            {
                int o = fixupStart + f * 12;
                int lod = BitConverter.ToInt32(d, o), source = BitConverter.ToInt32(d, o + 4), count = BitConverter.ToInt32(d, o + 8);
                if (lod >= 0)
                    order.AddRange(Enumerable.Range(source, count));
            }
        }

        var result = new List<SourceVertex>(order.Count);
        foreach (int v in order)
        {
            int o = vertexStart + v * VvdVertexSize;
            int boneCount = d[o + 15];
            int bone = 0;
            float weight = -1;
            for (int k = 0; k < boneCount; k++)
            {
                float w = BitConverter.ToSingle(d, o + k * 4);
                if (w > weight)
                {
                    weight = w;
                    bone = d[o + 12 + k];
                }
            }

            result.Add(new SourceVertex(
                Vec(d, o + 16),
                Vec(d, o + 28),
                // SMD v runs up, vvd v runs down
                new Vector2(BitConverter.ToSingle(d, o + 40), 1f - BitConverter.ToSingle(d, o + 44)),
                bone));
        }

        return result;
    }

    /// <summary>
    /// Source 2013 games write 25 byte strip group headers; CS:GO-era studiomdl (and Strata's) add two ints (33 bytes).
    /// Both are version 7, so pick the size whose headers all point inside the file.
    /// </summary>
    private static int DetectStripGroupSize(byte[] vtx, int bodyPartOffset, int bodyPartCount)
    {
        foreach (int size in new[] { 25, 33 })
        {
            if (AllGroupsValid(size))
                return size;
        }

        return 25;

        bool AllGroupsValid(int size)
        {
            try
            {
                for (int p = 0; p < bodyPartCount; p++)
                {
                    int part = bodyPartOffset + p * 8;
                    int models = BitConverter.ToInt32(vtx, part), modelOffset = part + BitConverter.ToInt32(vtx, part + 4);
                    for (int m = 0; m < models; m++)
                    {
                        int model = modelOffset + m * 8;
                        int lodCount = BitConverter.ToInt32(vtx, model), lodOffset = model + BitConverter.ToInt32(vtx, model + 4);
                        for (int l = 0; l < lodCount; l++)
                        {
                            int lod = lodOffset + l * 12;
                            int meshes = BitConverter.ToInt32(vtx, lod), meshOffset = lod + BitConverter.ToInt32(vtx, lod + 4);
                            for (int e = 0; e < meshes; e++)
                            {
                                int mesh = meshOffset + e * 9;
                                int groups = BitConverter.ToInt32(vtx, mesh), groupOffset = mesh + BitConverter.ToInt32(vtx, mesh + 4);
                                for (int g = 0; g < groups; g++)
                                {
                                    int group = groupOffset + g * size;
                                    int verts = BitConverter.ToInt32(vtx, group), vertOffset = group + BitConverter.ToInt32(vtx, group + 4);
                                    int indices = BitConverter.ToInt32(vtx, group + 8), indexOffset = group + BitConverter.ToInt32(vtx, group + 12);
                                    int strips = BitConverter.ToInt32(vtx, group + 16);
                                    if (verts < 0 || indices < 0 || indices % 3 != 0 || strips < 0 || strips > 10000
                                        || vertOffset + verts * VtxVertexSize > vtx.Length || indexOffset + indices * 2 > vtx.Length)
                                        return false;

                                    for (int i = 0; i < indices; i++)
                                    {
                                        if (BitConverter.ToUInt16(vtx, indexOffset + i * 2) >= verts)
                                            return false;
                                    }
                                }
                            }
                        }
                    }
                }

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    private static Vector3 Vec(byte[] d, int o) => new(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8));
}
