namespace PlayerModelSpike;

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Orthographic z-buffered software render of SMD meshes (one row per SMD; front, side and three-quarter views), all at
/// the same scale, so retarget and pose results can be looked at without hlmv.
/// </summary>
public static class Renderer
{
    private const int ViewWidth = 300;
    private const int ViewHeight = 400;

    // Model space: +X forward, +Y left, +Z up. Each view looks at the model from the given yaw (0 = from the front).
    private static readonly float[] ViewYaws = [0f, 90f, 35f];

    public static void Render(string outPath, List<Smd> meshes, List<string> names)
    {
        var all = meshes.SelectMany(m => m.Triangles).SelectMany(t => t.Vertices).Select(v => v.Position).ToList();
        float minZ = all.Min(p => p.Z), maxZ = all.Max(p => p.Z);
        float radius = all.Max(p => MathF.Sqrt(p.X * p.X + p.Y * p.Y));
        float scale = Math.Min((ViewHeight - 20) / (maxZ - minZ), (ViewWidth - 20) / (2 * radius));

        using var image = new Image<Rgba32>(ViewWidth * ViewYaws.Length, ViewHeight * meshes.Count, new Rgba32(32, 34, 40));
        for (int row = 0; row < meshes.Count; row++)
        {
            var tint = Palette[row % Palette.Length];
            for (int col = 0; col < ViewYaws.Length; col++)
                Draw(image, meshes[row], ViewYaws[col], col * ViewWidth, row * ViewHeight, scale, maxZ, tint);
            Console.WriteLine($"row {row}: {names[row]}");
        }

        image.SaveAsPng(outPath);
    }

    private static readonly Vector3[] Palette = [new(0.85f, 0.85f, 0.9f), new(0.95f, 0.75f, 0.45f), new(0.55f, 0.85f, 0.6f), new(0.6f, 0.7f, 1f)];

    private static void Draw(Image<Rgba32> image, Smd mesh, float yawDegrees, int ox, int oy, float scale, float maxZ, Vector3 tint)
    {
        float yaw = yawDegrees * MathF.PI / 180f;
        // Camera on a circle around z, looking at the origin: view direction and the screen's right vector
        var toCamera = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
        var right = Vector3.Cross(-toCamera, Vector3.UnitZ);
        var depth = new float[ViewWidth * ViewHeight];
        Array.Fill(depth, float.MinValue);
        var light = Vector3.Normalize(toCamera + new Vector3(0f, 0f, 0.6f) + right * 0.3f);

        foreach (var t in mesh.Triangles)
        {
            var p = t.Vertices.Select(v => v.Position).ToArray();
            var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (n.LengthSquared() < 1e-12f)
                continue;
            n = Vector3.Normalize(n);
            float shade = 0.25f + 0.75f * MathF.Abs(Vector3.Dot(n, light));
            var color = new Rgba32(tint.X * shade, tint.Y * shade, tint.Z * shade);

            var s = p.Select(q => new Vector3(
                ViewWidth / 2f + Vector3.Dot(q, right) * scale,
                10f + (maxZ - q.Z) * scale,
                Vector3.Dot(q, toCamera))).ToArray();

            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(s[0].X, MathF.Min(s[1].X, s[2].X))));
            int x1 = Math.Min(ViewWidth - 1, (int)MathF.Ceiling(MathF.Max(s[0].X, MathF.Max(s[1].X, s[2].X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(s[0].Y, MathF.Min(s[1].Y, s[2].Y))));
            int y1 = Math.Min(ViewHeight - 1, (int)MathF.Ceiling(MathF.Max(s[0].Y, MathF.Max(s[1].Y, s[2].Y))));
            float area = Edge(s[0], s[1], s[2].X, s[2].Y);
            if (MathF.Abs(area) < 1e-6f)
                continue;

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = Edge(s[1], s[2], px, py) / area, w1 = Edge(s[2], s[0], px, py) / area, w2 = Edge(s[0], s[1], px, py) / area;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                        continue;
                    float z = w0 * s[0].Z + w1 * s[1].Z + w2 * s[2].Z;
                    int i = y * ViewWidth + x;
                    if (z <= depth[i])
                        continue;
                    depth[i] = z;
                    image[ox + x, oy + y] = color;
                }
            }
        }

        // Ground line at z = 0
        int ground = (int)(10f + maxZ * scale);
        if (ground >= 0 && ground < ViewHeight)
        {
            for (int x = 0; x < ViewWidth; x += 2)
                image[ox + x, oy + ground] = new Rgba32(90, 90, 100);
        }
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
