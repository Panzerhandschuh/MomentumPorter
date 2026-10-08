namespace MomentumPorter.Assets.Models;

using System.Globalization;
using System.Numerics;
using System.Text;

public sealed record SmdBone(string Name, int Parent);

/// <summary>A bone's position and rotation (radians about x, then y, then z) relative to its parent.</summary>
public readonly record struct SmdBonePose(Vector3 Position, Vector3 Rotation);

public readonly record struct SmdVertex(int Bone, Vector3 Position, Vector3 Normal, Vector2 TexCoord);

/// <summary>A triangle, its vertices counterclockwise seen from the front.</summary>
public sealed record SmdTriangle(string Material, SmdVertex A, SmdVertex B, SmdVertex C);

/// <summary>
/// Writes Valve SMD files: reference meshes (a skeleton pose and triangles) and animations (a pose per frame).
/// </summary>
public static class SmdWriter
{
    public static string WriteReference(IReadOnlyList<SmdBone> bones, IReadOnlyList<SmdBonePose> pose, IEnumerable<SmdTriangle> triangles)
    {
        var smd = new StringBuilder();
        WriteSkeleton(smd, bones, [pose]);
        smd.Append("triangles\n");
        foreach (SmdTriangle triangle in triangles)
        {
            smd.Append(triangle.Material).Append('\n');
            foreach (SmdVertex vertex in new[] { triangle.A, triangle.B, triangle.C })
            {
                smd.Append(CultureInfo.InvariantCulture, $"{vertex.Bone} {Format(vertex.Position)} {Format(vertex.Normal)} {vertex.TexCoord.X:0.######} {vertex.TexCoord.Y:0.######}\n");
            }
        }

        smd.Append("end\n");
        return smd.ToString();
    }

    public static string WriteAnimation(IReadOnlyList<SmdBone> bones, IReadOnlyList<IReadOnlyList<SmdBonePose>> frames)
    {
        var smd = new StringBuilder();
        WriteSkeleton(smd, bones, frames);
        return smd.ToString();
    }

    private static void WriteSkeleton(StringBuilder smd, IReadOnlyList<SmdBone> bones, IReadOnlyList<IReadOnlyList<SmdBonePose>> frames)
    {
        smd.Append("version 1\nnodes\n");
        for (int i = 0; i < bones.Count; i++)
            smd.Append(CultureInfo.InvariantCulture, $"{i} \"{bones[i].Name}\" {bones[i].Parent}\n");

        smd.Append("end\nskeleton\n");
        for (int frame = 0; frame < frames.Count; frame++)
        {
            smd.Append(CultureInfo.InvariantCulture, $"time {frame}\n");
            for (int i = 0; i < bones.Count; i++)
                smd.Append(CultureInfo.InvariantCulture, $"{i} {Format(frames[frame][i].Position)} {Format(frames[frame][i].Rotation)}\n");
        }

        smd.Append("end\n");
    }

    public static string Format(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.######} {v.Y:0.######} {v.Z:0.######}");
}
