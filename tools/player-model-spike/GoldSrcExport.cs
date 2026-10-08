namespace PlayerModelSpike;

using BSPConvert.Lib.GoldSrc;

/// <summary>
/// GoldSrc (.mdl v10) player model -> reference SMD (first model of each body part) plus a one-frame SMD per sequence,
/// using BSPConvert's GoldSrcModel reader. Like the Source decompiler, everything is written in compiled model space.
/// </summary>
public static class GoldSrcExport
{
    public static void Export(string mdlPath, string outDir)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(mdlPath))!;
        var model = GoldSrcModel.Read(mdlPath, name => File.Exists(Path.Combine(dir, name)) ? Path.Combine(dir, name) : null, out string? error)
            ?? throw new InvalidDataException(error);
        Directory.CreateDirectory(outDir);

        var smd = new Smd();
        for (int i = 0; i < model.Bones.Count; i++)
            smd.Nodes.Add(new SmdNode(i, model.Bones[i].Name, model.Bones[i].Parent));
        smd.Frames.Add(model.Bones.Select(b => new SmdBonePose(b.Position, b.Rotation)).ToArray());

        foreach (var part in model.BodyParts)
        {
            if (part.Models.Count == 0)
                continue;

            foreach (var triangle in part.Models[0].Triangles)
            {
                int texture = model.SkinFamilies.Length > 0 && triangle.SkinRef >= 0 && triangle.SkinRef < model.SkinFamilies[0].Length
                    ? model.SkinFamilies[0][triangle.SkinRef]
                    : triangle.SkinRef;
                string material = texture >= 0 && texture < model.Textures.Count ? Path.GetFileNameWithoutExtension(model.Textures[texture].Name) : "missing";
                smd.Triangles.Add(new SmdTriangle(material, Convert(triangle.A), Convert(triangle.B), Convert(triangle.C)));
            }

            Console.WriteLine($"body part {part.Name}: {part.Models.Count} models, first has {part.Models[0].Triangles.Count} triangles");
        }

        smd.Write(Path.Combine(outDir, "reference.smd"));
        Console.WriteLine($"{model.Bones.Count} bones: {string.Join(", ", model.Bones.Select(b => b.Name))}");
        Console.WriteLine($"{model.Sequences.Count} sequences, textures: {string.Join(", ", model.Textures.Select(t => $"{t.Name} {t.Width}x{t.Height}"))}");

        // First frame of a few sequences, for posing/rendering checks
        for (int s = 0; s < Math.Min(model.Sequences.Count, 4); s++)
        {
            var sequence = model.Sequences[s];
            var anim = new Smd();
            anim.Nodes.AddRange(smd.Nodes);
            anim.Frames.Add(Enumerable.Range(0, model.Bones.Count).Select(b => new SmdBonePose(sequence.Positions[0][b], sequence.Rotations[0][b])).ToArray());
            anim.Write(Path.Combine(outDir, $"seq_{s}_{sequence.Name}.smd"));
        }
    }

    private static SmdVertex Convert(GoldSrcModel.Vertex v) => new()
    {
        ParentBone = v.Bone,
        Position = v.Position,
        Normal = v.Normal,
        TexCoord = v.TexCoord,
        Links = [new SmdLink(v.Bone, 1f)],
    };
}
