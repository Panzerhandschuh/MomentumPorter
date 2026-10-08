namespace PlayerModelSpike;

using System.Numerics;
using MomentumPorter.Assets.Models;

/// <summary>
/// Moves a skinned mesh from its own skeleton onto Momentum's (or any compiled model's) skeleton.
/// <list type="bullet">
/// <item><b>rename</b>: keeps the source skeleton, renames mapped bones to the target names and folds every other bone
/// into its nearest mapped ancestor. This is the "mechanical" port: it compiles, but the bones keep the source rig's
/// rest positions and axes, which the target's animations don't expect.</item>
/// <item><b>fit</b>: poses the mesh so each mapped joint sits on the target's joint and each bone points where the
/// target bone points (linear blend skinning with the source weights), then binds it to the target skeleton as is.
/// The result has the target's skeleton exactly, so the target's animations play on it without distortion beyond what
/// the fit itself introduces.</item>
/// </list>
/// </summary>
public static class Retargeter
{
    public sealed class Options
    {
        public string Mode { get; set; } = "fit";

        /// <summary>Uniform scale applied to the source first; null picks the median bone length ratio</summary>
        public float? Scale { get; set; } = 1f;

        public int MaxLinks { get; set; } = 3;
    }

    public sealed record Result(Smd Mesh, List<string> Log);

    public static Result Retarget(Smd source, BoneMap map, StudioModelHeader target, Options options)
    {
        var log = new List<string>();
        var srcWorld = source.GetWorldMatrices(0);

        // Target skeleton as an SMD, built from the compiled model's rest pose, so studiomdl sees exactly these bones
        var targetSmd = new Smd();
        foreach (var bone in target.Bones)
            targetSmd.Nodes.Add(new SmdNode(bone.Index, bone.Name, bone.Parent));
        targetSmd.Frames.Add(target.Bones.Select(b => new SmdBonePose(b.Position, b.Rotation)).ToArray());
        var tgtWorld = targetSmd.GetWorldMatrices(0);
        float eulerError = target.Bones.Max(b => Vector3.Distance(tgtWorld[b.Index].Translation, target.GetBoneToModel(b.Index).Translation));
        log.Add($"target skeleton: {target.Bones.Count} bones (Euler vs quaternion rest pose differ by at most {eulerError:0.0000} units)");

        // source index -> target index for mapped bones
        var mapped = new Dictionary<int, int>();
        foreach (var node in source.Nodes)
        {
            if (!map.Targets.TryGetValue(node.Name, out string? targetName))
                continue;

            int t = target.FindBone(targetName);
            if (t < 0)
                log.Add($"WARNING: {node.Name} maps to {targetName}, which the target doesn't have; dropped");
            else
                mapped[node.Index] = t;
        }

        // Every source bone resolves to itself or its nearest mapped ancestor
        var effective = new int[source.Nodes.Count];
        for (int i = 0; i < source.Nodes.Count; i++)
        {
            int b = i;
            while (b >= 0 && !mapped.ContainsKey(b))
                b = source.Nodes[b].Parent;
            effective[i] = b;
        }

        var weightedBones = source.Triangles.SelectMany(t => t.Vertices).SelectMany(v => v.Weights).Select(l => l.Bone).ToHashSet();
        foreach (int b in weightedBones.Order())
        {
            if (mapped.ContainsKey(b))
                continue;
            string into = effective[b] >= 0 ? $"{source.Nodes[effective[b]].Name} -> {target.Bones[mapped[effective[b]]].Name}" : "NOTHING (unmapped root)";
            log.Add($"folded weighted bone {source.Nodes[b].Name} into {into}");
        }

        foreach (int b in weightedBones.Where(b => mapped.ContainsKey(b)).Order())
            log.Add($"mapped {source.Nodes[b].Name} -> {target.Bones[mapped[b]].Name}");

        var unusedTargets = target.Bones.Where(tb => !mapped.ContainsValue(tb.Index)).Select(tb => tb.Name).ToList();
        log.Add($"target bones with no source bone (left unweighted): {string.Join(", ", unusedTargets)}");

        return options.Mode switch
        {
            "rename" => Rename(source, srcWorld, mapped, effective, target, options, log),
            "fit" => Fit(source, srcWorld, map, mapped, effective, targetSmd, tgtWorld, options, log),
            _ => throw new ArgumentException($"unknown mode {options.Mode}"),
        };
    }

    private static Result Rename(Smd source, Matrix4x4[] srcWorld, Dictionary<int, int> mapped, int[] effective,
        StudioModelHeader target, Options options, List<string> log)
    {
        var kept = mapped.Keys.Order().ToList();
        var newIndex = kept.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);
        var mesh = new Smd();
        var frame = new SmdBonePose[kept.Count];
        foreach (int s in kept)
        {
            int parent = source.Nodes[s].Parent >= 0 ? effective[source.Nodes[s].Parent] : -1;
            int newParent = parent >= 0 ? newIndex[parent] : -1;
            mesh.Nodes.Add(new SmdNode(newIndex[s], target.Bones[mapped[s]].Name, newParent));
            var local = parent >= 0 ? srcWorld[s] * SmdMath.Inverse(srcWorld[parent]) : srcWorld[s];
            frame[newIndex[s]] = SmdMath.ToPose(local);
        }

        mesh.Frames.Add(frame);
        foreach (var triangle in source.Triangles)
        {
            mesh.Triangles.Add(triangle with
            {
                A = Remap(triangle.A, b => effective[b] >= 0 ? newIndex[effective[b]] : 0, options.MaxLinks),
                B = Remap(triangle.B, b => effective[b] >= 0 ? newIndex[effective[b]] : 0, options.MaxLinks),
                C = Remap(triangle.C, b => effective[b] >= 0 ? newIndex[effective[b]] : 0, options.MaxLinks),
            });
        }

        return new Result(mesh, log);
    }

    private static Result Fit(Smd source, Matrix4x4[] srcWorld, BoneMap map, Dictionary<int, int> mapped, int[] effective,
        Smd targetSmd, Matrix4x4[] tgtWorld, Options options, List<string> log)
    {
        // The mapped bone each mapped bone points at
        var aims = new Dictionary<int, int>();
        foreach (int s in mapped.Keys)
        {
            var children = mapped.Keys.Where(c => c != s && source.Nodes[c].Parent >= 0 && effective[source.Nodes[c].Parent] == s).ToList();
            if (map.Aims.TryGetValue(source.Nodes[s].Name, out string? aimName) && aimName == "-")
            {
                // aim=- : keep the bone's direction (it turns with its parent)
            }
            else if (aimName != null)
            {
                int aim = source.FindNode(aimName);
                if (aim >= 0 && mapped.ContainsKey(aim))
                    aims[s] = aim;
                else
                    log.Add($"WARNING: aim {aimName} of {source.Nodes[s].Name} isn't a mapped bone");
            }
            else if (children.Count == 1)
            {
                aims[s] = children[0];
            }
            else if (children.Count > 1)
            {
                log.Add($"WARNING: {source.Nodes[s].Name} has {children.Count} mapped children and no aim=; it won't be rotated");
            }
        }

        // Where each mapped source joint goes: its weight bone's joint, or the place= bone's
        var place = new Dictionary<int, int>();
        foreach (int s in mapped.Keys)
        {
            place[s] = mapped[s];
            if (map.Places.TryGetValue(source.Nodes[s].Name, out string? placeName))
            {
                int p = targetSmd.FindNode(placeName);
                if (p >= 0)
                    place[s] = p;
                else
                    log.Add($"WARNING: place {placeName} of {source.Nodes[s].Name} isn't a target bone");
            }
        }

        // Face the source the way the target faces: turn it about z so its left-right axis (mapped .L bones minus
        // their .R counterparts) lines up with the target's. CS:S/HL2 rest poses face -y, Momentum's faces +x.
        var srcLeft = Vector3.Zero;
        var tgtLeft = Vector3.Zero;
        foreach (int s in mapped.Keys)
        {
            string name = targetSmd.Nodes[place[s]].Name;
            if (!name.EndsWith(".L", StringComparison.Ordinal))
                continue;
            string right = name[..^2] + ".R";
            foreach (int r in mapped.Keys.Where(r => targetSmd.Nodes[place[r]].Name == right))
            {
                srcLeft += srcWorld[s].Translation - srcWorld[r].Translation;
                tgtLeft += tgtWorld[place[s]].Translation - tgtWorld[place[r]].Translation;
            }
        }

        float yaw = MathF.Atan2(tgtLeft.Y, tgtLeft.X) - MathF.Atan2(srcLeft.Y, srcLeft.X);
        if (srcLeft.LengthSquared() < 1e-6f || tgtLeft.LengthSquared() < 1e-6f)
            yaw = 0f;
        var turn = Matrix4x4.CreateRotationZ(yaw);
        log.Add($"source turned {yaw * 180f / MathF.PI:0.#} degrees about z to face the target's way");

        float scale = options.Scale ?? Median(aims.Select(p =>
        {
            float src = Vector3.Distance(srcWorld[p.Value].Translation, srcWorld[p.Key].Translation);
            float tgt = Vector3.Distance(tgtWorld[place[p.Value]].Translation, tgtWorld[place[p.Key]].Translation);
            return src > 1e-3f ? tgt / src : 1f;
        }).ToList());
        log.Add($"source scale: {scale:0.###}");
        var pre = Matrix4x4.CreateScale(scale) * turn;
        Vector3 Joint(int s) => Vector3.Transform(srcWorld[s].Translation, pre);

        log.Add("segment                                         source len  target len  ratio  bend (deg)");
        var rotations = new Dictionary<int, Quaternion>();
        foreach (int s in mapped.Keys.OrderBy(s => Depth(source, s)))
        {
            if (aims.TryGetValue(s, out int c))
            {
                var srcDir = Joint(c) - Joint(s);
                var tgtDir = tgtWorld[place[c]].Translation - tgtWorld[place[s]].Translation;
                rotations[s] = SmdMath.RotationBetween(srcDir, tgtDir);
                float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(srcDir), Vector3.Normalize(tgtDir)), -1f, 1f)) * 180f / MathF.PI;
                log.Add($"  {source.Nodes[s].Name + " -> " + source.Nodes[c].Name,-46} {srcDir.Length(),10:0.00} {tgtDir.Length(),10:0.00} {tgtDir.Length() / srcDir.Length(),6:0.00} {angle,8:0.0}");
            }
            else
            {
                // No child to aim at: turn with the nearest mapped ancestor
                int parent = source.Nodes[s].Parent >= 0 ? effective[source.Nodes[s].Parent] : -1;
                rotations[s] = parent >= 0 ? rotations[parent] : Quaternion.Identity;
            }
        }

        // Deformation of each mapped source bone: scale and face the target's way, turn about its joint, move the
        // joint onto the target's
        var deform = new Dictionary<int, Matrix4x4>();
        var turnNormal = new Dictionary<int, Matrix4x4>();
        foreach (int s in mapped.Keys)
        {
            deform[s] = pre
                * Matrix4x4.CreateTranslation(-Joint(s))
                * Matrix4x4.CreateFromQuaternion(rotations[s])
                * Matrix4x4.CreateTranslation(tgtWorld[place[s]].Translation);
            turnNormal[s] = turn * Matrix4x4.CreateFromQuaternion(rotations[s]);
        }

        var mesh = new Smd();
        mesh.Nodes.AddRange(targetSmd.Nodes);
        mesh.Frames.Add(targetSmd.Frames[0]);
        int orphanVertices = 0;
        foreach (var triangle in source.Triangles)
        {
            var corners = triangle.Vertices.Select(v =>
            {
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                float total = 0f;
                foreach (var link in v.Weights)
                {
                    int s = effective[link.Bone];
                    if (s < 0)
                        continue;
                    position += Vector3.Transform(v.Position, deform[s]) * link.Weight;
                    normal += Vector3.TransformNormal(v.Normal, turnNormal[s]) * link.Weight;
                    total += link.Weight;
                }

                if (total <= 0f)
                {
                    orphanVertices++;
                    return v.Clone();
                }

                var moved = Remap(v, b => effective[b] >= 0 ? mapped[effective[b]] : 0, options.MaxLinks);
                moved.Position = position / total;
                moved.Normal = Vector3.Normalize(normal);
                return moved;
            }).ToArray();
            mesh.Triangles.Add(triangle with { A = corners[0], B = corners[1], C = corners[2] });
        }

        if (orphanVertices > 0)
            log.Add($"WARNING: {orphanVertices} vertices only weighted to unmapped root bones were left in place");
        return new Result(mesh, log);
    }

    /// <summary>Remaps a vertex's links, merging links that land on the same bone and keeping the strongest few</summary>
    private static SmdVertex Remap(SmdVertex v, Func<int, int> remap, int maxLinks)
    {
        var links = v.Weights
            .GroupBy(l => remap(l.Bone))
            .Select(g => new SmdLink(g.Key, g.Sum(l => l.Weight)))
            .OrderByDescending(l => l.Weight)
            .Take(maxLinks)
            .ToList();
        float total = links.Sum(l => l.Weight);
        var result = v.Clone();
        result.Links = links.Select(l => l with { Weight = l.Weight / total }).ToList();
        result.ParentBone = result.Links[0].Bone;
        return result;
    }

    private static int Depth(Smd smd, int bone)
    {
        int depth = 0;
        for (int b = smd.Nodes[bone].Parent; b >= 0; b = smd.Nodes[b].Parent)
            depth++;
        return depth;
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 1f;
        values.Sort();
        return values[values.Count / 2];
    }
}
