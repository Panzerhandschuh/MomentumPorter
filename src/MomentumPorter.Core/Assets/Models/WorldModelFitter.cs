namespace MomentumPorter.Assets.Models;

using System.Numerics;

/// <summary>
/// How a weapon model sits on the bone that holds it, in that bone's frame: the barrel's direction, the direction of
/// the weapon's top (perpendicular unit vectors) and where its trigger is.
/// </summary>
public sealed record GunFrame(Vector3 Forward, Vector3 Up, Vector3? Trigger, string From)
{
    public override string ToString() => $"barrel {Describe(Forward)}, top {Describe(Up)} (from {From})";

    private static string Describe(Vector3 v)
    {
        Vector3 a = Vector3.Abs(v);
        (string name, float along) = a.X >= a.Y && a.X >= a.Z ? (v.X > 0 ? "+x" : "-x", a.X) : a.Y >= a.Z ? (v.Y > 0 ? "+y" : "-y", a.Y) : (v.Z > 0 ? "+z" : "-z", a.Z);
        float off = MathF.Acos(Math.Clamp(along, -1f, 1f)) * 180 / MathF.PI;
        return off < 0.5f ? name : $"{name} {off:0} degrees off";
    }
}

/// <summary>How a world model was fitted, for the log.</summary>
public sealed record WorldModelFit(string? GripBone, GunFrame Source, GunFrame Target, bool TriggersAligned)
{
    public override string ToString() =>
        $"held at {GripBone ?? "the model's origin"} with its {Source}; placed like Momentum's with its {Target}"
        + (TriggersAligned ? ", triggers lined up" : ", no trigger to line up");
}

/// <summary>
/// Makes another game's world (third person) weapon model attach to Momentum's player.
/// </summary>
/// <remarks>
/// <para>
/// Held weapons are bone-merged onto the player model, which matches bones by name. Momentum's world weapons hang
/// from the player's <c>weapon_bone.R</c>; other games' world models are rigged to their own player skeletons (CS:GO's
/// carry its whole skeleton and hold the gun on <c>weapon_hand_R</c>), share no bone with Momentum's player, and so
/// stay at the player's feet. This re-expresses the mesh relative to the bone the source game holds it by, turns and
/// moves it to sit on <c>weapon_bone.R</c> the way the Momentum model it replaces does, and compiles it on that one bone.
/// </para>
/// <para>
/// Each Momentum weapon has its own hold animation, so how its gun sits on <c>weapon_bone.R</c> differs (the pistol's
/// barrel is 16 degrees off the bone's -z, the machine gun's 20). What they share is model space: every Momentum world
/// model is laid out level with the barrel along -y and the top along +z, so the target frame is those two directions
/// taken into <c>weapon_bone.R</c>'s frame. The source game's frame comes from its model's landmarks.
/// </para>
/// </remarks>
public static class WorldModelFitter
{
    public const string MomentumGripBone = "weapon_bone.R";

    /// <summary>Momentum's world models are laid out level with the barrel along -y and the top along +z.</summary>
    public static readonly Vector3 MomentumModelForward = -Vector3.UnitY;
    public static readonly Vector3 MomentumModelUp = Vector3.UnitZ;

    /// <summary>
    /// Momentum's pistol (w_mom_pistol.mdl, whose weapon_bone.R rests turned -1.283 radians about x), used when the
    /// replaced model can't be read.
    /// </summary>
    public static readonly GunFrame DefaultTarget = new(
        new Vector3(0, -0.2838f, -0.9589f), new Vector3(0, -0.9589f, 0.2838f), new Vector3(0, 0.51f, -1.73f), "Momentum's pistol");

    /// <summary>Quake 3 holds weapons at their origin with the barrel along +x and the top along +z.</summary>
    public static readonly GunFrame Quake3Source = new(Vector3.UnitX, Vector3.UnitZ, null, "Quake 3's convention");

    /// <summary>GoldSrc's p_ models' grip bone, Bip01 R Hand, as GoldSrcWeaponConverter names it.</summary>
    public const string GoldSrcGripBone = "bip01_r_hand";

    /// <summary>
    /// GoldSrc's p_ models (CS 1.6's and Half-Life's) hold weapons on Bip01 R Hand with the barrel along +x and the top
    /// along +z; knives and crowbars point along +z. Few have landmarks to measure this from, and their grip and
    /// magazine are too small to tell up from down. The bone is at the wrist, so the trigger is about 4.5 units ahead
    /// of it and 1 above (measured on CS 1.6's USP, AK-47 and M249 and Half-Life's 357, 9mm handgun, MP5 and shotgun).
    /// </summary>
    public static readonly GunFrame GoldSrcSource = new(Vector3.UnitX, Vector3.UnitZ, new Vector3(4.5f, 0, 1), "GoldSrc's Bip01 R Hand");

    /// <summary>
    /// The most a measured barrel tilt is trusted; more means the measurement found something other than the barrel.
    /// </summary>
    private const float MaxTilt = 15 * MathF.PI / 180;

    /// <summary>Bones other games' world models are held by, most specific first.</summary>
    private static readonly string[] GripBones =
    [
        MomentumGripBone,
        "weapon_hand_R",           // CS:GO
        "ValveBiped.weapon_bone",  // CS:S, HL2
        "weapon_bone",             // TF2
        "ValveBiped.Bip01_R_Hand",
        GoldSrcGripBone,
    ];

    /// <summary>The bone the model is held by and the transform from model space into its frame.</summary>
    public static (string? Bone, Matrix4x4 ModelToGrip) FindGrip(StudioModelHeader header)
    {
        string? name = GripBones.FirstOrDefault(x => header.FindBone(x) >= 0);
        Matrix4x4 gripToModel = name is null ? Matrix4x4.Identity : header.GetBoneToModel(header.FindBone(name));
        if (!Matrix4x4.Invert(gripToModel, out Matrix4x4 modelToGrip))
            throw new InvalidDataException($"{header.Name}'s grip bone can't be inverted");

        return (name, modelToGrip);
    }

    /// <summary>
    /// How a Momentum world model's gun sits on its <see cref="MomentumGripBone"/>: model space's barrel and top
    /// directions in the bone's frame. Null if it has no such bone.
    /// </summary>
    public static GunFrame? MeasureMomentum(SourceModelMesh mesh)
    {
        int bone = mesh.Header.FindBone(MomentumGripBone);
        if (bone < 0 || !Matrix4x4.Invert(mesh.Header.GetBoneToModel(bone), out Matrix4x4 modelToBone))
            return null;

        Vector3 forward = Vector3.Normalize(Vector3.TransformNormal(MomentumModelForward, modelToBone));
        Vector3 up = Vector3.TransformNormal(MomentumModelUp, modelToBone);
        up = Vector3.Normalize(up - Vector3.Dot(up, forward) * forward);
        return new GunFrame(forward, up, FindPoint(mesh.Header, modelToBone, "trigger"), "Momentum's model space");
    }

    /// <summary>
    /// Works out how a model's gun sits on its grip bone from its landmarks. The barrel is along the mesh's longest
    /// side, towards the muzzle. The top is towards the shell ejection or the slide, else away from the side the grip and
    /// magazine hang from the barrel. The barrel's angle off that side comes from the line from the shell ejection to the
    /// muzzle (both on the barrel), else from the weapon's top line.
    /// </summary>
    public static GunFrame Measure(SourceModelMesh mesh, Matrix4x4 modelToGrip)
    {
        StudioModelHeader header = mesh.Header;
        List<Vector3> points = Points(mesh, modelToGrip);
        (Vector3 min, Vector3 max) = Bounds(points);
        var from = new List<string>();

        // The barrel: the longest side, towards the muzzle. (A muzzle attachment can sit well inside the gun, where its
        // effects start, so it only decides which end is the front.)
        Vector3? muzzle = FindPoint(header, modelToGrip, "muzzle");
        Vector3 axis = LongestAxis(min, max, exclude: null, signed: false);
        float muzzleAlong = muzzle is { } mz ? Vector3.Dot(mz, axis) : 0;
        Vector3 forward;
        if (MathF.Abs(muzzleAlong) > 0.5f)
        {
            forward = muzzleAlong > 0 ? axis : -axis;
            from.Add("the muzzle");
        }
        else
        {
            forward = Vector3.Dot(max, axis) >= -Vector3.Dot(min, axis) ? axis : -axis;
            from.Add("the far end");
        }

        // The top
        Vector3 up = Vector3.Zero;
        Vector3? eject = FindPoint(header, modelToGrip, "shell") ?? FindPoint(header, modelToGrip, "eject");
        foreach ((string what, Vector3? point) in new[] { ("the shell ejection", eject), ("the slide", FindPoint(header, modelToGrip, "slide")) })
        {
            if (point is not { } p)
                continue;

            Vector3 side = p - Vector3.Dot(p, forward) * forward;
            if (side.Length() > 0.05f)
            {
                up = Snap(side, exclude: forward);
                from.Add(what);
                break;
            }
        }

        // The barrel's offset from the grip bone gives the vertical axis, and the grip and magazine, which hang much
        // further below the barrel than sights rise above it, give which way is down. (The barrel isn't always above
        // the grip bone: CS:S puts ValveBiped.weapon_bone on top of the receiver.)
        if (up == Vector3.Zero && muzzle is { } barrel)
        {
            Vector3 side = barrel - Vector3.Dot(barrel, forward) * forward;
            if (side.Length() > 0.05f)
            {
                Vector3 vertical = Vector3.Abs(Snap(side, exclude: forward));
                float line = Vector3.Dot(barrel, vertical);
                float below = line - Vector3.Dot(min, vertical);
                float above = Vector3.Dot(max, vertical) - line;
                up = above <= below ? vertical : -vertical;
                from.Add("the side the grip hangs from");
            }
        }

        if (up == Vector3.Zero)
        {
            up = LongestAxis(min, max, exclude: forward, signed: true);
            from.Add("a guess from the mesh's extent");
        }

        // The barrel's angle off the axis it was snapped to
        float tilt;
        if (muzzle is { } m && eject is { } e && Vector3.Dot(m - e, forward) > 3)
        {
            Vector3 d = m - e;
            tilt = MathF.Atan2(Vector3.Dot(d, up), Vector3.Dot(d, forward));
            from.Add("its shell ejection to muzzle line");
        }
        else
        {
            tilt = TopLineTilt(points, forward, up);
            from.Add("its top line");
        }

        if (MathF.Abs(tilt) > MaxTilt)
            tilt = 0;

        (forward, up) = (MathF.Cos(tilt) * forward + MathF.Sin(tilt) * up, MathF.Cos(tilt) * up - MathF.Sin(tilt) * forward);
        return new GunFrame(forward, up, FindPoint(header, modelToGrip, "trigger"), string.Join(", ", from));
    }

    /// <summary>The same frame with its barrel's angle measured from the mesh, for frames known up to their tilt.</summary>
    public static GunFrame WithMeasuredTilt(GunFrame frame, SourceModelMesh mesh, Matrix4x4 modelToGrip)
    {
        float tilt = TopLineTilt(Points(mesh, modelToGrip), frame.Forward, frame.Up);
        if (MathF.Abs(tilt) > MaxTilt)
            tilt = 0;

        return frame with
        {
            Forward = MathF.Cos(tilt) * frame.Forward + MathF.Sin(tilt) * frame.Up,
            Up = MathF.Cos(tilt) * frame.Up - MathF.Sin(tilt) * frame.Forward,
        };
    }

    /// <summary>
    /// Turns a direction from the source's grip frame to the target's. The barrel, top and left (top cross barrel) are
    /// a right handed basis on both sides, so this is a rotation and triangle windings stay as they are.
    /// </summary>
    public static Vector3 Transfer(Vector3 v, GunFrame source, GunFrame target)
    {
        Vector3 sourceLeft = Vector3.Cross(source.Up, source.Forward);
        Vector3 targetLeft = Vector3.Cross(target.Up, target.Forward);
        return Vector3.Dot(v, source.Forward) * target.Forward + Vector3.Dot(v, source.Up) * target.Up + Vector3.Dot(v, sourceLeft) * targetLeft;
    }

    /// <summary>
    /// The mesh as triangles on bone 0, in the target's grip frame.
    /// </summary>
    /// <param name="sourceFrame">How the source sits on its grip, up to the barrel's tilt; measured from its landmarks when null</param>
    public static (IReadOnlyList<SmdTriangle> Triangles, WorldModelFit Fit) Fit(SourceModelMesh mesh, GunFrame? sourceFrame, GunFrame target)
    {
        (string? grip, Matrix4x4 modelToGrip) = FindGrip(mesh.Header);
        GunFrame source = sourceFrame is null ? Measure(mesh, modelToGrip) : WithMeasuredTilt(sourceFrame, mesh, modelToGrip);

        // Line the triggers up: every grip puts the index finger on it, wherever the two games put their grip bones
        bool aligned = source.Trigger is not null && target.Trigger is not null;
        Vector3 offset = aligned ? target.Trigger!.Value - Transfer(source.Trigger!.Value, source, target) : Vector3.Zero;

        SmdVertex Convert(SourceVertex v) => new(
            0,
            Transfer(Vector3.Transform(v.Position, modelToGrip), source, target) + offset,
            Vector3.Normalize(Transfer(Vector3.TransformNormal(v.Normal, modelToGrip), source, target)),
            v.TexCoord);

        var triangles = mesh.Triangles.Select(x => new SmdTriangle(x.Material, Convert(x.A), Convert(x.B), Convert(x.C))).ToList();
        return (triangles, new WorldModelFit(grip, source, target, aligned));
    }

    /// <summary>
    /// Fits the mesh to sit like <paramref name="replaces"/> and compiles it as <paramref name="modelName"/> (a path under
    /// models/).
    /// </summary>
    /// <param name="replaces">
    /// The Momentum world model this one replaces, or null to sit like Momentum's pistol. Its frame on
    /// <see cref="MomentumGripBone"/> is matched, and its rest pose copied so the model also lies the same way where it's
    /// drawn on its own (weapon spawners) rather than bone-merged onto the player.
    /// </param>
    public static async Task<(CompiledModel Model, WorldModelFit Fit)> CompileAsync(
        SourceModelMesh mesh, GunFrame? sourceFrame, SourceModelMesh? replaces, StudioMdlCompiler compiler, string modelName, CancellationToken cancellation)
    {
        int replacedBone = replaces?.Header.FindBone(MomentumGripBone) ?? -1;
        GunFrame target = (replaces is null ? null : MeasureMomentum(replaces)) ?? DefaultTarget;

        (IReadOnlyList<SmdTriangle> triangles, WorldModelFit fit) = Fit(mesh, sourceFrame, target);
        string folder = compiler.CreateSourceFolder(modelName);

        // Without the replaced model, rest like the pistol: the target's frame laid out level in model space
        Matrix4x4 restToModel = replacedBone >= 0
            ? replaces!.Header.GetBoneToModel(replacedBone)
            : Matrix4x4.CreateRotationX(-1.283012f);
        var rest = new SmdBonePose(restToModel.Translation, ToEuler(restToModel));
        SmdVertex Place(SmdVertex v) => v with
        {
            Position = Vector3.Transform(v.Position, restToModel),
            Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, restToModel)),
        };
        triangles = triangles.Select(x => x with { A = Place(x.A), B = Place(x.B), C = Place(x.C) }).ToList();

        var bones = new List<SmdBone> { new(MomentumGripBone, -1) };
        var pose = new List<SmdBonePose> { rest };
        await File.WriteAllTextAsync(Path.Combine(folder, "reference.smd"), SmdWriter.WriteReference(bones, pose, triangles), cancellation).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, "idle.smd"), SmdWriter.WriteAnimation(bones, [pose]), cancellation).ConfigureAwait(false);

        // The materials stay where the source game has them; Momentum mounts the game
        IReadOnlyList<string> directories = mesh.Header.TextureDirectories.Count > 0 ? mesh.Header.TextureDirectories : [""];
        var qc = new QcWriter(modelName, Normalize(directories[0]));
        foreach (string directory in directories.Skip(1))
            qc.Line($"$cdmaterials \"{Normalize(directory)}/\"");

        // The vertices are already in game space; studiomdl would otherwise turn them 90 degrees about z
        qc.Origin(0, 0, 0, -90)
            .SurfaceProp(string.IsNullOrEmpty(mesh.Header.SurfaceProp) ? "weapon" : mesh.Header.SurfaceProp)
            .Body("weapon", "reference.smd")
            .Sequence(new QcSequence("idle", "idle.smd", 30, Loop: true));

        string qcPath = Path.Combine(folder, "model.qc");
        await File.WriteAllTextAsync(qcPath, qc.ToString(), cancellation).ConfigureAwait(false);
        CompiledModel model = await compiler.CompileAsync(qcPath, modelName, cancellation).ConfigureAwait(false);
        return (model, fit);
    }

    private static string Normalize(string directory) => directory.Replace('\\', '/').Trim('/');

    private static List<Vector3> Points(SourceModelMesh mesh, Matrix4x4 modelToGrip)
    {
        var points = mesh.Triangles.SelectMany(x => new[] { x.A, x.B, x.C }).Select(x => Vector3.Transform(x.Position, modelToGrip)).ToList();
        if (points.Count == 0)
            throw new InvalidDataException($"{mesh.Header.Name} has no triangles");

        return points;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyList<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }

    /// <summary>
    /// The angle of the weapon's top line (slide, receiver, tube) from <paramref name="forward"/> towards
    /// <paramref name="up"/>: a line fitted through the highest points, which run along the barrel.
    /// </summary>
    private static float TopLineTilt(IReadOnlyList<Vector3> points, Vector3 forward, Vector3 up)
    {
        float highest = points.Max(x => Vector3.Dot(x, up));
        float lowest = points.Min(x => Vector3.Dot(x, up));
        float threshold = highest - 0.15f * (highest - lowest);

        double n = 0, sf = 0, su = 0, sff = 0, sfu = 0;
        foreach (Vector3 p in points)
        {
            float u = Vector3.Dot(p, up);
            if (u < threshold)
                continue;

            float f = Vector3.Dot(p, forward);
            n++;
            sf += f;
            su += u;
            sff += f * f;
            sfu += f * u;
        }

        double denominator = n * sff - sf * sf;
        if (n < 3 || Math.Abs(denominator) < 1e-6)
            return 0;

        return (float)Math.Atan((n * sfu - sf * su) / denominator);
    }

    /// <summary>
    /// Where an attachment, or failing that a bone, whose name contains <paramref name="name"/> is, in the grip's frame.
    /// </summary>
    private static Vector3? FindPoint(StudioModelHeader header, Matrix4x4 modelToGrip, string name)
    {
        StudioAttachment? attachment = header.Attachments.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (attachment is not null && attachment.Bone >= 0 && attachment.Bone < header.Bones.Count)
        {
            var local = new Vector3(attachment.Local[3], attachment.Local[7], attachment.Local[11]);
            return Vector3.Transform(Vector3.Transform(local, header.GetBoneToModel(attachment.Bone)), modelToGrip);
        }

        StudioBone? bone = header.Bones.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return bone is null ? null : Vector3.Transform(header.GetBoneToModel(bone.Index).Translation, modelToGrip);
    }

    /// <summary>The axis (with sign) <paramref name="v"/> points along the most, other than <paramref name="exclude"/>.</summary>
    private static Vector3 Snap(Vector3 v, Vector3? exclude)
    {
        Vector3 best = Vector3.UnitX;
        float bestLength = float.MinValue;
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            if (exclude is { } e && MathF.Abs(Vector3.Dot(e, axis)) > 0.5f)
                continue;

            float d = Vector3.Dot(v, axis);
            if (MathF.Abs(d) > bestLength)
            {
                bestLength = MathF.Abs(d);
                best = d >= 0 ? axis : -axis;
            }
        }

        return best;
    }

    /// <summary>
    /// The axis the bounds are longest along (other than <paramref name="exclude"/>); with <paramref name="signed"/>,
    /// towards its far end.
    /// </summary>
    private static Vector3 LongestAxis(Vector3 min, Vector3 max, Vector3? exclude, bool signed)
    {
        Vector3 best = Vector3.UnitX;
        float bestSpan = float.MinValue;
        foreach ((Vector3 axis, float lo, float hi) in new[] { (Vector3.UnitX, min.X, max.X), (Vector3.UnitY, min.Y, max.Y), (Vector3.UnitZ, min.Z, max.Z) })
        {
            if (exclude is { } e && MathF.Abs(Vector3.Dot(e, axis)) > 0.5f)
                continue;

            if (hi - lo > bestSpan)
            {
                bestSpan = hi - lo;
                best = !signed || MathF.Abs(hi) >= MathF.Abs(lo) ? axis : -axis;
            }
        }

        return best;
    }

    /// <summary>A rotation's Euler angles in radians (x, then y, then z), as SMD skeletons write them.</summary>
    internal static Vector3 ToEuler(Matrix4x4 m)
    {
        // SMD angles mean R = Rz * Ry * Rx for column vectors; System.Numerics matrices are row-vector, so m = R transposed
        float sy = -m.M13;
        float y = MathF.Asin(Math.Clamp(sy, -1f, 1f));
        if (MathF.Abs(sy) < 0.9999f)
            return new Vector3(MathF.Atan2(m.M23, m.M33), y, MathF.Atan2(m.M12, m.M11));

        return new Vector3(MathF.Atan2(-m.M32, m.M22), y, 0);
    }
}
