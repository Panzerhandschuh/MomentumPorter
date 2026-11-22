namespace MomentumPorter.Fixes;

using System.Globalization;
using System.Text.RegularExpressions;
using LibBSP;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Porting;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;

/// <summary>
/// Source 2013 ran HDR in integer mode, which stored float cubemaps at 1/16 of their brightness, and its
/// <c>Refract</c> shader never scaled them back up. Maps tuned <c>$envmaptint</c> against that, so with Momentum's
/// float HDR their Refract reflections come out 16 times as bright: surf_surreal's island water (tint 10) is nearly
/// white. The packed Refract materials reflecting a float cubemap get their tint divided by 16.
/// </summary>
public static partial class RefractEnvmaps
{
    private const float Scale = 1.0f / 16.0f;

    private const int FloatHdrFormat = 24; // IMAGE_FORMAT_RGBA16161616F

    // Marks a material as already scaled, so porting a ported map doesn't dim it again; the engine ignores % keys
    private const string ScaledKey = "%porter_envmaptint_scaled";

    // Refract, and the DX-level names that fall back to it
    [GeneratedRegex(@"\Arefract(_dx\d+)?\z", RegexOptions.IgnoreCase)]
    private static partial Regex RefractShader();

    /// <summary>
    /// The material's text with its envmap tint scaled down, or null when it isn't a Refract material with an envmap
    /// that <paramref name="isFloatCubemap"/> says is a float cubemap.
    /// </summary>
    public static string? Fix(string vmt, Func<string, bool> isFloatCubemap)
    {
        KeyValuesNode material;
        try
        {
            material = KeyValuesNode.Parse(vmt);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!RefractShader().IsMatch(material.Key) || material.GetString(ScaledKey) is not null || FindValues(material, "$envmap").FirstOrDefault() is not { } envmap || !isFloatCubemap(envmap))
            return null;

        // Fallback blocks that the shader merges in can set their own tint
        List<KeyValuesNode> tints = FindLeaves(material, "$envmaptint").ToList();
        if (material.GetString("$envmaptint") is null)
            tints.Add(material.Set("$envmaptint", "1"));

        foreach (KeyValuesNode tint in tints)
            tint.Value = ScaleTint(tint.Value!);

        material.Set(ScaledKey, "1");
        return material.Write();
    }

    /// <summary>
    /// The tint, as written in a VMT, that comes out 1/16 as bright. Components up to 1 are gamma encoded and the
    /// shader converts them to linear, larger ones are used as they are.
    /// </summary>
    public static string ScaleTint(string tint)
    {
        float[] gamma = ParseColor(tint);
        IEnumerable<float> scaled = gamma.Select(x =>
        {
            float linear = (x > 1.0f ? x : MathF.Pow(Math.Max(x, 0.0f), 2.2f)) * Scale;
            return linear > 1.0f ? linear : MathF.Pow(linear, 1.0f / 2.2f);
        });
        return "[" + string.Join(" ", scaled.Select(x => x.ToString("0.####", CultureInfo.InvariantCulture))) + "]";
    }

    /// <summary>
    /// Scales the envmap tint of the map's packed Refract materials that reflect a float cubemap. Returns how many
    /// were.
    /// </summary>
    public static int Apply(PortContext context)
    {
        if (context.Bsp.PakFile is not { Data.Length: > 0 } pak)
            return 0;

        var fixes = new List<(string Path, string Text)>();
        using (ZipArchive zip = (ZipArchive)ZipArchive.OpenArchive(new MemoryStream(pak.Data)))
        {
            Dictionary<string, ZipArchiveEntry> entries = zip.Entries
                .Where(x => !x.IsDirectory && x.Key is not null)
                .GroupBy(x => x.Key!.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            bool IsFloatFile(string path) => entries.TryGetValue(path, out ZipArchiveEntry? entry) && IsFloat(PortContext.ReadEntry(entry));

            // env_cubemap is whichever cubemap the map built nearest the surface
            string builtCubemaps = $"materials/maps/{context.MapName}/";
            bool builtFloat = entries.Keys.Any(x =>
                x.StartsWith(builtCubemaps, StringComparison.OrdinalIgnoreCase) && x.EndsWith(".hdr.vtf", StringComparison.OrdinalIgnoreCase) && IsFloatFile(x));

            bool IsFloatCubemap(string envmap)
            {
                if (envmap.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase))
                    return builtFloat;

                // HDR loads the .hdr.vtf when there is one
                string path = "materials/" + envmap.Replace('\\', '/').TrimStart('/');
                if (path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase))
                    path = path[..^4];

                return entries.ContainsKey(path + ".hdr.vtf") ? IsFloatFile(path + ".hdr.vtf") : IsFloatFile(path + ".vtf");
            }

            foreach ((string path, ZipArchiveEntry entry) in entries)
            {
                if (!path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
                    continue;

                string vmt = Entity.TextEncoding.GetString(PortContext.ReadEntry(entry));
                if (Fix(vmt, IsFloatCubemap) is { } fixedVmt)
                    fixes.Add((path, fixedVmt));
            }
        }

        if (fixes.Count > 0)
        {
            context.EditPakFiles(zip =>
            {
                foreach ((string path, string text) in fixes)
                {
                    if (PortContext.FindEntry(zip, path) is { } existing)
                        zip.RemoveEntry(existing);

                    zip.AddEntry(path, new MemoryStream(Entity.TextEncoding.GetBytes(text)), closeStream: true);
                }
            });
        }

        if (fixes.Count > 0)
            context.Report.Change("Refract materials' envmap tint divided by 16 to match Source 2013's integer HDR", fixes.Count);

        return fixes.Count;
    }

    private static bool IsFloat(byte[]? vtf) =>
        vtf is { Length: >= 56 } && vtf[0] == 'V' && vtf[1] == 'T' && vtf[2] == 'F' && BitConverter.ToInt32(vtf, 52) == FloatHdrFormat;

    private static IEnumerable<KeyValuesNode> FindLeaves(KeyValuesNode block, string key)
    {
        foreach (KeyValuesNode child in block.Children)
        {
            if (child.IsBlock)
            {
                if (child.Key.Equals("Proxies", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (KeyValuesNode leaf in FindLeaves(child, key))
                    yield return leaf;
            }
            else if (child.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<string> FindValues(KeyValuesNode block, string key) =>
        FindLeaves(block, key).Select(x => x.Value!).Where(x => x.Length > 0);

    // "[r g b]" holds floats, "{r g b}" holds 0-255 integers, and a single number sets all three
    private static float[] ParseColor(string text)
    {
        string trimmed = text.Trim();
        bool bytes = trimmed.StartsWith('{');
        float[] values = trimmed.Trim('[', ']', '{', '}')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0.0f)
            .Select(x => bytes ? x / 255.0f : x)
            .ToArray();

        return values.Length switch
        {
            0 => [1.0f, 1.0f, 1.0f],
            1 or 2 => [values[0], values[0], values[0]],
            _ => values[..3],
        };
    }
}
