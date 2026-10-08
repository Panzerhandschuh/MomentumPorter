using System.Globalization;
using System.Numerics;
using System.Text;
using MomentumPorter.Assets.Models;
using PlayerModelSpike;
using ValvePak;

// Phase 6a spike tooling: see docs/player-models.md for how each command was used.
if (args.Length == 0)
{
    Console.WriteLine("""
        PlayerModelSpike <command> ...
          info <model.mdl>                                   header dump: bones (+ model space positions), hitboxes,
                                                             attachments, body groups, materials, sequences, includes
          extract <pak_dir.vpk> <out dir> <path prefix>...   extract files from a VPK
          decompile <model.mdl> <out.smd>                    reference mesh (LOD 0, first model of each body group)
          skeleton <model.mdl> <out.smd>                     rest pose skeleton (no triangles) as an SMD
          goldsrc <model.mdl> <out dir>                      GoldSrc model -> reference SMD + first frame of a few sequences
          smdbones <smd>                                     SMD skeleton with model space joints, weighted bones
          retarget <src.smd> <bonemap.txt> <target.mdl> <out.smd> [rename|fit] [--scale auto|<f>]
          qc <target.mdl> <mesh.smd> <out.qc> [--cdmaterials dir]... [--modelname player/x.mdl] [--no-definebone] [--static]
                                                             QC for a body fitted onto the target's skeleton
          compare <reference.mdl> <port.mdl>                 skeleton / bind pose / attachment check of a compiled port
          pose <mesh.smd> <anim.smd> <frame> <out.smd>       skins a reference SMD with one frame of an animation SMD
                                                             (the anim's local transforms replace the mesh skeleton's)
          bake-anim <compiled.mdl> <rig-rest.smd> <anim.smd> <out.smd>
                                                             converts a source-rig animation SMD into the compiled
                                                             model's bone frames ($definebone fixups), for pose
          render <out.png> <smd> [<smd>...]                 front + side + 3/4 software render of SMD meshes
          stats <rest.smd> <posed.smd>                       edge length distortion between a mesh and its posed copy
        """);
    return 1;
}

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

switch (args[0])
{
    case "info":
        Info(args[1]);
        break;
    case "extract":
        Extract(args[1], args[2], args[3..]);
        break;
    case "decompile":
    {
        var result = SourceModelDecompiler.Decompile(args[1]);
        result.Log.ForEach(Console.WriteLine);
        CheckWinding(result.Reference);
        result.Reference.Write(args[2]);
        Console.WriteLine($"wrote {args[2]}: {result.Reference.Nodes.Count} bones, {result.Reference.Triangles.Count} triangles, " +
            $"materials: {string.Join(", ", result.Reference.Triangles.Select(t => t.Material).Distinct())}");
        break;
    }
    case "skeleton":
    {
        var header = StudioModelHeader.Read(args[1]);
        var smd = new Smd();
        foreach (var bone in header.Bones)
            smd.Nodes.Add(new SmdNode(bone.Index, bone.Name, bone.Parent));
        smd.Frames.Add(header.Bones.Select(b => new SmdBonePose(b.Position, b.Rotation)).ToArray());
        smd.Write(args[2]);
        break;
    }
    case "goldsrc":
        GoldSrcExport.Export(args[1], args[2]);
        break;
    case "retarget":
    {
        var options = new Retargeter.Options
        {
            Mode = args.Length > 5 && !args[5].StartsWith("--") ? args[5] : "fit",
        };
        int scaleArg = Array.IndexOf(args, "--scale");
        if (scaleArg > 0)
            options.Scale = args[scaleArg + 1] == "auto" ? null : float.Parse(args[scaleArg + 1], CultureInfo.InvariantCulture);
        else
            options.Scale = 1f;

        var source = Smd.Read(args[1]);
        var map = BoneMap.Read(args[2]);
        var target = StudioModelHeader.Read(args[3]);
        var result = Retargeter.Retarget(source, map, target, options);
        result.Log.ForEach(Console.WriteLine);
        result.Mesh.Write(args[4]);
        Console.WriteLine($"wrote {args[4]}: {result.Mesh.Nodes.Count} bones, {result.Mesh.Triangles.Count} triangles");
        break;
    }
    case "pose":
    {
        var mesh = Smd.Read(args[1]);
        var anim = Smd.Read(args[2]);
        var posed = Posing.Pose(mesh, anim, int.Parse(args[3]), out var missing);
        if (missing.Count > 0)
            Console.WriteLine($"bones not in the animation (kept at rest): {string.Join(", ", missing)}");
        posed.Write(args[4]);
        break;
    }
    case "bake-anim":
    {
        var header = StudioModelHeader.Read(args[1]);
        var compiled = new Smd();
        foreach (var bone in header.Bones)
            compiled.Nodes.Add(new SmdNode(bone.Index, bone.Name, bone.Parent));
        compiled.Frames.Add(header.Bones.Select(b => new SmdBonePose(b.Position, b.Rotation)).ToArray());
        var log = new List<string>();
        var baked = Posing.BakeToCompiled(compiled, Smd.Read(args[2]), Smd.Read(args[3]), log);
        log.ForEach(Console.WriteLine);
        baked.Write(args[4]);
        break;
    }
    case "qc":
    {
        // qc <target.mdl> <mesh.smd (name as the QC should reference it)> <out.qc> [--cdmaterials dir]... [--no-definebone]
        //    [--static] [--modelname player/x.mdl]
        var options = new QcWriter.Options();
        for (int i = 4; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--cdmaterials": options.CdMaterials.Add(args[++i]); break;
                case "--no-definebone": options.DefineBones = false; break;
                case "--static": options.IncludeAnimations = false; break;
                case "--modelname": options.ModelName = args[++i]; break;
            }
        }

        File.WriteAllText(args[3], QcWriter.Write(StudioModelHeader.Read(args[1]), args[2], options));
        break;
    }
    case "smdbones":
    {
        var smd = Smd.Read(args[1]);
        var world = smd.GetWorldMatrices(0);
        var weighted = smd.Triangles.SelectMany(t => t.Vertices).SelectMany(v => v.Weights).Select(l => l.Bone).ToHashSet();
        foreach (var node in smd.Nodes)
            Console.WriteLine($"{node.Index,3} {node.Name,-28} {(node.Parent >= 0 ? smd.Nodes[node.Parent].Name : "-"),-28} {V(world[node.Index].Translation)}{(weighted.Contains(node.Index) ? " weighted" : "")}");
        break;
    }
    case "compare":
        Compare(StudioModelHeader.Read(args[1]), StudioModelHeader.Read(args[2]));
        break;
    case "render":
        Renderer.Render(args[1], args[2..].Select(Smd.Read).ToList(), args[2..].Select(Path.GetFileNameWithoutExtension).ToList()!);
        break;
    case "stats":
        Posing.PrintDistortion(Smd.Read(args[1]), Smd.Read(args[2]));
        break;
    default:
        Console.Error.WriteLine($"unknown command {args[0]}");
        return 1;
}

return 0;

static void Info(string path)
{
    var h = StudioModelHeader.Read(path);
    Console.WriteLine($"name {h.Name} version {h.Version} checksum {h.Checksum:X8} flags 0x{h.Flags:X}");
    Console.WriteLine($"eye {V(h.EyePosition)} hull {V(h.HullMin)} .. {V(h.HullMax)} view {V(h.ViewMin)} .. {V(h.ViewMax)}");
    Console.WriteLine($"surfaceprop {h.SurfaceProp} mass {h.Mass}");
    Console.WriteLine($"includemodels: {string.Join(", ", h.IncludeModels)}");
    Console.WriteLine($"cdmaterials: {string.Join(", ", h.TextureDirectories)}; materials: {string.Join(", ", h.Textures)}; skin families {h.SkinFamilies.Count}");
    Console.WriteLine($"{h.Bones.Count} bones (index name parent | local pos | model space pos | flags):");
    foreach (var b in h.Bones)
    {
        var world = h.GetBoneToModel(b.Index).Translation;
        Console.WriteLine($"  {b.Index,3} {b.Name,-34} {(b.Parent >= 0 ? h.Bones[b.Parent].Name : "-"),-34} {V(b.Position)} | {V(world)} | 0x{b.Flags:X}");
    }

    foreach (var set in h.HitboxSets)
    {
        Console.WriteLine($"hitbox set {set.Name}: {set.Hitboxes.Count}");
        foreach (var hb in set.Hitboxes)
            Console.WriteLine($"  bone {h.Bones[hb.Bone].Name} group {hb.Group} {V(hb.Min)} {V(hb.Max)} {hb.Name}");
    }

    Console.WriteLine($"attachments: {h.Attachments.Count}");
    foreach (var a in h.Attachments)
        Console.WriteLine($"  {a.Name} on {h.Bones[a.Bone].Name} at ({a.Local[3]:0.##}, {a.Local[7]:0.##}, {a.Local[11]:0.##})");

    Console.WriteLine($"body parts: {h.BodyParts.Count}");
    foreach (var p in h.BodyParts)
        Console.WriteLine($"  {p.Name}: {string.Join(", ", p.Models.Select(m => $"{m.Name} ({m.VertexCount} verts, {m.Meshes.Count} meshes)"))}");

    Console.WriteLine($"pose parameters: {string.Join(", ", h.PoseParameters.Select(p => $"{p.Name} [{p.Start}, {p.End}]"))}");
    Console.WriteLine($"local animations {h.LocalAnimationCount}, sequences {h.Sequences.Count}:");
    foreach (var s in h.Sequences)
        Console.WriteLine($"  {s.Label} {s.Activity} flags 0x{s.Flags:X} blends {s.BlendCount}");
    if (h.KeyValues.Length > 0)
        Console.WriteLine($"keyvalues: {h.KeyValues}");
}

static void Extract(string vpkPath, string outDir, string[] prefixes)
{
    using var package = new Package();
    package.Read(vpkPath);
    int count = 0;
    foreach (var (extension, entries) in package.Entries!)
    {
        foreach (var entry in entries)
        {
            string full = entry.GetFullPath();
            if (!prefixes.Any(p => full.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            package.ReadEntry(entry, out byte[] data);
            string target = Path.Combine(outDir, full);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, data);
            count++;
        }
    }

    Console.WriteLine($"extracted {count} files");
}

// Does a compiled port have the reference model's skeleton (what the included animations are built for)?
static void Compare(StudioModelHeader reference, StudioModelHeader port)
{
    Console.WriteLine($"bones: reference {reference.Bones.Count}, port {port.Bones.Count}");
    float maxPos = 0f, maxAngle = 0f, maxBind = 0f;
    int problems = 0;
    foreach (var rb in reference.Bones)
    {
        int pi = port.FindBone(rb.Name);
        if (pi < 0)
        {
            Console.WriteLine($"  missing in port: {rb.Name}");
            problems++;
            continue;
        }

        var pb = port.Bones[pi];
        string rp = rb.Parent >= 0 ? reference.Bones[rb.Parent].Name : "";
        string pp = pb.Parent >= 0 ? port.Bones[pb.Parent].Name : "";
        if (rp != pp)
        {
            Console.WriteLine($"  {rb.Name}: parent {pp}, reference has {rp}");
            problems++;
        }

        maxPos = MathF.Max(maxPos, Vector3.Distance(rb.Position, pb.Position));
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rb.Quaternion), Quaternion.Normalize(pb.Quaternion)));
        maxAngle = MathF.Max(maxAngle, 2f * MathF.Acos(MathF.Min(1f, dot)) * 180f / MathF.PI);
        for (int k = 0; k < 12; k++)
            maxBind = MathF.Max(maxBind, MathF.Abs(rb.PoseToBone[k] - pb.PoseToBone[k]));
    }

    foreach (var pb in port.Bones.Where(b => reference.FindBone(b.Name) < 0))
        Console.WriteLine($"  extra in port: {pb.Name}");
    Console.WriteLine($"rest pose: max position difference {maxPos:0.0000}, max rotation difference {maxAngle:0.000} deg, max poseToBone difference {maxBind:0.0000}");
    Console.WriteLine($"parent/missing problems: {problems}");
    Console.WriteLine($"includemodels: reference [{string.Join(", ", reference.IncludeModels)}], port [{string.Join(", ", port.IncludeModels)}]");

    foreach (var ra in reference.Attachments)
    {
        var pa = port.Attachments.FirstOrDefault(a => a.Name == ra.Name);
        if (pa == null)
        {
            Console.WriteLine($"  attachment {ra.Name} missing in port");
            continue;
        }

        float diff = Enumerable.Range(0, 12).Max(k => MathF.Abs(ra.Local[k] - pa.Local[k]));
        Console.WriteLine($"  attachment {ra.Name}: bone {port.Bones[pa.Bone].Name} (reference {reference.Bones[ra.Bone].Name}), max matrix difference {diff:0.0000}");
    }

    Console.WriteLine($"hitboxes: reference {reference.HitboxSets.Sum(s => s.Hitboxes.Count)}, port {port.HitboxSets.Sum(s => s.Hitboxes.Count)}");
    Console.WriteLine($"bone flags differing: {string.Join(", ", reference.Bones.Where(rb => port.FindBone(rb.Name) is int i && i >= 0 && port.Bones[i].Flags != rb.Flags).Select(rb => $"{rb.Name} 0x{rb.Flags:X}->0x{port.Bones[port.FindBone(rb.Name)].Flags:X}"))}");
}

static void CheckWinding(Smd smd)
{
    // The face normal should agree with the vertex normals if the winding is the SMD one
    int agree = 0, total = 0;
    foreach (var t in smd.Triangles.Take(2000))
    {
        var n = Vector3.Cross(t.B.Position - t.A.Position, t.C.Position - t.A.Position);
        if (n.LengthSquared() < 1e-8f)
            continue;
        total++;
        if (Vector3.Dot(n, t.A.Normal + t.B.Normal + t.C.Normal) > 0)
            agree++;
    }

    Console.WriteLine($"winding check: {agree}/{total} triangles face along their vertex normals");
}

static string V(Vector3 v) => $"({v.X:0.##}, {v.Y:0.##}, {v.Z:0.##})";
