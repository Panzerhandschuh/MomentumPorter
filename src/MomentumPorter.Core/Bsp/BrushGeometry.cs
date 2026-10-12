namespace MomentumPorter.Bsp;

using System.Numerics;
using LibBSP;

/// <summary>
/// Reads the brushes of a model's collision tree (the world's, model 0, or a brush entity's "*N") and their bounds.
/// </summary>
public static class BrushGeometry
{
    // How far outside one of its planes a corner can be and still count as part of the brush
    private const float CornerEpsilon = 0.01f;

    /// <summary>
    /// The brushes in a model's collision tree, sorted by brush index, with the first leaf each one was found in.
    /// </summary>
    public static List<(int Brush, int Leaf)> GetBrushes(BSP bsp, int model)
    {
        Lump<Node> nodes = bsp.Nodes;
        Lump<Leaf> leaves = bsp.Leaves;
        NumList leafBrushes = bsp.LeafBrushes;

        var brushes = new SortedDictionary<int, int>();
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(bsp.Models[model].HeadNodeIndex);

        while (stack.Count > 0)
        {
            int node = stack.Pop();
            if (!visited.Add(node))
                continue;

            if (node < 0)
            {
                int leafIndex = -1 - node;
                Leaf leaf = leaves[leafIndex];
                for (int i = 0; i < leaf.NumMarkBrushIndices; i++)
                    brushes.TryAdd((int)leafBrushes[leaf.FirstMarkBrushIndex + i], leafIndex);

                continue;
            }

            stack.Push(nodes[node].Child1Index);
            stack.Push(nodes[node].Child2Index);
        }

        return brushes.Select(x => (x.Key, x.Value)).ToList();
    }

    /// <summary>
    /// A brush's bounds, in the model's own space. Built from its axial planes, which compiled brushes always have (as
    /// bevels if nothing else), or else from its corners. Null for a brush with no volume.
    /// </summary>
    public static (Vector3 Mins, Vector3 Maxs)? GetBrushBounds(BSP bsp, int brush)
    {
        List<PlaneBSP> planes = GetBrushPlanes(bsp, brush);

        float?[] mins = new float?[3];
        float?[] maxs = new float?[3];
        foreach (PlaneBSP plane in planes)
        {
            Vector3 normal = plane.Normal;
            for (int axis = 0; axis < 3; axis++)
            {
                if (normal[axis] == 1 && normal.LengthSquared() == 1)
                    maxs[axis] = plane.Distance;
                else if (normal[axis] == -1 && normal.LengthSquared() == 1)
                    mins[axis] = -plane.Distance;
            }
        }

        if (mins.All(x => x is not null) && maxs.All(x => x is not null))
            return (new Vector3(mins[0]!.Value, mins[1]!.Value, mins[2]!.Value), new Vector3(maxs[0]!.Value, maxs[1]!.Value, maxs[2]!.Value));

        return GetCornerBounds(planes);
    }

    /// <summary>
    /// Whether a point (in the model's own space) is inside a brush: behind every one of its planes.
    /// </summary>
    public static bool Contains(BSP bsp, int brush, Vector3 point)
    {
        List<PlaneBSP> planes = GetBrushPlanes(bsp, brush);
        return planes.Count > 0 && planes.All(x => Vector3.Dot(x.Normal, point) - x.Distance <= CornerEpsilon);
    }

    /// <summary>
    /// Where the vertical line through (x, y) (in the model's own space) passes through a brush: the heights it enters
    /// and leaves it at, which on a sloped brush depend on where the line is. Null if the line misses the brush.
    /// </summary>
    public static (float Bottom, float Top)? GetColumnSpan(BSP bsp, int brush, float x, float y)
    {
        List<PlaneBSP> planes = GetBrushPlanes(bsp, brush);
        if (planes.Count == 0)
            return null;

        float bottom = float.MinValue;
        float top = float.MaxValue;
        foreach (PlaneBSP plane in planes)
        {
            // Inside is normal . (x, y, z) <= distance
            Vector3 normal = plane.Normal;
            float rest = plane.Distance - normal.X * x - normal.Y * y;
            if (MathF.Abs(normal.Z) < 1e-6f)
            {
                if (rest < -CornerEpsilon)
                    return null;
            }
            else if (normal.Z > 0)
            {
                top = MathF.Min(top, rest / normal.Z);
            }
            else
            {
                bottom = MathF.Max(bottom, rest / normal.Z);
            }
        }

        return bottom <= top ? (bottom, top) : null;
    }

    private static List<PlaneBSP> GetBrushPlanes(BSP bsp, int brush)
    {
        Brush brushData = bsp.Brushes[brush];
        Lump<BrushSide> sides = bsp.BrushSides;
        Lump<PlaneBSP> planes = bsp.Planes;

        var result = new List<PlaneBSP>(brushData.NumSides);
        for (int side = brushData.FirstSideIndex; side < brushData.FirstSideIndex + brushData.NumSides; side++)
            result.Add(planes[sides[side].PlaneIndex]);

        return result;
    }

    // The corners are where three of the planes meet, inside all the others
    private static (Vector3 Mins, Vector3 Maxs)? GetCornerBounds(List<PlaneBSP> planes)
    {
        var mins = new Vector3(float.MaxValue);
        var maxs = new Vector3(float.MinValue);
        bool any = false;

        for (int a = 0; a < planes.Count; a++)
        {
            for (int b = a + 1; b < planes.Count; b++)
            {
                for (int c = b + 1; c < planes.Count; c++)
                {
                    if (Intersect(planes[a], planes[b], planes[c]) is not Vector3 corner)
                        continue;

                    if (planes.Any(x => Vector3.Dot(x.Normal, corner) - x.Distance > CornerEpsilon))
                        continue;

                    mins = Vector3.Min(mins, corner);
                    maxs = Vector3.Max(maxs, corner);
                    any = true;
                }
            }
        }

        return any ? (mins, maxs) : null;
    }

    private static Vector3? Intersect(PlaneBSP a, PlaneBSP b, PlaneBSP c)
    {
        Vector3 bc = Vector3.Cross(b.Normal, c.Normal);
        float denominator = Vector3.Dot(a.Normal, bc);
        if (MathF.Abs(denominator) < 1e-6f)
            return null;

        return (a.Distance * bc + b.Distance * Vector3.Cross(c.Normal, a.Normal) + c.Distance * Vector3.Cross(a.Normal, b.Normal)) / denominator;
    }
}
