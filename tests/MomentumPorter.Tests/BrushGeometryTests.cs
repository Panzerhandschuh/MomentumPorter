namespace MomentumPorter.Tests;

using System.Numerics;
using LibBSP;
using MomentumPorter.Bsp;
using MomentumPorter.Steam;
using Xunit;

public class BrushGeometryTests
{
    [Fact]
    public void IgnoresMisplacedAxialEdgeBevels()
    {
        // bhop_ivy_final's bhop trigger *717 ends in a long, thin sloped brush that vbsp gave edge bevels snapped to +Z
        // without moving them, 25 units below its bottom. Its real top is 12, and it holds points along the slope.
        if (FindCssMap("bhop_ivy_final") is not { } path)
            return;

        var bsp = new BSP(new FileInfo(path));
        for (int brush = 0; brush < bsp.Brushes.Count; brush++)
        {
            (Vector3 mins, Vector3 maxs) = BrushGeometry.GetBrushBounds(bsp, brush)!.Value;
            Assert.True(mins.X <= maxs.X && mins.Y <= maxs.Y && mins.Z <= maxs.Z, $"brush {brush}: {mins} {maxs}");
        }

        var point = new Vector3(826, -892.6f, -1);
        int path717 = Assert.Single(BrushGeometry.GetBrushes(bsp, 717), x => BrushGeometry.GetBrushBounds(bsp, x.Brush) is (Vector3 mins, Vector3 maxs)
            && mins.X <= point.X && point.X <= maxs.X).Brush;
        (Vector3 pathMins, Vector3 pathMaxs) = BrushGeometry.GetBrushBounds(bsp, path717)!.Value;
        Assert.Equal(-14, pathMins.Z, 0.1f);
        Assert.Equal(12, pathMaxs.Z, 0.1f);
        Assert.True(BrushGeometry.Contains(bsp, path717, point));
        (float bottom, float top) = BrushGeometry.GetColumnSpan(bsp, path717, point.X, point.Y)!.Value;
        Assert.Equal(-2.06f, bottom, 0.1f);
        Assert.Equal(-0.02f, top, 0.1f);
    }

    private static string? FindCssMap(string name)
    {
        GameLocation? css = new SteamLocator().Find(KnownGame.CounterStrikeSource);
        string? path = css?.ContentDirectory is { } content ? Path.Combine(content, "maps", name + ".bsp") : null;
        return path is not null && File.Exists(path) ? path : null;
    }
}
