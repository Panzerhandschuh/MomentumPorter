namespace MomentumPorter.Fixes;

using System.Text.RegularExpressions;
using MomentumPorter.Assets.Weapons;
using MomentumPorter.Porting;

/// <summary>
/// The submission docs' "Missing Skyboxes" fix. Momentum draws the <c>Sky</c> shader as <c>Sky_HDR</c> whenever HDR
/// is on, and <c>Sky_HDR</c> only samples <c>$hdrbasetexture</c> or <c>$hdrcompressedtexture</c>, so a packed sky
/// material with nothing but a <c>$basetexture</c> comes out as the error texture (surf_vale2). Those are switched to
/// <c>Sky_SDR</c>, which draws <c>$basetexture</c> and is what <c>Sky</c> falls back to without HDR anyway.
/// </summary>
public static partial class SkyMaterials
{
    // Shaders Momentum draws with Sky_HDR
    private static readonly string[] HdrSkyShaders = ["sky", "sky_hdr_dx9"];

    private static readonly string[] HdrTextureKeys = ["$hdrbasetexture", "$hdrcompressedtexture", "$hdrcompressedtexture0"];

    // The shader name: the first token, after any comments
    [GeneratedRegex(@"\A(﻿?(?:\s|//[^\n]*)*)(""[^""]*""|[^\s{""]+)")]
    private static partial Regex ShaderName();

    /// <summary>
    /// Whether a sky material draws as the error texture in Momentum, and if so its text with the shader switched to
    /// <c>Sky_SDR</c>.
    /// </summary>
    public static string? Fix(string vmt)
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

        if (!HdrSkyShaders.Contains(material.Key, StringComparer.OrdinalIgnoreCase)
            || HdrTextureKeys.Any(x => material.GetString(x) is not null)
            || material.GetString("$basetexture") is null)
            return null;

        Match match = ShaderName().Match(vmt);
        return match.Success ? string.Concat(match.Groups[1].Value, "\"Sky_SDR\"", vmt.AsSpan(match.Length)) : null;
    }

    /// <summary>
    /// Switches the map's packed sky materials that would draw as the error texture to <c>Sky_SDR</c>. Returns how
    /// many were.
    /// </summary>
    public static int Apply(PortContext context)
    {
        var fixes = new List<(string Path, string Text)>();
        foreach (string path in context.ListPakFiles())
        {
            if (!path.StartsWith("materials/skybox/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
                continue;

            if (context.ReadPakFile(path) is { } vmt && Fix(vmt) is { } fixedVmt)
                fixes.Add((path, fixedVmt));
        }

        foreach ((string path, string text) in fixes)
            context.WritePakFile(path, text);

        if (fixes.Count > 0)
            context.Report.Change("sky materials switched to Sky_SDR, which Momentum draws without HDR textures", fixes.Count);

        return fixes.Count;
    }
}
