namespace MomentumPorter.Games.Gokz;

using System.Numerics;
using BSPConvert.Lib.Zones;
using LibBSP;

/// <summary>
/// Builds Momentum zone regions (a 2D polygon extruded from a bottom height) from brush entities.
/// </summary>
public static class ZoneShapes
{
    // The engine's ValidateRegionPolygon limits (mom_zone_runtime_data.cpp)
    public const float MinPointDistance = 4;
    public const double MinAngleDegrees = 15;
    public const int MaxPoints = 64;

    // A region's height has to be more than 1 unit. Boxes smaller than this in any direction are grown to it.
    public const float MinSize = 8;

    private const float VerticalEpsilon = 0.01f;

    /// <summary>
    /// One region per brush of the entity's model, in world space. Each brush's outline is its bounds clipped by its
    /// vertical sides, so sloped sides are approximated by the brush's full height. Falls back to the model's bounds
    /// when the brushes can't be read.
    /// </summary>
    public static List<Region> FromBrushEntity(BSP bsp, Entity entity)
    {
        int model = entity.ModelNumber;
        if (model <= 0 || model >= bsp.Models.Count)
            return [];

        // Models of entities with an origin are relative to it
        Vector3 origin = entity.Origin;
        var regions = new List<Region>();
        foreach (int brush in GetBrushes(bsp, model))
        {
            if (FromBrush(bsp, brush, origin) is { } region)
                regions.Add(region);
        }

        if (regions.Count == 0)
            regions.Add(Box(bsp.Models[model].Minimums + origin, bsp.Models[model].Maximums + origin));

        return regions;
    }

    /// <summary>
    /// The bounds of the entity's model in world space, or null if it has no brush model.
    /// </summary>
    public static (Vector3 Mins, Vector3 Maxs)? GetBounds(BSP bsp, Entity entity)
    {
        int model = entity.ModelNumber;
        if (model <= 0 || model >= bsp.Models.Count)
            return null;

        Vector3 origin = entity.Origin;
        return (bsp.Models[model].Minimums + origin, bsp.Models[model].Maximums + origin);
    }

    /// <summary>
    /// A box region, grown to <see cref="MinSize"/> where it's smaller.
    /// </summary>
    public static Region Box(Vector3 mins, Vector3 maxs)
    {
        (mins, maxs) = Grow(mins, maxs);
        return new Region
        {
            Points = Rectangle(mins, maxs).Select(p => new[] { p.X, p.Y }).ToList(),
            Bottom = mins.Z,
            Height = maxs.Z - mins.Z,
        };
    }

    /// <summary>
    /// Whether the engine accepts the polygon: enough points, none too close together, no sharp or straight angles.
    /// </summary>
    public static bool IsValidPolygon(IReadOnlyList<Vector2> points)
    {
        int count = points.Count;
        if (count < 3 || count > MaxPoints)
            return false;

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (Vector2.Distance(points[i], points[j]) < MinPointDistance)
                    return false;
            }
        }

        double minAngle = MinAngleDegrees * Math.PI / 180;
        for (int i = 0; i < count; i++)
        {
            Vector2 prev = points[(i - 1 + count) % count] - points[i];
            Vector2 next = points[(i + 1) % count] - points[i];
            double angle = Math.Acos(Math.Clamp(Vector2.Dot(prev, next) / (prev.Length() * next.Length()), -1, 1));
            if (angle < minAngle || angle > Math.PI - 0.0001)
                return false;
        }

        return true;
    }

    public static bool IsValidPolygon(Region region)
    {
        return IsValidPolygon(region.Points.Select(p => new Vector2(p[0], p[1])).ToList());
    }

    private static Region? FromBrush(BSP bsp, int brushIndex, Vector3 origin)
    {
        Brush brush = bsp.Brushes[brushIndex];
        var mins = new Vector3(float.NaN);
        var maxs = new Vector3(float.NaN);
        var vertical = new List<PlaneBSP>();

        for (int i = 0; i < brush.NumSides; i++)
        {
            PlaneBSP plane = bsp.Planes[bsp.BrushSides[brush.FirstSideIndex + i].PlaneIndex];
            Vector3 n = plane.Normal;
            float d = plane.Distance;

            // vbsp gives every brush its six axial planes, which are its bounds
            if (n == Vector3.UnitX) maxs.X = d;
            else if (n == -Vector3.UnitX) mins.X = -d;
            else if (n == Vector3.UnitY) maxs.Y = d;
            else if (n == -Vector3.UnitY) mins.Y = -d;
            else if (n == Vector3.UnitZ) maxs.Z = d;
            else if (n == -Vector3.UnitZ) mins.Z = -d;
            else if (Math.Abs(n.Z) < VerticalEpsilon) vertical.Add(plane);
        }

        if (float.IsNaN(mins.X + mins.Y + mins.Z + maxs.X + maxs.Y + maxs.Z) || maxs.X <= mins.X || maxs.Y <= mins.Y || maxs.Z <= mins.Z)
            return null;

        Region box = Box(mins + origin, maxs + origin);
        if (vertical.Count == 0 || maxs.X - mins.X < MinSize || maxs.Y - mins.Y < MinSize)
            return box;

        // Brush outline (counter-clockwise), clipped by each vertical side's inside half-space n.p <= d
        List<Vector2> polygon = Rectangle(mins, maxs);
        foreach (PlaneBSP plane in vertical)
        {
            polygon = Clip(polygon, new Vector2(plane.Normal.X, plane.Normal.Y), plane.Distance);
            if (polygon.Count < 3)
                return box;
        }

        polygon = Simplify(polygon);
        if (!IsValidPolygon(polygon))
            return box;

        return new Region
        {
            Points = polygon.Select(p => new[] { p.X + origin.X, p.Y + origin.Y }).ToList(),
            Bottom = box.Bottom,
            Height = box.Height,
        };
    }

    // The brushes in a model's collision tree, in index order
    private static IEnumerable<int> GetBrushes(BSP bsp, int model)
    {
        var brushes = new SortedSet<int>();
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
                Leaf leaf = bsp.Leaves[-1 - node];
                for (int i = 0; i < leaf.NumMarkBrushIndices; i++)
                    brushes.Add((int)bsp.LeafBrushes[leaf.FirstMarkBrushIndex + i]);

                continue;
            }

            stack.Push(bsp.Nodes[node].Child1Index);
            stack.Push(bsp.Nodes[node].Child2Index);
        }

        return brushes;
    }

    private static (Vector3 Mins, Vector3 Maxs) Grow(Vector3 mins, Vector3 maxs)
    {
        Vector3 center = (mins + maxs) / 2;
        Vector3 half = Vector3.Max((maxs - mins) / 2, new Vector3(MinSize / 2));
        return (center - half, center + half);
    }

    private static List<Vector2> Rectangle(Vector3 mins, Vector3 maxs)
    {
        return [new(mins.X, maxs.Y), new(mins.X, mins.Y), new(maxs.X, mins.Y), new(maxs.X, maxs.Y)];
    }

    // Sutherland-Hodgman against one half-space
    private static List<Vector2> Clip(List<Vector2> polygon, Vector2 normal, float dist)
    {
        var output = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            float currentDist = Vector2.Dot(normal, current) - dist;
            float nextDist = Vector2.Dot(normal, next) - dist;
            bool currentInside = currentDist <= VerticalEpsilon;
            bool nextInside = nextDist <= VerticalEpsilon;

            if (currentInside)
                output.Add(current);

            if (currentInside != nextInside)
                output.Add(current + currentDist / (currentDist - nextDist) * (next - current));
        }

        return output;
    }

    // Drops points that are on top of their neighbour or in a straight line with them
    private static List<Vector2> Simplify(List<Vector2> polygon)
    {
        var points = new List<Vector2>(polygon);
        bool changed = true;
        while (changed && points.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 prev = points[(i - 1 + points.Count) % points.Count];
                Vector2 next = points[(i + 1) % points.Count];
                Vector2 a = prev - points[i];
                Vector2 b = next - points[i];
                bool duplicate = a.Length() < 0.01f;
                bool straight = Math.Abs(a.X * b.Y - a.Y * b.X) < 0.001f * a.Length() * b.Length() && Vector2.Dot(a, b) < 0;
                if (duplicate || straight)
                {
                    points.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        return points;
    }
}
