namespace MomentumPorter.Bsp;

using System.Numerics;

/// <summary>
/// Source's angle conventions: (pitch, yaw, roll) in degrees, as in <c>angles</c>, <c>movedir</c> and <c>pushdir</c>.
/// </summary>
public static class SourceAngles
{
    /// <summary>The forward vector of a set of angles (AngleVectors).</summary>
    public static Vector3 Forward(Vector3 angles)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(angles.X * MathF.PI / 180f);
        (float sinYaw, float cosYaw) = MathF.SinCos(angles.Y * MathF.PI / 180f);
        return new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
    }

    /// <summary>Rotates a direction from an entity's own space into the world by the entity's angles (VectorRotate with AngleMatrix).</summary>
    public static Vector3 Rotate(Vector3 direction, Vector3 angles)
    {
        if (angles == Vector3.Zero)
            return direction;

        (float sp, float cp) = MathF.SinCos(angles.X * MathF.PI / 180f);
        (float sy, float cy) = MathF.SinCos(angles.Y * MathF.PI / 180f);
        (float sr, float cr) = MathF.SinCos(angles.Z * MathF.PI / 180f);

        var row0 = new Vector3(cp * cy, sr * sp * cy + cr * -sy, cr * sp * cy + -sr * -sy);
        var row1 = new Vector3(cp * sy, sr * sp * sy + cr * cy, cr * sp * sy + -sr * cy);
        var row2 = new Vector3(-sp, sr * cp, cr * cp);
        return new Vector3(Vector3.Dot(direction, row0), Vector3.Dot(direction, row1), Vector3.Dot(direction, row2));
    }
}
