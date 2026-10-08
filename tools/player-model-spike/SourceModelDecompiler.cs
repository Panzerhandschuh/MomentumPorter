namespace PlayerModelSpike;

using System.Numerics;
using MomentumPorter.Assets.Models;

/// <summary>
/// Rebuilds the reference mesh of a compiled Source model (.mdl + .vvd + .dx90.vtx) as an SMD: the skeleton in its rest
/// pose and every body group model's LOD 0 triangles, weighted as compiled. Enough to recompile the model's look;
/// animations, flexes, LODs and the physics model aren't rebuilt (Crowbar does all of that, but only from its GUI).
/// </summary>
public static class SourceModelDecompiler
{
    private const int VvdVertexSize = 48;
    private const int VtxVertexSize = 9;

    public sealed record Result(Smd Reference, StudioModelHeader Header, List<string> Log);

    /// <param name="bodyGroupModels">Per body part, which model to export (default: the first of each)</param>
    public static Result Decompile(string mdlPath, Func<string, int>? bodyGroupModels = null)
    {
        var log = new List<string>();
        var header = StudioModelHeader.Read(mdlPath);
        string basePath = Path.ChangeExtension(mdlPath, null);
        var vertices = ReadVvd(basePath + ".vvd", log);
        byte[] vtx = File.ReadAllBytes(File.Exists(basePath + ".dx90.vtx") ? basePath + ".dx90.vtx" : basePath + ".vtx");

        var smd = new Smd();
        foreach (var bone in header.Bones)
            smd.Nodes.Add(new SmdNode(bone.Index, bone.Name, bone.Parent));
        smd.Frames.Add(header.Bones.Select(b => new SmdBonePose(b.Position, b.Rotation)).ToArray());

        int bodyPartCount = BitConverter.ToInt32(vtx, 28);
        int bodyPartOffset = BitConverter.ToInt32(vtx, 32);
        if (bodyPartCount != header.BodyParts.Count)
            log.Add($"vtx has {bodyPartCount} body parts, mdl has {header.BodyParts.Count}");

        int stripGroupSize = DetectStripGroupSize(vtx, bodyPartOffset, bodyPartCount);
        log.Add($"vtx strip group header size: {stripGroupSize}");

        for (int p = 0; p < Math.Min(bodyPartCount, header.BodyParts.Count); p++)
        {
            var part = header.BodyParts[p];
            int partHeader = bodyPartOffset + p * 8;
            int modelCount = BitConverter.ToInt32(vtx, partHeader);
            int modelOffset = partHeader + BitConverter.ToInt32(vtx, partHeader + 4);
            int chosen = bodyGroupModels?.Invoke(part.Name) ?? 0;
            if (chosen >= part.Models.Count)
                continue;

            var model = part.Models[chosen];
            if (model.Meshes.Count == 0)
            {
                log.Add($"body part {part.Name}: model {chosen} ({model.Name}) is blank");
                continue;
            }

            int modelHeader = modelOffset + chosen * 8;
            int lodOffset = modelHeader + BitConverter.ToInt32(vtx, modelHeader + 4);
            int meshCount = BitConverter.ToInt32(vtx, lodOffset);
            int meshOffset = lodOffset + BitConverter.ToInt32(vtx, lodOffset + 4);
            int modelFirstVertex = model.VertexIndex / VvdVertexSize;
            int triangles = 0;

            for (int m = 0; m < Math.Min(meshCount, model.Meshes.Count); m++)
            {
                var mesh = model.Meshes[m];
                string material = header.Textures[header.SkinFamilies.Count > 0 ? header.SkinFamilies[0][mesh.Material] : mesh.Material];
                int meshHeader = meshOffset + m * 9;
                int groupCount = BitConverter.ToInt32(vtx, meshHeader);
                int groupOffset = meshHeader + BitConverter.ToInt32(vtx, meshHeader + 4);

                for (int g = 0; g < groupCount; g++)
                {
                    int group = groupOffset + g * stripGroupSize;
                    int vertCount = BitConverter.ToInt32(vtx, group);
                    int vertOffset = group + BitConverter.ToInt32(vtx, group + 4);
                    int indexCount = BitConverter.ToInt32(vtx, group + 8);
                    int indexOffset = group + BitConverter.ToInt32(vtx, group + 12);

                    // Every strip studiomdl writes nowadays is a triangle list, so the group's indices are triangles
                    for (int i = 0; i + 2 < indexCount; i += 3)
                    {
                        var corners = new SmdVertex[3];
                        for (int k = 0; k < 3; k++)
                        {
                            int index = BitConverter.ToUInt16(vtx, indexOffset + (i + k) * 2);
                            int original = BitConverter.ToUInt16(vtx, vertOffset + index * VtxVertexSize + 4);
                            corners[k] = vertices[modelFirstVertex + mesh.VertexOffset + original].Clone();
                        }

                        // vtx triangles wind the other way round from SMD triangles
                        smd.Triangles.Add(new SmdTriangle(material, corners[0], corners[2], corners[1]));
                        triangles++;
                    }
                }
            }

            log.Add($"body part {part.Name}: model {chosen} ({model.Name}), {triangles} triangles");
        }

        return new Result(smd, header, log);
    }

    private static List<SmdVertex> ReadVvd(string path, List<string> log)
    {
        byte[] d = File.ReadAllBytes(path);
        if (BitConverter.ToInt32(d, 0) != 0x56534449) // "IDSV"
            throw new InvalidDataException($"{path} isn't a vvd");

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

        log.Add($"vvd: {lod0Count} LOD 0 vertices, {fixupCount} fixups");
        var result = new List<SmdVertex>(order.Count);
        foreach (int v in order)
        {
            int o = vertexStart + v * VvdVertexSize;
            int boneCount = d[o + 15];
            var vertex = new SmdVertex
            {
                Position = Vec(d, o + 16),
                Normal = Vec(d, o + 28),
                // SMD v runs up, vvd v runs down
                TexCoord = new Vector2(BitConverter.ToSingle(d, o + 40), 1f - BitConverter.ToSingle(d, o + 44)),
            };
            for (int k = 0; k < boneCount; k++)
                vertex.Links.Add(new SmdLink(d[o + 12 + k], BitConverter.ToSingle(d, o + k * 4)));
            vertex.ParentBone = vertex.Links.Count > 0 ? vertex.Links.MaxBy(l => l.Weight).Bone : 0;
            result.Add(vertex);
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
