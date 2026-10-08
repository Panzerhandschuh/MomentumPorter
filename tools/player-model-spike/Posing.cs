namespace PlayerModelSpike;

using System.Numerics;

/// <summary>
/// Plays one frame of an animation SMD on a reference SMD the way the engine plays an $includemodel animation: bones
/// are matched by name and take the animation's local position and rotation (so the animation's bone lengths win),
/// bones the animation lacks keep the mesh's rest pose, and vertices follow by linear blend skinning.
/// </summary>
public static class Posing
{
    public static Smd Pose(Smd mesh, Smd anim, int frame, out List<string> missing)
    {
        missing = [];
        var rest = mesh.GetWorldMatrices(0);
        var pose = new SmdBonePose[mesh.Nodes.Count];
        for (int i = 0; i < mesh.Nodes.Count; i++)
        {
            int a = anim.FindNode(mesh.Nodes[i].Name);
            if (a >= 0)
            {
                pose[i] = anim.Frames[Math.Min(frame, anim.Frames.Count - 1)][a];
            }
            else
            {
                pose[i] = mesh.Frames[0][i];
                missing.Add(mesh.Nodes[i].Name);
            }
        }

        var posed = new Smd();
        posed.Nodes.AddRange(mesh.Nodes);
        posed.Frames.Add(pose);
        var world = posed.GetWorldMatrices(0);
        var skin = new Matrix4x4[mesh.Nodes.Count];
        for (int i = 0; i < skin.Length; i++)
            skin[i] = SmdMath.Inverse(rest[i]) * world[i];

        foreach (var triangle in mesh.Triangles)
        {
            var corners = triangle.Vertices.Select(v =>
            {
                var moved = v.Clone();
                moved.Position = Vector3.Zero;
                moved.Normal = Vector3.Zero;
                float total = 0f;
                foreach (var link in v.Weights)
                {
                    moved.Position += Vector3.Transform(v.Position, skin[link.Bone]) * link.Weight;
                    moved.Normal += Vector3.TransformNormal(v.Normal, skin[link.Bone]) * link.Weight;
                    total += link.Weight;
                }

                moved.Position /= total;
                moved.Normal = Vector3.Normalize(moved.Normal);
                return moved;
            }).ToArray();
            posed.Triangles.Add(triangle with { A = corners[0], B = corners[1], C = corners[2] });
        }

        return posed;
    }

    /// <summary>
    /// Converts an animation SMD exported from the source rig (e.g. Momentum's modelsrc anims) into the compiled model's
    /// bone frames. studiomdl does the same when $definebone carries fixups: each bone's axes are turned by a fixed
    /// rotation F (compiled rest = F * source rest, in model space), so every frame becomes F * source world. F is derived
    /// from the rig's rest skeleton (animRest) and the compiled skeleton, so no fixup table is needed.
    /// </summary>
    public static Smd BakeToCompiled(Smd compiled, Smd animRest, Smd anim, List<string> log)
    {
        var compiledRest = compiled.GetWorldMatrices(0);
        var rigRest = animRest.GetWorldMatrices(0);
        var fixups = new Matrix4x4?[compiled.Nodes.Count];
        var animIndex = new int[compiled.Nodes.Count];
        for (int i = 0; i < compiled.Nodes.Count; i++)
        {
            int r = animRest.FindNode(compiled.Nodes[i].Name);
            animIndex[i] = anim.FindNode(compiled.Nodes[i].Name);
            if (r < 0 || animIndex[i] < 0)
            {
                log.Add($"{compiled.Nodes[i].Name}: not in the animation, kept at rest");
                continue;
            }

            var f = compiledRest[i] * SmdMath.Inverse(rigRest[r]);
            fixups[i] = f;
            if (f.Translation.Length() > 0.05f)
                log.Add($"{compiled.Nodes[i].Name}: rest positions differ by {f.Translation.Length():0.00} units");
        }

        var baked = new Smd();
        baked.Nodes.AddRange(compiled.Nodes);
        for (int t = 0; t < anim.Frames.Count; t++)
        {
            var animWorld = anim.GetWorldMatrices(t);
            var world = new Matrix4x4[compiled.Nodes.Count];
            var frame = new SmdBonePose[compiled.Nodes.Count];
            for (int i = 0; i < compiled.Nodes.Count; i++)
            {
                // Nodes are parent-first in every SMD studiomdl reads, so the parent's world is known here
                int parent = compiled.Nodes[i].Parent;
                world[i] = fixups[i] is { } f
                    ? f * animWorld[animIndex[i]]
                    : compiled.Frames[0][i].ToMatrix() * (parent >= 0 ? world[parent] : Matrix4x4.Identity);
                frame[i] = SmdMath.ToPose(parent >= 0 ? world[i] * SmdMath.Inverse(world[parent]) : world[i]);
            }

            baked.Frames.Add(frame);
        }

        return baked;
    }

    /// <summary>
    /// How much a pose stretched the mesh: the ratio of every triangle edge's length after to before. A rigid pose
    /// gives ratios of 1; skinning near joints legitimately bends a few edges, so compare against the target's own body.
    /// </summary>
    public static void PrintDistortion(Smd rest, Smd posed)
    {
        var ratios = new List<float>();
        for (int t = 0; t < Math.Min(rest.Triangles.Count, posed.Triangles.Count); t++)
        {
            var a = rest.Triangles[t].Vertices.ToArray();
            var b = posed.Triangles[t].Vertices.ToArray();
            for (int k = 0; k < 3; k++)
            {
                float before = Vector3.Distance(a[k].Position, a[(k + 1) % 3].Position);
                float after = Vector3.Distance(b[k].Position, b[(k + 1) % 3].Position);
                if (before > 1e-3f)
                    ratios.Add(after / before);
            }
        }

        // Where the stretched edges are: by the strongest bone of the edge's first vertex
        var byBone = new Dictionary<string, (int Total, int Stretched)>();
        for (int t = 0; t < Math.Min(rest.Triangles.Count, posed.Triangles.Count); t++)
        {
            var a = rest.Triangles[t].Vertices.ToArray();
            var b = posed.Triangles[t].Vertices.ToArray();
            for (int k = 0; k < 3; k++)
            {
                float before = Vector3.Distance(a[k].Position, a[(k + 1) % 3].Position);
                if (before <= 1e-3f)
                    continue;
                float after = Vector3.Distance(b[k].Position, b[(k + 1) % 3].Position);
                string bone = rest.Nodes[a[k].Weights.MaxBy(l => l.Weight).Bone].Name;
                var entry = byBone.GetValueOrDefault(bone);
                byBone[bone] = (entry.Total + 1, entry.Stretched + (MathF.Abs(after / before - 1f) > 0.25f ? 1 : 0));
            }
        }

        Console.WriteLine("  most stretched (> 25%) edges by bone: " + string.Join(", ", byBone
            .Where(p => p.Value.Stretched > 0)
            .OrderByDescending(p => p.Value.Stretched)
            .Take(8)
            .Select(p => $"{p.Key} {p.Value.Stretched}/{p.Value.Total}")));

        var deviation = ratios.Select(r => MathF.Abs(r - 1f)).Order().ToList();
        Console.WriteLine($"edges {deviation.Count}: mean |stretch| {deviation.Average() * 100:0.0}%, median {deviation[deviation.Count / 2] * 100:0.0}%, " +
            $"p95 {deviation[(int)(deviation.Count * 0.95)] * 100:0.0}%, max {deviation[^1] * 100:0.0}%, " +
            $"edges stretched > 25%: {deviation.Count(d => d > 0.25f) * 100f / deviation.Count:0.0}%");
    }
}
