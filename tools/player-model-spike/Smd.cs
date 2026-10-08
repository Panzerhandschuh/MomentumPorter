namespace PlayerModelSpike;

using System.Globalization;
using System.Numerics;
using System.Text;

public sealed record SmdNode(int Index, string Name, int Parent);

/// <summary>A bone's transform relative to its parent: position and Euler angles in radians, as SMD writes them</summary>
public record struct SmdBonePose(Vector3 Position, Vector3 Rotation)
{
    public Matrix4x4 ToMatrix() => SmdMath.FromPose(Position, Rotation);
}

public record struct SmdLink(int Bone, float Weight);

public sealed class SmdVertex
{
    public int ParentBone;
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
    public List<SmdLink> Links = [];

    public SmdVertex Clone() => new()
    {
        ParentBone = ParentBone,
        Position = Position,
        Normal = Normal,
        TexCoord = TexCoord,
        Links = [.. Links],
    };

    /// <summary>The weights to use: the links, or the parent bone if there are none</summary>
    public IEnumerable<SmdLink> Weights => Links.Count > 0 ? Links : [new SmdLink(ParentBone, 1f)];
}

public sealed record SmdTriangle(string Material, SmdVertex A, SmdVertex B, SmdVertex C)
{
    public IEnumerable<SmdVertex> Vertices => [A, B, C];
}

/// <summary>
/// A Valve SMD (version 1): skeleton nodes, one or more frames of bone poses, and optionally triangles in model space.
/// </summary>
public sealed class Smd
{
    public List<SmdNode> Nodes { get; } = [];
    public List<SmdBonePose[]> Frames { get; } = [];
    public List<SmdTriangle> Triangles { get; } = [];

    public int FindNode(string name) => Nodes.FindIndex(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Model space transform of every bone in a frame</summary>
    public Matrix4x4[] GetWorldMatrices(int frame = 0)
    {
        var world = new Matrix4x4[Nodes.Count];
        var done = new bool[Nodes.Count];
        for (int i = 0; i < Nodes.Count; i++)
            Resolve(i);
        return world;

        void Resolve(int i)
        {
            if (done[i])
                return;

            var local = Frames[frame][i].ToMatrix();
            int parent = Nodes[i].Parent;
            if (parent >= 0)
            {
                Resolve(parent);
                world[i] = local * world[parent];
            }
            else
            {
                world[i] = local;
            }

            done[i] = true;
        }
    }

    public static Smd Read(string path)
    {
        var smd = new Smd();
        using var reader = new StreamReader(path);
        string? line;
        string section = "";
        SmdBonePose[]? frame = null;
        string? material = null;
        var pending = new List<SmdVertex>(3);

        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//"))
                continue;

            if (section == "")
            {
                if (trimmed is "nodes" or "skeleton" or "triangles" or "vertexanimation")
                    section = trimmed;
                continue;
            }

            if (trimmed == "end")
            {
                section = "";
                continue;
            }

            switch (section)
            {
                case "nodes":
                {
                    int q1 = trimmed.IndexOf('"'), q2 = trimmed.LastIndexOf('"');
                    int index = int.Parse(trimmed[..q1].Trim(), CultureInfo.InvariantCulture);
                    string name = trimmed[(q1 + 1)..q2];
                    int parent = int.Parse(trimmed[(q2 + 1)..].Trim(), CultureInfo.InvariantCulture);
                    if (index != smd.Nodes.Count)
                        throw new InvalidDataException($"Node {index} is out of order in {path}");
                    smd.Nodes.Add(new SmdNode(index, name, parent));
                    break;
                }
                case "skeleton":
                {
                    if (trimmed.StartsWith("time"))
                    {
                        frame = new SmdBonePose[smd.Nodes.Count];
                        // Bones missing from a frame keep their previous pose
                        if (smd.Frames.Count > 0)
                            Array.Copy(smd.Frames[^1], frame, frame.Length);
                        smd.Frames.Add(frame);
                        continue;
                    }

                    var f = Floats(trimmed);
                    frame![(int)f[0]] = new SmdBonePose(new Vector3(f[1], f[2], f[3]), new Vector3(f[4], f[5], f[6]));
                    break;
                }
                case "triangles":
                {
                    if (material == null)
                    {
                        material = trimmed;
                        continue;
                    }

                    var f = Floats(trimmed);
                    var vertex = new SmdVertex
                    {
                        ParentBone = (int)f[0],
                        Position = new Vector3(f[1], f[2], f[3]),
                        Normal = new Vector3(f[4], f[5], f[6]),
                        TexCoord = new Vector2(f[7], f[8]),
                    };
                    if (f.Length > 9)
                    {
                        int links = (int)f[9];
                        for (int k = 0; k < links; k++)
                            vertex.Links.Add(new SmdLink((int)f[10 + k * 2], f[11 + k * 2]));
                    }

                    pending.Add(vertex);
                    if (pending.Count == 3)
                    {
                        smd.Triangles.Add(new SmdTriangle(material, pending[0], pending[1], pending[2]));
                        pending.Clear();
                        material = null;
                    }

                    break;
                }
            }
        }

        return smd;
    }

    public void Write(string path)
    {
        var sb = new StringBuilder();
        sb.Append("version 1\nnodes\n");
        foreach (var node in Nodes)
            sb.Append(CultureInfo.InvariantCulture, $"{node.Index} \"{node.Name}\" {node.Parent}\n");
        sb.Append("end\nskeleton\n");
        for (int t = 0; t < Frames.Count; t++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"time {t}\n");
            for (int i = 0; i < Nodes.Count; i++)
            {
                var p = Frames[t][i];
                sb.Append(CultureInfo.InvariantCulture,
                    $"{i} {F(p.Position.X)} {F(p.Position.Y)} {F(p.Position.Z)} {F(p.Rotation.X)} {F(p.Rotation.Y)} {F(p.Rotation.Z)}\n");
            }
        }

        sb.Append("end\n");
        if (Triangles.Count > 0)
        {
            sb.Append("triangles\n");
            foreach (var triangle in Triangles)
            {
                sb.Append(triangle.Material).Append('\n');
                foreach (var v in triangle.Vertices)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $"{v.ParentBone} {F(v.Position.X)} {F(v.Position.Y)} {F(v.Position.Z)} {F(v.Normal.X)} {F(v.Normal.Y)} {F(v.Normal.Z)} {F(v.TexCoord.X)} {F(v.TexCoord.Y)}");
                    sb.Append(CultureInfo.InvariantCulture, $" {v.Links.Count}");
                    foreach (var link in v.Links)
                        sb.Append(CultureInfo.InvariantCulture, $" {link.Bone} {F(link.Weight)}");
                    sb.Append('\n');
                }
            }

            sb.Append("end\n");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string F(float value) => value.ToString("0.000000", CultureInfo.InvariantCulture);

    private static float[] Floats(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
}

public static class SmdMath
{
    /// <summary>
    /// studiomdl's AngleMatrix for SMD angles: rotate about x, then y, then z. System.Numerics matrices are row-vector
    /// (v * M), so the product reads in application order.
    /// </summary>
    public static Matrix4x4 FromPose(Vector3 position, Vector3 rotation)
    {
        var m = Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z);
        m.Translation = position;
        return m;
    }

    /// <summary>Inverse of FromPose for a rigid matrix</summary>
    public static SmdBonePose ToPose(Matrix4x4 m)
    {
        float sy = Math.Clamp(-m.M13, -1f, 1f);
        float y = MathF.Asin(sy);
        float x, z;
        if (MathF.Abs(sy) < 0.99999f)
        {
            x = MathF.Atan2(m.M23, m.M33);
            z = MathF.Atan2(m.M12, m.M11);
        }
        else
        {
            x = 0f;
            z = MathF.Atan2(-m.M21, m.M22);
        }

        return new SmdBonePose(m.Translation, new Vector3(x, y, z));
    }

    public static Matrix4x4 Inverse(Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inverse);
        return inverse;
    }

    /// <summary>Shortest rotation taking direction a to direction b</summary>
    public static Quaternion RotationBetween(Vector3 a, Vector3 b)
    {
        a = Vector3.Normalize(a);
        b = Vector3.Normalize(b);
        float d = Vector3.Dot(a, b);
        if (d > 0.999999f)
            return Quaternion.Identity;
        if (d < -0.999999f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }

        var c = Vector3.Cross(a, b);
        var q = new Quaternion(c, 1f + d);
        return Quaternion.Normalize(q);
    }
}
